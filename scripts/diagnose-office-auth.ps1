[CmdletBinding()]
param(
    [string]$PackageDirectory = 'C:\LAC-Publish',
    [string]$Username,
    [string]$HealthUrl,
    [switch]$VerifyPassword,
    [switch]$OfficePreflight
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$package = [IO.Path]::GetFullPath($PackageDirectory)
$app = Join-Path $package 'LAC.Api.exe'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
    throw "LAC.Api.exe was not found in $package"
}

$webConfig = Join-Path $package 'web.config'
$webConfigVariables = @()
if (Test-Path -LiteralPath $webConfig -PathType Leaf) {
    [xml]$xml = Get-Content -LiteralPath $webConfig -Raw
    $aspNetCore = $xml.SelectSingleNode('/configuration/location/system.webServer/aspNetCore')
    if ($null -eq $aspNetCore) { $aspNetCore = $xml.SelectSingleNode('/configuration/system.webServer/aspNetCore') }
    $processPath = if ($null -eq $aspNetCore) { '(missing)' } else { $aspNetCore.Attributes['processPath'].Value }
    Write-Output "IIS web.config processPath: $processPath"
    $webConfigVariables = @($xml.SelectNodes('//aspNetCore/environmentVariables/environmentVariable'))
    $variableNames = @($webConfigVariables | ForEach-Object { $_.Attributes['name'].Value })
    Write-Output "IIS web.config environment variable names: $($variableNames -join ', ')"
} else {
    Write-Output 'IIS web.config: absent'
}

$arguments = @('auth-doctor')
if ($Username) { $arguments += @('--username', $Username) }
if ($HealthUrl) { $arguments += @('--health-url', $HealthUrl) }
if ($VerifyPassword) { $arguments += '--verify-password' }
if ($OfficePreflight) { $arguments += '--office-preflight' }

Push-Location $package
$originalVariables = @{}
try {
    foreach ($variable in $webConfigVariables) {
        $name = $variable.Attributes['name'].Value
        $originalVariables[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $variable.Attributes['value'].Value, 'Process')
    }
    & $app @arguments
    exit $LASTEXITCODE
} finally {
    foreach ($name in $originalVariables.Keys) {
        [Environment]::SetEnvironmentVariable($name, $originalVariables[$name], 'Process')
    }
    Pop-Location
}
