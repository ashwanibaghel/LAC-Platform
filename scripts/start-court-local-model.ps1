param(
    [Parameter(Mandatory=$true)][string]$ServerExe,
    [Parameter(Mandatory=$true)][string]$ModelPath,
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory
)
$ErrorActionPreference = 'Stop'
$taskExe = (Resolve-Path -LiteralPath $ServerExe).Path
$taskModel = (Resolve-Path -LiteralPath $ModelPath).Path
$taskRuntime = [IO.Path]::GetFullPath($RuntimeDirectory)
New-Item -ItemType Directory -Path $taskRuntime -Force | Out-Null
$taskRecord=Join-Path $taskRuntime 'court-local-model.pid.json'
if (Test-Path -LiteralPath $taskRecord) {
    $taskPrevious=Get-Content -LiteralPath $taskRecord -Raw | ConvertFrom-Json
    $taskExisting=Get-Process -Id $taskPrevious.pid -ErrorAction SilentlyContinue
    if ($taskExisting -and $taskExisting.Path -eq $taskPrevious.executable -and $taskExisting.StartTime.ToUniversalTime().Ticks -eq ([datetime]$taskPrevious.startedAt).ToUniversalTime().Ticks) { throw 'Local model already running. Its PID record was not overwritten.' }
}
if (Get-NetTCPConnection -LocalPort 8096 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 8096 already occupied. Existing process was not changed.' }
$taskArgs = "--model `"$taskModel`" --host 127.0.0.1 --port 8096 --ctx-size 4096 --threads 2 --parallel 1 --n-gpu-layers 0 --prio -1 --poll 0"
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WorkingDirectory $taskRuntime -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRuntime 'model.stdout.log') -RedirectStandardError (Join-Path $taskRuntime 'model.stderr.log')
@{ pid=$taskProcess.Id; startedAt=$taskProcess.StartTime.ToUniversalTime().ToString('O'); executable=$taskExe; role='court-local-model' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRuntime 'court-local-model.pid.json')
Write-Output "Local Court model PID $($taskProcess.Id), loopback port 8096. No documents sent externally."
