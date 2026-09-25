[CmdletBinding()]
param(
    [string]$PublishDirectory = 'C:\LAC-Publish-New',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repoRoot 'src\LAC.Web'
$apiProject = Join-Path $repoRoot 'src\LAC.Api'
$wwwroot = Join-Path $apiProject 'wwwroot'
$resolvedPublish = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\', '/')
$liveDirectory = [IO.Path]::GetFullPath('C:\LAC-Publish').TrimEnd('\', '/')
$dataDirectory = [IO.Path]::GetFullPath('D:\Software Data').TrimEnd('\', '/')
$resolvedWwwroot = [IO.Path]::GetFullPath($wwwroot)
$resolvedRepoRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/')
if ($resolvedPublish.Equals($liveDirectory, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedPublish.StartsWith($liveDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $liveDirectory.StartsWith($resolvedPublish + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to publish directly over or around live C:\LAC-Publish.'
}
if ($resolvedPublish.Equals($dataDirectory, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedPublish.StartsWith($dataDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must not be inside D:\Software Data.'
}
if (-not $resolvedWwwroot.StartsWith($resolvedRepoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Static-file staging path is outside the repository.'
}
if (Test-Path -LiteralPath $resolvedPublish) {
    if (Get-ChildItem -LiteralPath $resolvedPublish -Force | Select-Object -First 1) {
        throw "Staging directory is not empty: $resolvedPublish. Use an empty directory."
    }
} else {
    New-Item -ItemType Directory -Path $resolvedPublish -Force | Out-Null
}

Write-Host 'Building React production bundle with Vite...'
Push-Location $webProject
try {
    & npx vite build
    if ($LASTEXITCODE -ne 0) { throw 'Vite production build failed.' }
} finally { Pop-Location }

Write-Host 'Refreshing API static-file staging area...'
if (Test-Path -LiteralPath $wwwroot) { Remove-Item -LiteralPath $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $webProject 'dist') -Force | Copy-Item -Destination $wwwroot -Recurse -Force

Write-Host "Publishing API to $resolvedPublish..."
$publishArgs = @('publish', (Join-Path $apiProject 'LAC.Api.csproj'), '-c', 'Release', '-o', $resolvedPublish, '-r', 'win-x64')
if ($FrameworkDependent) { $publishArgs += @('--self-contained', 'false') }
else { $publishArgs += @('--self-contained', 'true') }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed; staging directory is incomplete.' }

Copy-Item (Join-Path $repoRoot 'docs\office-lan-pilot.md') (Join-Path $resolvedPublish 'OFFICE-DEPLOYMENT-README.md') -Force

Write-Host ''
Write-Host "IIS package staged at $resolvedPublish."
Write-Host 'Back up the office database, current deployment, and document storage before the IIS swap.'
Write-Host 'Configure secrets in protected office-host settings, then verify IIS and ONLYOFFICE after deployment.'
