#requires -Version 5.1
[CmdletBinding()] param([Parameter(Mandatory=$true)][string]$DataDirectory,[switch]$FullRegression)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dataRoot=[IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
$repoFull=[IO.Path]::GetFullPath($repoRoot).TrimEnd('\')
if($dataRoot -eq [IO.Path]::GetPathRoot($dataRoot) -or $dataRoot -ieq $repoFull -or $dataRoot.StartsWith($repoFull+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $dataRoot)){throw 'Use a new disposable directory outside the repository. Existing paths are never overwritten.'}
if(@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object {$_.LocalPort -eq 55440}).Count){throw 'Port 55440 is occupied. Existing runtimes are never stopped.'}
$init=Get-Command initdb.exe -ErrorAction SilentlyContinue
if(-not $init){$candidates=@(Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles 'PostgreSQL') -Directory | Where-Object {$_.Name -match '^\d+$'} | Sort-Object {[int]$_.Name} -Descending);foreach($candidate in $candidates){$path=Join-Path $candidate.FullName 'bin\initdb.exe';if(Test-Path -LiteralPath $path){$init=Get-Item -LiteralPath $path;break}}}
if(-not $init){throw 'Install/locate PostgreSQL tooling first; no software is installed by this helper.'}
$initPath=if($init.PSObject.Properties['Source']){$init.Source}else{$init.FullName}
$control=Join-Path (Split-Path -Parent $initPath) 'pg_ctl.exe'
& $initPath -D $dataRoot -U postgres -A trust --encoding=UTF8 --no-locale
if($LASTEXITCODE -ne 0){throw 'Dedicated test cluster initialization failed.'}
Add-Content -LiteralPath (Join-Path $dataRoot 'postgresql.conf') -Value "`nlisten_addresses = '127.0.0.1'`nport = 55440`n"
$started=$false
try {
    & $control start -D $dataRoot -l (Join-Path $dataRoot 'server.log') -w
    if($LASTEXITCODE -ne 0){throw 'Dedicated test cluster did not start.'};$started=$true
    Push-Location $repoRoot
    try { $arguments=@('test','tests/LAC.Tests');if(-not $FullRegression){$arguments+=@('--filter','FullyQualifiedName~CompensationHistoryTests')}; & dotnet @arguments; if($LASTEXITCODE -ne 0){throw 'Tests failed; inspect the output and disposable cluster log.'} }
    finally {Pop-Location}
} finally {if($started){& $control stop -D $dataRoot -m fast -w}}
# Test directory/logs remain available for diagnosis; no recursive deletion is performed.
