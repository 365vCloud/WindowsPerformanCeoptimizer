[CmdletBinding()]
param(
    [Parameter(ParameterSetName = 'Pfx', Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$PfxPath,
    [Parameter(ParameterSetName = 'Store', Mandatory)][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'Pfx')][SecureString]$PfxPassword,
    [Parameter(ParameterSetName = 'Pfx')][string]$PfxPasswordEnvironmentVariable,
    [Parameter(Mandatory)][ValidatePattern('^https?://')][string]$TimestampUrl,
    [string]$SigntoolPath,
    [string]$ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) {
    $ReleaseDirectory = Join-Path $PSScriptRoot "bin\SignedRelease"
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exePath = Join-Path $repoRoot 'src-tauri\target\release\wpo-app.exe'
$msiPath = Join-Path $PSScriptRoot 'bin\Release\WPO.Installer.msi'
$temporaryCertificate = $null

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
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint.Replace(' ', '') -ne $Thumbprint.Replace(' ', '') -or
        $signature.SignerCertificate.Subject -ne $Subject -or -not $signature.TimeStamperCertificate) {
        throw "Post-signing validation failed for '$Path'. Status=$($signature.Status)."
    }
}

try {
    if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
        $password = Get-RequiredPfxPassword
        $temporaryCertificate = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $password -Exportable:$false
        if (-not $temporaryCertificate.HasPrivateKey) { throw 'The supplied PFX has no private key.' }
        $CertificateThumbprint = $temporaryCertificate.Thumbprint
    }
    $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object { $_.Thumbprint.Replace(' ', '') -eq $CertificateThumbprint.Replace(' ', '') -and $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $certificate) { throw "No signing certificate with private key was found for thumbprint '$CertificateThumbprint'." }

    $signtool = Find-Signtool
    & dotnet build (Join-Path $PSScriptRoot 'WPO.Installer.wixproj') -c Release -p:BuildTauriBeforeMsi=true
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exePath)) { throw 'Clean Tauri release build failed.' }

    & $signtool sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Signing the Tauri executable failed.' }
    Assert-Signature $exePath $certificate.Thumbprint $certificate.Subject
    $exeHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash

    & dotnet build (Join-Path $PSScriptRoot 'WPO.Installer.wixproj') -c Release -p:BuildTauriBeforeMsi=false -p:ExpectedTauriExeSha256=$exeHash
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msiPath)) { throw 'WiX MSI build failed.' }
    & $signtool sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $msiPath
    if ($LASTEXITCODE -ne 0) { throw 'Signing the MSI failed.' }

    & (Join-Path $PSScriptRoot 'Validate-Msi.ps1') -MsiPath $msiPath -SourceExePath $exePath -RequireSignature -ExpectedSignerThumbprint $certificate.Thumbprint -ExpectedSignerSubject $certificate.Subject
    if ($LASTEXITCODE -ne 0) { throw 'Final formal release validation failed.' }
    New-Item -ItemType Directory -Force -Path $ReleaseDirectory | Out-Null
    Copy-Item $exePath, $msiPath -Destination $ReleaseDirectory -Force
    $releasedExe = Join-Path $ReleaseDirectory (Split-Path $exePath -Leaf)
    $releasedMsi = Join-Path $ReleaseDirectory (Split-Path $msiPath -Leaf)
    $hashes = Get-FileHash -LiteralPath $releasedExe, $releasedMsi -Algorithm SHA256
    ($hashes | ForEach-Object { "$($_.Hash) *$($_.Name)" }) | Set-Content (Join-Path $ReleaseDirectory 'SHA256SUMS.txt') -Encoding ascii
    [ordered]@{ version = (Get-Content (Join-Path $repoRoot 'src-tauri\tauri.conf.json') -Raw | ConvertFrom-Json).version; generatedUtc = [DateTime]::UtcNow.ToString('o'); signerThumbprint = $certificate.Thumbprint; artifacts = @($hashes | ForEach-Object { [ordered]@{ file = $_.Name; sha256 = $_.Hash } }) } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ReleaseDirectory 'release-manifest.json') -Encoding utf8
    Write-Host "Formal signed release created: $ReleaseDirectory"
}
finally {
    if ($temporaryCertificate) { Remove-Item -LiteralPath $temporaryCertificate.PSPath -Force -ErrorAction SilentlyContinue }
}
