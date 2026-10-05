#requires -Version 5.1
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibraryOnly=$false
. (Join-Path $repo 'scripts\office-court-runtime-common.ps1')
if ($LibraryOnly) { throw 'Library import changed the entry-point execution flag.' }
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('office-court-tests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$script:passed=0
function Assert-True($Condition,[string]$Name) { if (-not $Condition) { throw "FAIL: $Name" };$script:passed++;Write-Output "PASS: $Name" }
function Assert-Rejected([scriptblock]$Action,[string]$Message,[string]$Name) {
    $rejected=$false
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -notlike "*$Message*") { throw };$rejected=$true }
    Assert-True $rejected $Name
}
$before=@(Get-Process -Name llama-server -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$pin=Read-CourtJson (Join-Path $repo 'scripts\office-court-question-package.json')
$source=Join-Path $repo 'tools\court-order-intelligence'
Assert-OfficeQuestionPackage $source $pin
Assert-True $true 'All accepted Python/recovery bytes pinned'
$package=Join-Path $testRoot 'package';[IO.Directory]::CreateDirectory($package) | Out-Null
foreach ($file in $pin.files) { [IO.File]::Copy((Join-Path $source $file.name),(Join-Path $package $file.name)) }
Assert-OfficeQuestionPackage $package $pin
[IO.File]::AppendAllText((Join-Path $package 'questions.py'),'# changed')
Assert-Rejected { Assert-OfficeQuestionPackage $package $pin } 'Wrong SHA256' 'Changed question package refused'
[IO.File]::Copy((Join-Path $source 'questions.py'),(Join-Path $package 'questions.py'),$true)
[IO.File]::WriteAllText((Join-Path $package 'extra.py'),'# extra')
Assert-Rejected { Assert-OfficeQuestionPackage $package $pin } 'QuestionPackageHashMismatch' 'Extra Python package member refused'
Remove-Item -LiteralPath (Join-Path $package 'extra.py')
[IO.File]::Move((Join-Path $package 'runtime_recovery.py'),(Join-Path $package 'held'))
Assert-Rejected { Assert-OfficeQuestionPackage $package $pin } 'QuestionPackageHashMismatch' 'Missing recovery controller refused'
$badPin=($pin | ConvertTo-Json -Depth 10) | ConvertFrom-Json;$badPin.packageSha256='0'*64
Assert-Rejected { Assert-OfficeQuestionPackage $source $badPin } 'QuestionPackageHashMismatch' 'Wrong package identity refused'

$publish=Join-Path $testRoot 'publish';$root=Join-Path $testRoot 'canonical';$state=Join-Path $testRoot 'state'
foreach ($path in @($publish,$root,$state)) { [IO.Directory]::CreateDirectory($path) | Out-Null }
$webPath=Join-Path $publish 'web.config'
[IO.File]::WriteAllText($webPath,'<configuration><system.webServer><aspNetCore processPath="dotnet" arguments=".\LAC.Api.dll"><environmentVariables><environmentVariable name="Jwt__Key" value="secret-sentinel"/><environmentVariable name="Unrelated__Setting" value="preserve"/></environmentVariables></aspNetCore></system.webServer></configuration>')
[IO.File]::WriteAllText((Join-Path $publish 'appsettings.json'),('{"Storage":{"ExtractionRoot":'+($root | ConvertTo-Json -Compress)+'}}'))
$xml=Read-OfficeXml $webPath
$effective=Get-OfficeConfiguration $publish @{} $xml
Assert-True ($effective['Storage__ExtractionRoot'] -ceq $root) 'Existing canonical JSON root resolved'
$hostEnv=@{Storage__ExtractionRoot=$root;CourtRuntime__RecoveryEnabled='false'}
$overrideRoot=Join-Path $testRoot 'web-canonical';[IO.Directory]::CreateDirectory($overrideRoot) | Out-Null
Set-OfficeWebEnvironment $xml @{Storage__ExtractionRoot=$overrideRoot}
$effective=Get-OfficeConfiguration $publish $hostEnv $xml
Assert-True ($effective['Storage__ExtractionRoot'] -ceq $overrideRoot) 'IIS environment takes precedence over host/JSON'
Assert-Rejected { Get-OfficeConfiguration $publish @{'Storage:ExtractionRoot'=$root} $xml } 'Colon environment aliases' 'Ambiguous environment alias refused'
Assert-Rejected { Assert-OfficeNoCloud @{CourtRuntime__Endpoint='https://external.example'} } 'Unrecognized' 'External endpoint configuration refused'
Assert-Rejected { Assert-OfficeNoCloud @{CourtRuntime__CloudFallback='true'} } 'Unrecognized' 'Cloud fallback configuration refused'
Assert-Rejected { Assert-OfficeNoCloud @{CourtRuntime__Threads='2'} } 'Unrecognized' 'Silent model settings override refused'
Assert-Rejected { Assert-OfficeRuntimePaths $publish $repo (Join-Path $publish 'runtime') $root } 'overlaps' 'Runtime logs cannot be under live publish root'
Assert-Rejected { Assert-OfficeRuntimePaths $publish $repo $root $root } 'overlaps' 'Runtime state cannot overlap extraction root'
$xml=Read-OfficeXml $webPath;$originalSha=Get-CourtSha256 $webPath
$values=Get-OfficeRuntimeValues 'C:\Python311\python.exe' $state $root $pin.packageSha256
Set-OfficeWebEnvironment $xml $values
Assert-OfficeNoCloud $values
Write-OfficeConfigurationAtomic $webPath $xml $originalSha
$updated=Read-OfficeXml $webPath;$env=Get-OfficeWebEnvironment $updated
Assert-True ($env['Jwt__Key'] -ceq 'secret-sentinel' -and $env['Unrelated__Setting'] -ceq 'preserve') 'Unrelated settings/secrets preserved without printing them'
Assert-True ($env['Storage__ExtractionRoot'] -ceq $root -and $env['CourtRuntime__ManifestPath'] -ceq 'D:\LAC-CourtAI-V3-Office\court-model-manifest.json') 'Office paths configured without relocating canonical artifacts'
$count=$updated.SelectNodes('//environmentVariable').Count
Set-OfficeWebEnvironment $updated $values
Assert-True ($updated.SelectNodes('//environmentVariable').Count -eq $count) 'Repeated configuration produces no duplicate keys'
Assert-Rejected { Write-OfficeConfigurationAtomic $webPath $updated $originalSha } 'Concurrent' 'Concurrent configuration changes refused'
Assert-True ((Get-CourtSha256 $webPath) -cne $originalSha) 'Concurrent refusal leaves accepted config intact'
Set-OfficeWebEnvironment $updated @{Storage__ExtractionRoot=(Join-Path $testRoot 'missing-root')}
Assert-Rejected { Get-OfficeConfiguration $publish @{} $updated } 'CanonicalRootUnavailable' 'Missing canonical root is not recreated'

# Resolve the same environment precedence as WebApplicationBuilder, not the operator's environment.
$environmentXml=Read-OfficeXml $webPath
Set-OfficeWebEnvironment $environmentXml @{ASPNETCORE_ENVIRONMENT='Development'}
$environmentRootNode=$environmentXml.SelectSingleNode("//environmentVariable[@name='Storage__ExtractionRoot']")
[void]$environmentRootNode.ParentNode.RemoveChild($environmentRootNode)
[IO.File]::WriteAllText((Join-Path $publish 'appsettings.Production.json'),('{"Storage":{"ExtractionRoot":'+($root | ConvertTo-Json -Compress)+'}}'))
[IO.File]::WriteAllText((Join-Path $publish 'appsettings.Development.json'),('{"Storage":{"ExtractionRoot":'+($overrideRoot | ConvertTo-Json -Compress)+'}}'))
Assert-True ((Get-OfficeConfiguration $publish @{DOTNET_ENVIRONMENT='Production'} $environmentXml)['Storage__ExtractionRoot'] -ceq $root) 'DOTNET environment precedence preserves the effective production root'

# Receipt/configuration checks with tiny fixture files; no office paths are written and no process is run.
$installedPackage=Join-Path $state 'package';[IO.Directory]::CreateDirectory($installedPackage) | Out-Null
foreach ($file in $pin.files) { [IO.File]::Copy((Join-Path $source $file.name),(Join-Path $installedPackage $file.name)) }
$fakePython=Join-Path $testRoot 'python.exe';[IO.File]::WriteAllText($fakePython,'non-executable fixture')
$installedReceipt=[ordered]@{formatVersion=1;stateDirectory=$state;repository=$repo;publishRoot=$publish;appPool='DefaultAppPool'
    extractionRoot=$root;extractionRootFingerprint=Get-OfficeRootFingerprint $root;manifestSha256=$script:OfficePortableSha;packageSha256=$pin.packageSha256
    pythonExecutable=$fakePython;pythonSha256=Get-CourtSha256 $fakePython}
Write-CourtJson (Join-Path $state 'OFFICE-RUNTIME-INSTALL.json') $installedReceipt
$installedXml=Read-OfficeXml $webPath
Set-OfficeWebEnvironment $installedXml (Get-OfficeRuntimeValues $fakePython $state $root $pin.packageSha256)
Write-OfficeConfigurationAtomic $webPath $installedXml (Get-CourtSha256 $webPath)
$savedHostResolver=(Get-Command Get-OfficeHostEnvironment).ScriptBlock
function Get-OfficeHostEnvironment([string]$AppPool) { return @{} }
try {
    $first=Read-OfficeInstallation $state $repo $publish
    $second=Read-OfficeInstallation $state $repo $publish
    Assert-True ($first.packageSha256 -ceq $second.packageSha256) 'Repeated installed receipt validation is idempotent'
    $changed=Read-OfficeXml $webPath;Set-OfficeWebEnvironment $changed @{Storage__ExtractionRoot=$overrideRoot}
    Write-OfficeConfigurationAtomic $webPath $changed (Get-CourtSha256 $webPath)
    Assert-Rejected { Read-OfficeInstallation $state $repo $publish } 'ApplicationRuntimeConfigurationMismatch' 'Accepted installation rejects extraction-root drift'
    Write-OfficeConfigurationAtomic $webPath $installedXml (Get-CourtSha256 $webPath)
    [IO.File]::AppendAllText($fakePython,'changed')
    Assert-Rejected { Read-OfficeInstallation $state $repo $publish } 'Wrong SHA256' 'Python interpreter drift requires deliberate reinstall'
} finally { Set-Item -Path function:Get-OfficeHostEnvironment -Value $savedHostResolver }

$receipt=[pscustomobject]@{stateDirectory=$state;extractionRoot=$root;packageSha256=$pin.packageSha256;pythonExecutable='C:\Python311\python.exe'}
$health=[pscustomobject]@{httpStatus=200;data=[pscustomobject]@{questionServiceState='Ready';modelVersion=(Get-CourtTransferPolicy).ModelVersion;questionPackageSha256=$pin.packageSha256;extractionRootFingerprint=Get-OfficeRootFingerprint $root}}
Assert-OfficeQuestionHealth $health $receipt
Assert-True $true 'Question health proves model/package/canonical-root identity'
$health.data.extractionRootFingerprint='0'*64
Assert-Rejected { Assert-OfficeQuestionHealth $health $receipt } 'QuestionRuntimeMismatch' 'Wrong live extraction root rejected'
$health.data.extractionRootFingerprint=Get-OfficeRootFingerprint $root
$health.data.questionPackageSha256='0'*64
Assert-Rejected { Assert-OfficeQuestionHealth $health $receipt } 'QuestionRuntimeMismatch' 'Wrong live question package rejected'
$health.data.questionPackageSha256=$pin.packageSha256
$memory=[pscustomobject]@{reasonCode='ModelInsufficientMemory'}
$summary=Get-OfficeServiceSummary $health $null $memory
Assert-True ($summary.questionServiceState -ceq 'Ready' -and $summary.modelState -ceq 'ModelOffline' -and $summary.reasonCode -ceq 'ModelInsufficientMemory') 'Specific memory failure wins over generic offline while Q&A stays ready'
$readyModel=[pscustomobject]@{httpStatus=200;data=[pscustomobject]@{status='ok'}}
Assert-True ((Get-OfficeServiceSummary $health $readyModel $memory).reasonCode -ceq 'VerifiedServicesReady') 'Stale memory failure cannot override healthy model'
Assert-True ((Get-OfficeServiceSummary $null $null $null).questionServiceState -ceq 'QuestionServiceOffline') 'Question offline remains explicit'
Assert-True ((Get-OfficeServiceSummary $health ([pscustomobject]@{httpStatus=503;data=@{}}) $null).modelState -ceq 'Starting') 'Model-loading health remains Starting'
$recordPath=Join-Path $testRoot 'recovery.json'
Write-CourtJson $recordPath @{runtimeState='Starting';reasonCode='PackageVerification';recoveryPid=2147483647;startedAt=[DateTime]::UtcNow.ToString('o')}
Assert-True ((Get-OfficeVerifiedRecovery $recordPath $receipt).reasonCode -ceq 'RecoveryInterrupted') 'Stale recovery PID does not report endless Starting'
Remove-Item -LiteralPath $recordPath
Write-CourtJson $recordPath @{runtimeState='Failed';reasonCode='ModelInsufficientMemory';modelVersion=(Get-CourtTransferPolicy).ModelVersion
    manifestSha256=$script:OfficePortableSha;questionPackageSha256=$pin.packageSha256;extractionRootFingerprint=Get-OfficeRootFingerprint $root}
Assert-True ((Get-OfficeVerifiedRecovery $recordPath $receipt).reasonCode -ceq 'ModelInsufficientMemory') 'Durable verified memory failure remains observable'
Remove-Item -LiteralPath $recordPath
Write-CourtJson $recordPath @{runtimeState='Failed';reasonCode='ModelInsufficientMemory';modelVersion='wrong'
    manifestSha256=$script:OfficePortableSha;questionPackageSha256=$pin.packageSha256;extractionRootFingerprint=Get-OfficeRootFingerprint $root}
Assert-Rejected { Get-OfficeVerifiedRecovery $recordPath $receipt } 'RecoveryRecordMismatch' 'Foreign recovery record cannot supply memory reason'
Assert-OfficeListeners @([pscustomobject]@{LocalAddress='127.0.0.1'},[pscustomobject]@{LocalAddress='::1'})
Assert-True $true 'Only literal loopback listener addresses accepted'
foreach ($address in @('0.0.0.0','::','192.168.1.10')) {
    Assert-Rejected { Assert-OfficeListeners @([pscustomobject]@{LocalAddress=$address}) } 'NonLoopbackCourtListener' ('Public/LAN binding refused: '+$address)
}
$manifest=[pscustomobject]@{serverPath='D:\LAC-CourtAI-V3-Office\bin\llama-server.exe';baseGgufPath='D:\LAC-CourtAI-V3-Office\base.gguf';loraGgufPath='D:\LAC-CourtAI-V3-Office\lora.gguf'}
$modelArgs=@('--model',$manifest.baseGgufPath,'--lora',$manifest.loraGgufPath,'--host','127.0.0.1','--port','8096','--ctx-size','3072','--threads','6','--parallel','1','--n-gpu-layers','0','--prio','-1','--poll','0','--cache-type-k','f16','--cache-type-v','f16','--flash-attn','auto','--ubatch-size','128','--cache-ram','0')
$process=[pscustomobject]@{ProcessId=123;ExecutablePath=$manifest.serverPath;CommandLine=(@($manifest.serverPath)+$modelArgs | ForEach-Object { ConvertTo-OfficeNativeArgument $_ }) -join ' '}
$listeners=@([pscustomobject]@{LocalPort=8096;OwningProcess=123;LocalAddress='127.0.0.1'})
Assert-OfficeProcessContract $listeners @($process) $receipt $manifest
Assert-True $true 'Accepted CPU process command verified without starting it'
$process.CommandLine=$process.CommandLine.Replace('"3072"','"4096"')
Assert-Rejected { Assert-OfficeProcessContract $listeners @($process) $receipt $manifest } 'ExistingCourtStartupContractMismatch' 'Obsolete 4096 profile refused'
$process.ExecutablePath='C:\wrong\llama-server.exe'
Assert-Rejected { Assert-OfficeProcessContract $listeners @($process) $receipt $manifest } 'ExistingModelRuntimeMismatch' 'Foreign service owner refused'
$arguments=@(Get-OfficeRecoveryArguments $receipt)
Assert-True ($arguments[0] -ceq (Join-Path $state 'package\runtime_recovery.py') -and $arguments -notcontains '--ctx-size' -and $arguments -notcontains '--threads') 'Startup delegates all inference settings exclusively to accepted controller'
Assert-Rejected { ConvertTo-OfficeNativeArgument 'bad"argument' } 'Unsafe process argument' 'Native process quoting rejects unsafe input'

$caseId=[Guid]'665ec7ce-c31b-4320-9c74-45b69e14327a'
$cache=[pscustomobject]@{caseId=$caseId.ToString();progressSummary=[pscustomobject]@{usableBriefs=8;officialSources=8};runtime=[pscustomobject]@{modelState='ModelOffline'}}
Assert-True ((Assert-OfficeCachedIntelligenceResponse $caseId $cache).usableBriefs -eq 8) 'API-verified cached briefs remain readable with offline model'
Assert-Rejected { Assert-OfficeCachedIntelligenceResponse ([Guid]::NewGuid()) $cache } 'NoVerifiedCachedBriefsForCase' 'Wrong-case cache response fails closed'
$cache.progressSummary.usableBriefs=0
Assert-Rejected { Assert-OfficeCachedIntelligenceResponse $caseId $cache } 'NoVerifiedCachedBriefsForCase' 'Zero usable evidence cannot pass cached intelligence verification'

$portableSource=Join-Path $PSScriptRoot 'fixtures\office-court'
$badDeployment=Join-Path $testRoot 'bad-deployment';[IO.Directory]::CreateDirectory($badDeployment) | Out-Null
foreach ($name in @('SOURCE-DEPLOYMENT-MANIFEST.json','court-model-manifest.json')) { [IO.File]::Copy((Join-Path $portableSource $name),(Join-Path $badDeployment $name)) }
$portable=Read-CourtJson (Join-Path $badDeployment 'court-model-manifest.json')
Assert-Rejected { Assert-OfficeDeployment $badDeployment } 'Missing file' 'Missing model refused before application configuration'
[IO.File]::WriteAllBytes((Join-Path $badDeployment $portable.baseGgufFile),[byte[]]@(1))
Assert-Rejected { Assert-OfficeDeployment $badDeployment } 'Wrong file size' 'Wrong model refused before application configuration'
[IO.File]::AppendAllText((Join-Path $badDeployment 'court-model-manifest.json'),' ')
Assert-Rejected { Assert-OfficeDeployment $badDeployment } 'Wrong SHA256' 'Wrong portable manifest refused'

foreach ($name in @('install-office-court-ai.ps1','start-office-court-services.ps1','verify-office-court-ai.ps1','office-court-runtime-common.ps1')) {
    $path=Join-Path $repo ('scripts\'+$name);$tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
    Assert-True ($errors.Count -eq 0) ('PowerShell parses: '+$name)
    $text=[IO.File]::ReadAllText($path)
    Assert-True ($text -notmatch 'pip install|Invoke-Expression|Start-Web|Stop-Web|iisreset|v1/chat/completions|quantize|--jobs|/refresh') ('No download/deployment/inference operations: '+$name)
}
$verify=[IO.File]::ReadAllText((Join-Path $repo 'scripts\verify-office-court-ai.ps1'))
Assert-True ($verify -notmatch 'Process\]::Start|Start-Process') 'Verification never launches a process'
$after=@(Get-Process -Name llama-server -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
Assert-True (($before -join ',') -ceq ($after -join ',')) 'No model process launched in focused tests'
Write-Output "PASS: $script:passed focused assertions; fixtures retained at $testRoot"
