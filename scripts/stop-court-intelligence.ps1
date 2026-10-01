param(
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][ValidateSet('court-local-model','court-intelligence-worker','court-intelligence-questions')][string]$Role
)
$ErrorActionPreference='Stop'
$taskRecord=Join-Path ([IO.Path]::GetFullPath($RuntimeDirectory)) "$Role.pid.json"
$taskState=Get-Content -LiteralPath $taskRecord -Raw | ConvertFrom-Json
$taskProcess=Get-Process -Id $taskState.pid -ErrorAction SilentlyContinue
if (!$taskProcess) { Write-Output "$Role already stopped."; return }
if ($taskProcess.Path -ne $taskState.executable -or $taskProcess.StartTime.ToUniversalTime().Ticks -ne ([datetime]$taskState.startedAt).ToUniversalTime().Ticks) { throw 'PID identity changed; refusing to stop an unrelated process.' }
if ($Role -eq 'court-intelligence-worker') {
    $taskRuntime=[IO.Path]::GetFullPath($RuntimeDirectory).TrimEnd('\')+'\'
    if (!$taskState.stopFile -or ![IO.Path]::GetFullPath($taskState.stopFile).StartsWith($taskRuntime,[StringComparison]::OrdinalIgnoreCase)) { throw 'Missing or unsafe cooperative-stop marker; refusing force-stop.' }
    Set-Content -LiteralPath $taskState.stopFile -Value 'stop'
    Write-Output 'Worker stop requested. It will clean temporary files after the current bounded operation; no process was force-killed.'
    return
}
Stop-Process -Id $taskProcess.Id
Write-Output "$Role stopped. No API/frontend/database/storage process was stopped."
