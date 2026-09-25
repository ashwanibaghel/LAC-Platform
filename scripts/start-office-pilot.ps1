[CmdletBinding()]
param(
    [string]$SettingsFile = (Join-Path $PSScriptRoot 'office-pilot-settings.ps1')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (Test-Path -LiteralPath $SettingsFile) { . $SettingsFile }

$required = @(
    'ConnectionStrings__DefaultConnection', 'Storage__DocumentRoot', 'Storage__ExtractionRoot', 'Storage__BackupRoot',
    'OnlyOffice__BrowserUrl', 'OnlyOffice__AppExternalUrl', 'OnlyOffice__DocumentServerUrl', 'OnlyOffice__JwtSecret'
)
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'Process')) })
if ($missing.Count -gt 0) {
    throw "Missing required pilot settings: $($missing -join ', '). Set protected machine environment variables or create office-pilot-settings.ps1 from the template."
}
if ($env:OnlyOffice__Enabled -ne 'true') { throw 'OnlyOffice__Enabled must be true for the office pilot.' }
foreach ($path in @($env:Storage__DocumentRoot, $env:Storage__ExtractionRoot, $env:Storage__BackupRoot)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$app = Join-Path $PSScriptRoot 'LAC.Api.exe'
if (-not (Test-Path -LiteralPath $app)) { throw "LAC.Api.exe was not found in $PSScriptRoot." }
& $app --urls 'http://0.0.0.0:5088'
