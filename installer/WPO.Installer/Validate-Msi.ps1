[CmdletBinding()]
param(
    [string]$MsiPath,
    [string]$SourceExePath,
    [string]$SourceExeManifestPath,
    [switch]$RequireSignature,
    [string]$ExpectedSignerThumbprint,
    [string]$ExpectedSignerSubject,
    # Accepts an untrusted-chain (self-signed) signature ONLY for the local pipeline self-test certificate.
    [switch]$AllowUntrustedTestSigner
)

$ErrorActionPreference = "Stop"
$testSignerSubject = 'CN=WPO LOCAL TEST SIGNING - NOT FOR DISTRIBUTION'

if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $PSScriptRoot "bin\Release\WPO.Installer.msi"
}

if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
    throw "MSI was not found: $MsiPath. Build the Release installer first."
}

if ([string]::IsNullOrWhiteSpace($SourceExePath)) {
    $SourceExePath = Join-Path $PSScriptRoot "..\..\src-tauri\target\release\wpo-app.exe"
}
if ([string]::IsNullOrWhiteSpace($SourceExeManifestPath)) {
    $SourceExeManifestPath = Join-Path $PSScriptRoot "..\..\src-tauri\target\release\wpo-app.build-manifest.json"
}

function Normalize-Thumbprint([string]$Value) {
    return ($Value -replace '\s', '').ToUpperInvariant()
}

function Assert-AuthenticodeSignature {
    param([Parameter(Mandatory)][string]$Path, [string]$ExpectedThumbprint, [string]$ExpectedSubject)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $untrustedTestChain = $AllowUntrustedTestSigner -and
        $signature.Status -eq [System.Management.Automation.SignatureStatus]::UnknownError -and
        $null -ne $signature.SignerCertificate -and $signature.SignerCertificate.Subject -eq $testSignerSubject
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -and -not $untrustedTestChain) {
        throw "Authenticode signature is not valid for '$Path': $($signature.Status) $($signature.StatusMessage)"
    }
    if ($signature.SignerCertificate -and $signature.SignerCertificate.Subject -eq $testSignerSubject -and -not $AllowUntrustedTestSigner) {
        throw "'$Path' is signed with the local test certificate, which is not acceptable for a formal release."
    }
    if ($null -eq $signature.SignerCertificate) {
        throw "Authenticode validation returned no signer certificate for '$Path'."
    }
    if ($ExpectedThumbprint -and (Normalize-Thumbprint $signature.SignerCertificate.Thumbprint) -ne (Normalize-Thumbprint $ExpectedThumbprint)) {
        throw "Signer thumbprint mismatch for '$Path'. Expected '$ExpectedThumbprint', got '$($signature.SignerCertificate.Thumbprint)'."
    }
    if ($ExpectedSubject -and $signature.SignerCertificate.Subject -ne $ExpectedSubject) {
        throw "Signer subject mismatch for '$Path'. Expected '$ExpectedSubject', got '$($signature.SignerCertificate.Subject)'."
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "Authenticode signature for '$Path' has no RFC3161 timestamp certificate."
    }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$resolvedMsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$database = $installer.OpenDatabase($resolvedMsiPath, 0)

function Get-MsiColumnValues {
    param(
        [Parameter(Mandatory)][string]$Table,
        [Parameter(Mandatory)][string]$Column
    )

    $view = $database.OpenView("SELECT ``$Column`` FROM ``$Table``")
    [void]$view.Execute()
    $values = @()
    while ($record = $view.Fetch()) {
        $values += $record.StringData(1)
    }
    [void]$view.Close()
    return $values
}

$files = Get-MsiColumnValues -Table "File" -Column "File"
if ($files.Count -lt 1) {
    throw "The MSI File table must contain the application executable."
}

$fileNames = foreach ($file in (Get-MsiColumnValues -Table "File" -Column "FileName")) {
    $file.Split("|")[-1]
}

if ($fileNames -notcontains "WindowsPerformanceOptimizer.exe") {
    throw "The MSI File table does not contain WindowsPerformanceOptimizer.exe."
}

$upgradeRows = Get-MsiColumnValues -Table "Upgrade" -Column "UpgradeCode"
if ($upgradeRows.Count -eq 0) {
    throw "The MSI does not contain MajorUpgrade metadata."
}

$tableNames = Get-MsiColumnValues -Table "_Tables" -Column "Name"

if ($tableNames -contains "CustomAction") {
    throw "The MSI must not contain custom actions."
}

if ($tableNames -contains "Registry") {
    throw "The MSI must not write registry values."
}

$directoryNames = foreach ($directory in (Get-MsiColumnValues -Table "Directory" -Column "DefaultDir")) {
    $directory.Split("|")[-1]
}

if ($directoryNames -notcontains "WindowsPerformanceOptimizer") {
    throw "The MSI does not target the WindowsPerformanceOptimizer application directory."
}

if ($RequireSignature) {
    if ([string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint) -and [string]::IsNullOrWhiteSpace($ExpectedSignerSubject)) {
        throw "RequireSignature requires ExpectedSignerThumbprint and/or ExpectedSignerSubject."
    }
    if (-not (Test-Path -LiteralPath $SourceExePath -PathType Leaf)) {
        throw "Source Tauri executable was not found: $SourceExePath."
    }
    if (-not (Test-Path -LiteralPath $SourceExeManifestPath -PathType Leaf)) {
        throw "Source Tauri executable manifest was not found: $SourceExeManifestPath."
    }
    $sourceManifest = Get-Content -LiteralPath $SourceExeManifestPath -Raw | ConvertFrom-Json
    $actualSourceHash = (Get-FileHash -LiteralPath $SourceExePath -Algorithm SHA256).Hash
    if ($sourceManifest.schemaVersion -ne 1 -or $sourceManifest.file -ne 'wpo-app.exe' -or
        $sourceManifest.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        $sourceManifest.sha256 -ne $actualSourceHash) {
        throw "Source Tauri executable manifest does not bind to '$SourceExePath'."
    }
    Assert-AuthenticodeSignature -Path $resolvedMsiPath -ExpectedThumbprint $ExpectedSignerThumbprint -ExpectedSubject $ExpectedSignerSubject
    Assert-AuthenticodeSignature -Path (Resolve-Path -LiteralPath $SourceExePath).Path -ExpectedThumbprint $ExpectedSignerThumbprint -ExpectedSubject $ExpectedSignerSubject
    if ($AllowUntrustedTestSigner) {
        Write-Warning "TEST-SIGNED ARTIFACT - UNTRUSTED SELF-SIGNED CERTIFICATE - NOT FOR DISTRIBUTION"
        Write-Host "MSI test-signed pipeline validation passed: $MsiPath"
    }
    else {
        Write-Host "MSI formal release validation passed: $MsiPath"
    }
}
else {
    Write-Warning "UNSIGNED DEVELOPMENT ARTIFACT - NOT FOR DISTRIBUTION"
    Write-Host "MSI development static validation passed: $MsiPath"
}
