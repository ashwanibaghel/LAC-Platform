#requires -Version 5.1
[CmdletBinding()]
param([switch]$LibraryOnly)
. (Join-Path $PSScriptRoot 'office-court-runtime-common.ps1')
if ($LibraryOnly) { return }
Assert-OfficeAdministrator
$state='D:\LAC-CourtAI-Runtime'
$receipt=Read-OfficeInstallation $state 'C:\LAC-Platform' 'C:\LAC-Publish'
$manifest=Assert-OfficeDeployment 'D:\LAC-CourtAI-V3-Office'
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in @(8096,8097) })
Assert-OfficeListeners $listeners
Assert-OfficeProcessContract $listeners @(Get-CimInstance Win32_Process -ErrorAction Stop) $receipt $manifest
$health=Get-OfficeHealth 8097;Assert-OfficeQuestionHealth $health $receipt
$arguments=Get-OfficeRecoveryArguments $receipt
$start=[Diagnostics.ProcessStartInfo]::new($receipt.pythonExecutable)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$start.WorkingDirectory=Join-Path $state 'package'
$start.Arguments=(@($arguments | ForEach-Object { ConvertTo-OfficeNativeArgument $_ }) -join ' ')
$start.EnvironmentVariables['PYTHONNOUSERSITE']='1'
$start.EnvironmentVariables['PYTHONPATH']=''
$process=[Diagnostics.Process]::Start($start)
try {
    # Existing controller owns durable startup/PIDs/logs and the OS lock; no duplicate supervisor.
    if (-not $process.WaitForExit(210000)) {
        Write-Output '{"runtimeState":"Starting","reasonCode":"RecoveryStillRunning","progressFile":"D:\\LAC-CourtAI-Runtime\\services\\recovery.json"}'
        exit 2
    }
} finally { $process.Dispose() }
& (Join-Path $PSScriptRoot 'verify-office-court-ai.ps1')
