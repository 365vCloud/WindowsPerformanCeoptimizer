<#
.SYNOPSIS
    Builds, code-signs (Authenticode, SHA-256, RFC3161 timestamp) and validates the
    Windows Performance Optimizer release artifacts (wpo-app.exe and WPO.Installer.msi).

.DESCRIPTION
    Formal release modes (output: bin\SignedRelease):
      -PfxPath <file> [-PfxPassword | -PfxPasswordEnvironmentVariable]   organisation certificate file
      -CertificateThumbprint <hex>                                       certificate already in a cert store / HSM

    Local pipeline self-test mode (output: bin\TestSignedRelease):
      -LocalTestSigning
        Generates a short-lived, NON-EXPORTABLE self-signed code-signing certificate whose subject is
        'CN=WPO LOCAL TEST SIGNING - NOT FOR DISTRIBUTION', signs the artifacts with it, and deletes the
        certificate (and its private key) afterwards. Windows will NOT trust these signatures. The mode
        exists only to prove the signing pipeline works end-to-end on a machine that has no production
        certificate. Its output must never be shipped to users.
#>
[CmdletBinding()]
param(
    [Parameter(ParameterSetName = 'Pfx', Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$PfxPath,
    [Parameter(ParameterSetName = 'Store', Mandatory)][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'LocalTest', Mandatory)][switch]$LocalTestSigning,
    [Parameter(ParameterSetName = 'Pfx')][SecureString]$PfxPassword,
    [Parameter(ParameterSetName = 'Pfx')][string]$PfxPasswordEnvironmentVariable,
    [ValidatePattern('^https?://')][string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SigntoolPath,
    [string]$ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
$isLocalTest = $PSCmdlet.ParameterSetName -eq 'LocalTest'
$testSubject = 'CN=WPO LOCAL TEST SIGNING - NOT FOR DISTRIBUTION'
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $PSScriptRoot $(if ($isLocalTest) { 'bin\TestSignedRelease' } else { 'bin\SignedRelease' })
}
elseif ($isLocalTest -and (Split-Path $ReleaseDirectory -Leaf) -ieq 'SignedRelease') {
    throw 'Test-signed output must not be written to a directory named SignedRelease.'
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exePath = Join-Path $repoRoot 'src-tauri\target\release\wpo-app.exe'
$msiPath = Join-Path $PSScriptRoot 'bin\Release\WPO.Installer.msi'
$temporaryCertificate = $null

# Toolchains installed per-user (rustup, Node.js) are often missing from a fresh non-interactive PATH.
foreach ($toolDir in @("$env:ProgramFiles\nodejs", "$env:USERPROFILE\.cargo\bin", "$env:ProgramFiles\dotnet")) {
    if ((Test-Path -LiteralPath $toolDir) -and (($env:Path -split ';') -notcontains $toolDir)) { $env:Path = "$toolDir;$env:Path" }
}
$dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { throw 'dotnet.exe was not found. Install the .NET SDK 8.0 or later.' }

function Find-Signtool {
    if ($SigntoolPath) { return (Resolve-Path -LiteralPath $SigntoolPath).Path }
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidate = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $candidate) { throw 'signtool.exe was not found. Install the Windows SDK or pass -SigntoolPath.' }
    return $candidate.FullName
}

function Get-RequiredPfxPassword {
    if ($PfxPassword) { return $PfxPassword }
    if ($PfxPasswordEnvironmentVariable) {
        $value = [Environment]::GetEnvironmentVariable($PfxPasswordEnvironmentVariable)
        if ([string]::IsNullOrEmpty($value)) { throw "The environment variable '$PfxPasswordEnvironmentVariable' is empty or missing." }
        return (ConvertTo-SecureString $value -AsPlainText -Force)
    }
    return (Read-Host 'Enter PFX password' -AsSecureString)
}

function Assert-Signature([string]$Path, [string]$Thumbprint, [string]$Subject) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    # A self-signed test certificate chains to an untrusted root, which PowerShell reports as UnknownError.
    $acceptedStatuses = if ($isLocalTest) { @('Valid', 'UnknownError') } else { @('Valid') }
    if ($acceptedStatuses -notcontains $signature.Status -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint.Replace(' ', '') -ne $Thumbprint.Replace(' ', '') -or
        $signature.SignerCertificate.Subject -ne $Subject -or -not $signature.TimeStamperCertificate) {
        throw "Post-signing validation failed for '$Path'. Status=$($signature.Status) $($signature.StatusMessage)"
    }
}

try {
    if ($isLocalTest) {
        Write-Warning 'LOCAL TEST SIGNING MODE: output is signed with an untrusted self-signed certificate and is NOT FOR DISTRIBUTION.'
        $temporaryCertificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $testSubject `
            -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature `
            -NotAfter ([DateTime]::UtcNow.AddDays(3)) -CertStoreLocation Cert:\CurrentUser\My
        $CertificateThumbprint = $temporaryCertificate.Thumbprint
    }
    elseif ($PSCmdlet.ParameterSetName -eq 'Pfx') {
        $password = Get-RequiredPfxPassword
        $temporaryCertificate = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $password -Exportable:$false
        if (-not $temporaryCertificate.HasPrivateKey) { throw 'The supplied PFX has no private key.' }
        $CertificateThumbprint = $temporaryCertificate.Thumbprint
    }
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object { $_.Thumbprint.Replace(' ', '') -eq $CertificateThumbprint.Replace(' ', '') -and $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $certificate) { throw "No signing certificate with private key was found for thumbprint '$CertificateThumbprint'." }
    if (-not $isLocalTest -and $certificate.Subject -eq $testSubject) {
        throw 'The local test certificate must not be used for a formal release.'
    }

    $signtool = Find-Signtool
    & $dotnet build (Join-Path $PSScriptRoot 'WPO.Installer.wixproj') -c Release -p:BuildTauriBeforeMsi=true
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exePath)) { throw 'Clean Tauri release build failed.' }

    & $signtool sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Signing the Tauri executable failed.' }
    Assert-Signature $exePath $certificate.Thumbprint $certificate.Subject
    $exeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash

    & $dotnet build (Join-Path $PSScriptRoot 'WPO.Installer.wixproj') -c Release -p:BuildTauriBeforeMsi=false -p:ExpectedTauriExeSha256=$exeHash
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msiPath)) { throw 'WiX MSI build failed.' }
    & $signtool sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $msiPath
    if ($LASTEXITCODE -ne 0) { throw 'Signing the MSI failed.' }
    Assert-Signature $msiPath $certificate.Thumbprint $certificate.Subject

    # Validate-Msi.ps1 throws on any failure ($ErrorActionPreference = Stop), so no exit-code check is needed.
    $validatorArguments = @{ MsiPath = $msiPath; SourceExePath = $exePath; RequireSignature = $true
                             ExpectedSignerThumbprint = $certificate.Thumbprint; ExpectedSignerSubject = $certificate.Subject }
    if ($isLocalTest) { $validatorArguments.AllowUntrustedTestSigner = $true }
    & (Join-Path $PSScriptRoot 'Validate-Msi.ps1') @validatorArguments

    if (Test-Path -LiteralPath $ReleaseDirectory) { Remove-Item -LiteralPath $ReleaseDirectory -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ReleaseDirectory | Out-Null
    Copy-Item $exePath, $msiPath -Destination $ReleaseDirectory -Force
    $releasedExe = Join-Path $ReleaseDirectory (Split-Path $exePath -Leaf)
    $releasedMsi = Join-Path $ReleaseDirectory (Split-Path $msiPath -Leaf)
    $hashes = Get-FileHash -LiteralPath $releasedExe, $releasedMsi -Algorithm SHA256 |
        Select-Object Hash, @{ Name = 'Name'; Expression = { Split-Path $_.Path -Leaf } }
    ($hashes | ForEach-Object { "$($_.Hash) *$($_.Name)" }) | Set-Content (Join-Path $ReleaseDirectory 'SHA256SUMS.txt') -Encoding ascii
    $releaseType = if ($isLocalTest) { 'test-signed-not-for-distribution' } else { 'signed-release' }
    [ordered]@{
        version          = (Get-Content (Join-Path $repoRoot 'src-tauri\tauri.conf.json') -Raw | ConvertFrom-Json).version
        releaseType      = $releaseType
        generatedUtc     = [DateTime]::UtcNow.ToString('o')
        signerThumbprint = $certificate.Thumbprint
        signerSubject    = $certificate.Subject
        timestampUrl     = $TimestampUrl
        artifacts        = @($hashes | ForEach-Object { [ordered]@{ file = $_.Name; sha256 = $_.Hash } })
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ReleaseDirectory 'release-manifest.json') -Encoding utf8

    if ($isLocalTest) {
        @(
            'TEST-SIGNED ARTIFACTS - NOT TRUSTED BY WINDOWS - NOT FOR DISTRIBUTION',
            '',
            'These files were signed with a temporary self-signed certificate that has already been destroyed:',
            "  Subject:    $($certificate.Subject)",
            "  Thumbprint: $($certificate.Thumbprint)",
            '',
            'Purpose: prove that the signing pipeline (signtool + RFC3161 timestamp + hash binding + validation) works end-to-end.',
            'To produce a distributable release, rerun Build-SignedRelease.ps1 with -PfxPath or -CertificateThumbprint',
            'using the organisation''s publicly trusted code-signing certificate, or run the signed-release GitHub Actions workflow.'
        ) | Set-Content (Join-Path $ReleaseDirectory 'TEST-SIGNED-NOT-FOR-DISTRIBUTION.txt') -Encoding ascii
        Write-Warning "Test-signed pipeline self-test output created (NOT FOR DISTRIBUTION): $ReleaseDirectory"
    }
    else {
        Write-Host "Formal signed release created: $ReleaseDirectory"
    }
}
finally {
    if ($temporaryCertificate) { Remove-Item -LiteralPath $temporaryCertificate.PSPath -Force -ErrorAction SilentlyContinue }
}
