param(
    [Parameter(Mandatory=$true)][ValidateSet('Worker','Questions')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$PythonExe,
    [Parameter(Mandatory=$true)][string]$ExtractionRoot,
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$ModelVersion,
    [string]$Jobs
)
$ErrorActionPreference='Stop'
$taskPython=(Resolve-Path -LiteralPath $PythonExe).Path
$taskRoot=(Resolve-Path -LiteralPath $ExtractionRoot).Path
if (![IO.Path]::IsPathRooted($ExtractionRoot)) { throw 'Use the existing absolute Storage:ExtractionRoot.' }
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskRuntime=[IO.Path]::GetFullPath($RuntimeDirectory)
New-Item -ItemType Directory -Path $taskRuntime -Force | Out-Null
$taskRole = if ($Mode -eq 'Worker') { 'court-intelligence-worker' } else { 'court-intelligence-questions' }
$taskScript = if ($Mode -eq 'Worker') { 'worker.py' } else { 'serve_questions.py' }
if ($Mode -eq 'Questions' -and (Get-NetTCPConnection -LocalPort 8097 -State Listen -ErrorAction SilentlyContinue)) { throw 'Port 8097 already occupied.' }
$taskArgs="`"$(Join-Path $taskRepository "tools/court-order-intelligence/$taskScript")`" --extraction-root `"$taskRoot`" --model-version `"$ModelVersion`""
$taskStopFile=$null
if ($Mode -eq 'Worker') {
    if (!$Jobs) { throw 'Worker requires explicit local job file; no automatic source discovery.' }
    $taskJobs=(Resolve-Path -LiteralPath $Jobs).Path
    $taskArgs += " --jobs `"$taskJobs`""
    $taskStopFile=Join-Path $taskRuntime ("court-worker-stop-" + [guid]::NewGuid().ToString('N'))
    $taskArgs += " --stop-file `"$taskStopFile`""
}
$taskProcess=Start-Process -FilePath $taskPython -ArgumentList $taskArgs -WorkingDirectory $taskRepository -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRuntime "$taskRole.stdout.log") -RedirectStandardError (Join-Path $taskRuntime "$taskRole.stderr.log")
@{pid=$taskProcess.Id;startedAt=$taskProcess.StartTime.ToUniversalTime().ToString('O');executable=$taskPython;role=$taskRole;script=$taskScript;repository=$taskRepository;stopFile=$taskStopFile} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRuntime "$taskRole.pid.json")
Write-Output "$taskRole PID $($taskProcess.Id). No migration, seed or canonical mutation."
