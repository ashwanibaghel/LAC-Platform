param(
    [Parameter(Mandatory=$true)][string]$ServerExe,
    [Parameter(Mandatory=$true)][string]$ModelPath,
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [string]$LoraPath,
    [string]$ModelVersion = 'local-configured',
    [string]$ModelManifestPath
)
$ErrorActionPreference = 'Stop'
$taskExe = (Resolve-Path -LiteralPath $ServerExe).Path
$taskModel = (Resolve-Path -LiteralPath $ModelPath).Path
$taskLora = if ($LoraPath) { (Resolve-Path -LiteralPath $LoraPath -ErrorAction Stop).Path } else { $null }
$taskModelHash = (Get-FileHash -LiteralPath $taskModel -Algorithm SHA256).Hash.ToLowerInvariant()
$taskServerHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
$taskLoraHash = if ($taskLora) { (Get-FileHash -LiteralPath $taskLora -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
if ($ModelManifestPath) {
    $taskManifest = Get-Content -LiteralPath (Resolve-Path -LiteralPath $ModelManifestPath).Path -Raw | ConvertFrom-Json
    if ($taskManifest.baseGgufSha256 -ne $taskModelHash -or $taskManifest.loraGgufSha256 -ne $taskLoraHash -or $taskManifest.serverSha256 -ne $taskServerHash -or $taskManifest.contextSize -ne 4096) {
        throw 'Model files/context do not match the deployment manifest. Model was not started.'
    }
    $ModelVersion = $taskManifest.modelVersion
}
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
if ($taskLora) { $taskArgs += " --lora `"$taskLora`"" }
$taskServerVersion = (& $taskExe --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify local server version. Model was not started.' }
$taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WorkingDirectory $taskRuntime -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRuntime 'model.stdout.log') -RedirectStandardError (Join-Path $taskRuntime 'model.stderr.log')
@{ pid=$taskProcess.Id; startedAt=$taskProcess.StartTime.ToUniversalTime().ToString('O'); executable=$taskExe; role='court-local-model'; modelVersion=$ModelVersion; baseModel=@{path=$taskModel;sha256=$taskModelHash}; loraAdapter=if ($taskLora) { @{path=$taskLora;sha256=$taskLoraHash} } else { $null }; serverVersion=$taskServerVersion; serverSha256=$taskServerHash; host='127.0.0.1'; port=8096; contextSize=4096; parallel=1; gpuLayers=0 } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRuntime 'court-local-model.pid.json')
Write-Output "Local Court model PID $($taskProcess.Id), loopback port 8096. No documents sent externally."
