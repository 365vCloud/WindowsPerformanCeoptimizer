[CmdletBinding()]
param(
    [string]$MsiPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $MsiPath = Join-Path $PSScriptRoot "bin\Release\WPO.Installer.msi"
}

if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
    throw "MSI was not found: $MsiPath. Build the Release installer first."
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

Write-Host "MSI static validation passed: $MsiPath"


