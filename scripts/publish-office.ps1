[CmdletBinding()]
param(
    [string]$PublishDirectory = 'C:\LAC-Publish',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repoRoot 'src\LAC.Web'
$apiProject = Join-Path $repoRoot 'src\LAC.Api'
$wwwroot = Join-Path $apiProject 'wwwroot'
$resolvedPublish = [IO.Path]::GetFullPath($PublishDirectory)
if ($resolvedPublish.StartsWith('D:\Software Data', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must not be inside D:\Software Data.'
}

Write-Host 'Building React production bundle with Vite...'
Push-Location $webProject
try { npx vite build } finally { Pop-Location }

Write-Host 'Refreshing API static-file staging area...'
if (Test-Path -LiteralPath $wwwroot) { Remove-Item -LiteralPath $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $webProject 'dist') -Force | Copy-Item -Destination $wwwroot -Recurse -Force

Write-Host "Publishing API to $resolvedPublish..."
if (Test-Path -LiteralPath $resolvedPublish) { Remove-Item -LiteralPath $resolvedPublish -Recurse -Force }
$publishArgs = @('publish', (Join-Path $apiProject 'LAC.Api.csproj'), '-c', 'Release', '-o', $resolvedPublish)
if (-not $FrameworkDependent) { $publishArgs += @('-r', 'win-x64', '--self-contained', 'true') }
& dotnet @publishArgs

Write-Host 'Adding LAN pilot launch material (no secrets)...'
Copy-Item (Join-Path $repoRoot 'scripts\start-office-pilot.ps1') (Join-Path $resolvedPublish 'start-office-pilot.ps1') -Force
Copy-Item (Join-Path $repoRoot 'scripts\office-pilot-firewall.ps1') (Join-Path $resolvedPublish 'office-pilot-firewall.ps1') -Force
Copy-Item (Join-Path $repoRoot 'docs\office-pilot-settings.template.ps1') (Join-Path $resolvedPublish 'office-pilot-settings.template.ps1') -Force
Copy-Item (Join-Path $repoRoot 'docs\office-lan-pilot.md') (Join-Path $resolvedPublish 'OFFICE-PILOT-README.md') -Force

Write-Host ''
Write-Host 'Publish complete. This folder contains a self-contained win-x64 Kestrel pilot by default.'
Write-Host 'Copy only this folder to the office host. Configure secrets as protected machine environment variables.'
Write-Host 'Run .\start-office-pilot.ps1 and verify http://localhost:5088/api/health.'
