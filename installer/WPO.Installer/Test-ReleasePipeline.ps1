[CmdletBinding()]
param(
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $PSScriptRoot 'bin\Release\WPO.Installer.msi'
}
$validator = Join-Path $PSScriptRoot 'Validate-Msi.ps1'
if (-not (Test-Path -LiteralPath $MsiPath)) {
    throw "Test prerequisite MSI missing: $MsiPath"
}

$developmentOutput = & $validator -MsiPath $MsiPath *>&1
if (-not (($developmentOutput | Out-String) -match 'UNSIGNED DEVELOPMENT ARTIFACT')) {
    throw 'Development validation must succeed and emit the unsigned-distribution warning.'
}

$msiSignature = Get-AuthenticodeSignature -LiteralPath $MsiPath
$isTestSigned = $msiSignature.SignerCertificate -and $msiSignature.SignerCertificate.Subject -eq 'CN=WPO LOCAL TEST SIGNING - NOT FOR DISTRIBUTION'

if ($isTestSigned) {
    # Formal mode must reject the self-test certificate even when its thumbprint is supplied.
    $formalRejectedTestCert = $false
    try { & $validator -MsiPath $MsiPath -RequireSignature -ExpectedSignerThumbprint $msiSignature.SignerCertificate.Thumbprint *>&1 | Out-Null }
    catch { $formalRejectedTestCert = $true }
    if (-not $formalRejectedTestCert) { throw 'RequireSignature accepted a test-signed MSI as a formal release.' }

    $testOutput = & $validator -MsiPath $MsiPath -RequireSignature -AllowUntrustedTestSigner -ExpectedSignerThumbprint $msiSignature.SignerCertificate.Thumbprint *>&1
    if (-not (($testOutput | Out-String) -match 'TEST-SIGNED ARTIFACT')) {
        throw 'Test-signed validation must succeed and emit the not-for-distribution warning.'
    }

    $testRelease = Join-Path $PSScriptRoot 'bin\TestSignedRelease'
    if (Test-Path -LiteralPath (Join-Path $testRelease 'release-manifest.json')) {
        $testManifest = Get-Content (Join-Path $testRelease 'release-manifest.json') -Raw | ConvertFrom-Json
        if ($testManifest.releaseType -ne 'test-signed-not-for-distribution' -or
            -not (Test-Path (Join-Path $testRelease 'TEST-SIGNED-NOT-FOR-DISTRIBUTION.txt')) -or
            ($testManifest.artifacts | Where-Object { [string]::IsNullOrWhiteSpace($_.file) }).Count -ne 0 -or
            ((Get-Content (Join-Path $testRelease 'SHA256SUMS.txt')) -notmatch '^[A-F0-9]{64} \*\S+$').Count -ne 0) {
            throw 'TestSignedRelease output is not labelled correctly or has empty artifact names.'
        }
    }
}

$formalFailed = $false
try {
    & $validator -MsiPath $MsiPath -RequireSignature -ExpectedSignerThumbprint '0000000000000000000000000000000000000000' *>&1 | Out-Null
}
catch {
    $formalFailed = $true
}
if (-not $formalFailed) {
    throw 'RequireSignature unexpectedly accepted the current MSI with a bogus signer thumbprint.'
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("wpo-release-manifest-" + [guid]::NewGuid())
try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    $artifact = Join-Path $temporaryDirectory 'artifact.bin'
    [System.IO.File]::WriteAllBytes($artifact, [byte[]](1, 2, 3))
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    [ordered]@{ version = '0.0.0-test'; generatedUtc = [DateTime]::UtcNow.ToString('o'); signerThumbprint = 'ABCDEF'; artifacts = @([ordered]@{ file = 'artifact.bin'; sha256 = $hash }) } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $temporaryDirectory 'release-manifest.json') -Encoding utf8
    $manifestRaw = Get-Content (Join-Path $temporaryDirectory 'release-manifest.json') -Raw
    $manifest = $manifestRaw | ConvertFrom-Json
    if ($manifest.version -notmatch '^\d+\.\d+\.\d+' -or $manifestRaw -notmatch '"generatedUtc"\s*:\s*"[^"]+(Z|\+00:00)"' -or
        $manifest.artifacts.Count -ne 1 -or $manifest.artifacts[0].sha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'release-manifest.json format validation failed.'
    }
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Release pipeline executable tests passed.'
