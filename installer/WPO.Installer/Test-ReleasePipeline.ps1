[CmdletBinding()]
param(
    [string]$MsiPath = (Join-Path $PSScriptRoot 'bin\Release\WPO.Installer.msi')
)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Validate-Msi.ps1'
if (-not (Test-Path -LiteralPath $MsiPath)) {
    throw "Test prerequisite MSI missing: $MsiPath"
}

$developmentOutput = & $validator -MsiPath $MsiPath 3>&1
if ($LASTEXITCODE -ne 0 -or -not (($developmentOutput | Out-String) -match 'UNSIGNED DEVELOPMENT ARTIFACT')) {
    throw 'Development validation must succeed and emit the unsigned-distribution warning.'
}

$formalFailed = $false
try {
    & $validator -MsiPath $MsiPath -RequireSignature -ExpectedSignerThumbprint '0000000000000000000000000000000000000000' 3>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { $formalFailed = $true }
}
catch {
    $formalFailed = $true
}
if (-not $formalFailed) {
    throw 'RequireSignature unexpectedly accepted the current unsigned MSI.'
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
