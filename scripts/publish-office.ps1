[CmdletBinding()]
param(
    [string]$PublishDirectory = 'C:\LAC-Publish'
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

Write-Host 'Building React production bundle...'
Push-Location $webProject
try { npm run build } finally { Pop-Location }

Write-Host 'Refreshing API static-file staging area...'
if (Test-Path -LiteralPath $wwwroot) { Remove-Item -LiteralPath $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $webProject 'dist') -Force | Copy-Item -Destination $wwwroot -Recurse -Force

Write-Host "Publishing API to $resolvedPublish..."
dotnet publish (Join-Path $apiProject 'LAC.Api.csproj') -c Release -o $resolvedPublish

Write-Host ''
Write-Host 'Publish complete. In IIS, point the site at the publish directory and recycle its app pool.'
Write-Host 'Verify locally: http://localhost/api/health'
Write-Host 'Verify worker readiness: http://localhost/api/health/document-intelligence'
Write-Host 'Then verify from an approved LAN client using the IIS site URL.'
