#requires -Version 5.1
# Shared verification/configuration functions; dot-sourcing never starts services.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$officePreviousLibraryFlag=Get-Variable -Name LibraryOnly -ValueOnly -ErrorAction SilentlyContinue
. (Join-Path $PSScriptRoot 'restore-court-v3-office-transfer.ps1') -LibraryOnly
$LibraryOnly=$officePreviousLibraryFlag
Add-Type -AssemblyName System.Net.Http
$script:OfficePortableSha='36fc8c63794e566df7fecfd2588130c9c295d145b49d7015055171f154e2fb4f'
$script:OfficeTransferSha='fe212ae33eaa44ded1c3898c51409f1e4f75592d42db283b554c1250d431551d'

function Get-OfficeRootFingerprint([string]$Root) {
    $hash=[Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes((Get-CourtAbsolutePath $Root).ToLowerInvariant())))).Replace('-','').ToLowerInvariant() }
    finally { $hash.Dispose() }
}
function Assert-OfficeDeployment([string]$Root) {
    $root=Get-CourtAbsolutePath $Root
    $policy=Get-CourtTransferPolicy
    Assert-CourtFile (Join-Path $root 'SOURCE-DEPLOYMENT-MANIFEST.json') $policy.SourceManifestSha256
    Assert-CourtFile (Join-Path $root 'court-model-manifest.json') $script:OfficePortableSha
    $source=Read-CourtJson (Join-Path $root 'SOURCE-DEPLOYMENT-MANIFEST.json')
    $portable=Read-CourtJson (Join-Path $root 'court-model-manifest.json')
    Assert-CourtSourceManifest $source
    Assert-CourtPortableManifest $source $portable $policy.RuntimeControllerSha256
    $allowed=@{'SOURCE-DEPLOYMENT-MANIFEST.json'=$true;'court-model-manifest.json'=$true;'INSTALL-RECEIPT.json'=$true}
    foreach ($file in @(Get-CourtBinaryInventory $source)) {
        $allowed[$file.path.Replace('/','\')]=$true
        Assert-CourtFile (Join-Path $root $file.path) $file.sha256 $file.bytes
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -Force)) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse deployment member refused.' }
        if (-not $file.PSIsContainer -and -not $allowed.ContainsKey($file.FullName.Substring($root.Length+1))) { throw 'Unexpected deployment file.' }
    }
    return $portable
}
function Assert-OfficeQuestionPackage([string]$Folder, $Pin) {
    if ($Pin.formatVersion -ne 1 -or $Pin.acceptedBackend -cne '2676b70c3ef2689d600b2dcd3189d4ebbd1c7549' -or
        $Pin.packageSha256 -cne '8de2ff561e36b1dec0f323ab69200226b38a1a435505402ae2876b7c294151a8') { throw 'QuestionPackageHashMismatch' }
    $folder=Get-CourtAbsolutePath $Folder
    $actual=@(Get-ChildItem -LiteralPath $folder -Filter '*.py' -File | Sort-Object Name)
    if ($actual.Count -ne @($Pin.files).Count) { throw 'QuestionPackageHashMismatch' }
    $names=@{};$digestBytes=[IO.MemoryStream]::new()
    foreach ($file in $Pin.files) {
        if ($file.name -cnotmatch '^[a-z0-9_]+\.py$') { throw 'Unsafe package inventory.' }
        if ($names.ContainsKey($file.name)) { throw 'QuestionPackageHashMismatch' };$names[$file.name]=$true
        Assert-CourtFile (Join-Path $folder $file.name) $file.sha256
    }
    try {
        $orderedNames=[string[]]@($Pin.files | ForEach-Object { $_.name })
        [Array]::Sort($orderedNames,[StringComparer]::Ordinal)
        foreach ($name in $orderedNames) {
            $file=@($Pin.files | Where-Object { $_.name -ceq $name })[0]
            $nameBytes=[Text.Encoding]::UTF8.GetBytes($file.name);$digestBytes.Write($nameBytes,0,$nameBytes.Length)
            $bytes=New-Object byte[] 32
            for ($i=0;$i -lt 32;$i++) { $bytes[$i]=[Convert]::ToByte($file.sha256.Substring($i*2,2),16) }
            $digestBytes.Write($bytes,0,$bytes.Length)
        }
        $sha=[Security.Cryptography.SHA256]::Create()
        try { $computed=([BitConverter]::ToString($sha.ComputeHash($digestBytes.ToArray()))).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($computed -cne $Pin.packageSha256) { throw 'QuestionPackageHashMismatch' }
    } finally { $digestBytes.Dispose() }
    Assert-CourtFile (Join-Path $folder 'runtime_recovery.py') (Get-CourtTransferPolicy).RuntimeControllerSha256
}
function Read-OfficeXml([string]$Path) {
    [void](Get-CourtAbsolutePath $Path)
    if (-not [IO.File]::Exists($Path) -or (Get-Item -LiteralPath $Path).Length -gt 10MB) { throw 'Missing/oversized IIS configuration.' }
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try { $xml=[Xml.XmlDocument]::new();$xml.PreserveWhitespace=$true;$xml.XmlResolver=$null;$xml.Load($reader);return ,$xml }
    finally { $reader.Dispose() }
}
function Get-OfficeWebEnvironment($Xml) {
    $values=@{}
    foreach ($node in $Xml.SelectNodes('//system.webServer/aspNetCore/environmentVariables/environmentVariable')) {
        if ($values.ContainsKey($node.GetAttribute('name'))) { throw 'Duplicate IIS environment key.' }
        $values[$node.GetAttribute('name')]=$node.GetAttribute('value')
    }
    return $values
}
function Get-OfficeHostEnvironment([string]$AppPool) {
    if ($AppPool -notmatch '^[A-Za-z0-9_. -]+$') { throw 'Invalid app pool name.' }
    $values=@{}
    foreach ($key in [Environment]::GetEnvironmentVariables('Machine').Keys) { $values[$key]=[Environment]::GetEnvironmentVariable($key,'Machine') }
    $xml=Read-OfficeXml (Join-Path ([Environment]::SystemDirectory) 'inetsrv\config\applicationHost.config')
    $site=$xml.SelectSingleNode("/configuration/system.applicationHost/sites/site[@name='Default Web Site']")
    $application=if ($site) { $site.SelectSingleNode("application[@path='/']") } else { $null }
    $virtual=if ($application) { $application.SelectSingleNode("virtualDirectory[@path='/']") } else { $null }
    $sitePool=if ($application) { $application.GetAttribute('applicationPool') } else { '' }
    if (-not $sitePool) {
        $defaults=$xml.SelectSingleNode('/configuration/system.applicationHost/sites/applicationDefaults')
        $sitePool=if ($defaults -and $defaults.GetAttribute('applicationPool')) { $defaults.GetAttribute('applicationPool') } else { 'DefaultAppPool' }
    }
    if ($null -eq $virtual -or $sitePool -ine $AppPool -or
        (Get-CourtAbsolutePath ([Environment]::ExpandEnvironmentVariables($virtual.GetAttribute('physicalPath')))) -ine 'C:\LAC-Publish') { throw 'Existing office IIS application path/pool mismatch.' }
    $pool=$xml.SelectSingleNode("/configuration/system.applicationHost/applicationPools/add[@name='$AppPool']")
    if ($null -eq $pool) { throw 'Configured IIS app pool not found.' }
    $identity=$pool.SelectSingleNode('processModel')
    if ($null -eq $identity) { $identity=$xml.SelectSingleNode('/configuration/system.applicationHost/applicationPools/applicationPoolDefaults/processModel') }
    if ($identity -and $identity.GetAttribute('identityType') -and $identity.GetAttribute('identityType') -ne 'ApplicationPoolIdentity') { throw 'ApplicationPoolIdentity is required for automatic least-privilege ACLs.' }
    foreach ($node in $xml.SelectNodes('/configuration/system.applicationHost/applicationPools/applicationPoolDefaults/environmentVariables/add')) { $values[$node.GetAttribute('name')]=$node.GetAttribute('value') }
    foreach ($node in $pool.SelectNodes('environmentVariables/add')) { $values[$node.GetAttribute('name')]=$node.GetAttribute('value') }
    foreach ($node in $xml.SelectNodes('/configuration/system.webServer/aspNetCore/environmentVariables/environmentVariable')) { $values[$node.GetAttribute('name')]=$node.GetAttribute('value') }
    foreach ($node in $xml.SelectNodes("/configuration/location[@path='Default Web Site']/system.webServer/aspNetCore/environmentVariables/environmentVariable")) { $values[$node.GetAttribute('name')]=$node.GetAttribute('value') }
    return $values
}
function Get-OfficeConfiguration([string]$PublishRoot, $HostEnvironment, $WebXml) {
    # ASP.NET JSON -> host/app-pool environment -> aspNetCore environment. Never use the operator's user environment.
    $values=@{}
    $web=Get-OfficeWebEnvironment $WebXml
    $environment='Production'
    $effectiveEnvironment=@{}
    foreach ($source in @($HostEnvironment,$web)) {
        foreach ($key in @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT')) { if ($source.ContainsKey($key)) { $effectiveEnvironment[$key]=$source[$key] } }
    }
    # WebApplicationBuilder gives DOTNET_ENVIRONMENT precedence, after per-key IIS overrides.
    if ($effectiveEnvironment['DOTNET_ENVIRONMENT']) { $environment=$effectiveEnvironment['DOTNET_ENVIRONMENT'] }
    elseif ($effectiveEnvironment['ASPNETCORE_ENVIRONMENT']) { $environment=$effectiveEnvironment['ASPNETCORE_ENVIRONMENT'] }
    if ($environment -notmatch '^[A-Za-z0-9_-]+$') { throw 'Unsafe ASP.NET environment name.' }
    foreach ($name in @('appsettings.json',('appsettings.'+$environment+'.json'))) {
        $path=Join-Path $PublishRoot $name
        if ([IO.File]::Exists($path)) {
            $json=Read-CourtJson $path
            foreach ($section in @('Storage','CourtRuntime')) {
                $property=$json.PSObject.Properties[$section]
                if ($property) { foreach ($entry in $property.Value.PSObject.Properties) { $values[$section+'__'+$entry.Name]=[string]$entry.Value } }
            }
        }
    }
    foreach ($source in @($HostEnvironment,$web)) {
        foreach ($key in $source.Keys) {
            if ($key -match '^(Storage|CourtRuntime):') { throw 'Colon environment aliases refused; use canonical double-underscore keys.' }
            if ($key -match '^(Storage|CourtRuntime)__') { $values[$key]=$source[$key] }
        }
    }
    $asp=$WebXml.SelectSingleNode('//system.webServer/aspNetCore')
    if (@($WebXml.SelectNodes('//system.webServer/aspNetCore')).Count -ne 1 -or $asp.GetAttribute('configSource') -or
        $asp.GetAttribute('arguments') -match '--|Storage|CourtRuntime|Environment') { throw 'Unsupported IIS hosting/command-line configuration.' }
    $root=$values['Storage__ExtractionRoot']
    if ([string]::IsNullOrWhiteSpace($root)) { $root=Join-Path $PublishRoot 'App_Data\extraction' }
    $root=Get-CourtAbsolutePath $root
    if (-not [IO.Directory]::Exists($root)) { throw 'CanonicalRootUnavailable: existing extraction root required; no new root will be invented.' }
    $values['Storage__ExtractionRoot']=$root
    return $values
}
function Assert-OfficeNoCloud($Values) {
    $allowed=@('RecoveryEnabled','PythonExecutable','RecoveryScript','RuntimeDirectory','RecoveryScriptSha256','ManifestPath','ManifestSha256','PackageSha256','ModelVersion')
    foreach ($key in $Values.Keys) {
        if ($key -match '^CourtRuntime__(.+)$' -and $allowed -notcontains $Matches[1]) { throw 'Unrecognized Court runtime configuration; endpoint/cloud/settings overrides are refused.' }
    }
}
function Get-OfficeRuntimeValues([string]$Python, [string]$State, [string]$ExtractionRoot, [string]$PackageSha) {
    @{
        CourtRuntime__RecoveryEnabled='true';CourtRuntime__PythonExecutable=$Python
        CourtRuntime__RecoveryScript=(Join-Path $State 'package\runtime_recovery.py');CourtRuntime__RuntimeDirectory=(Join-Path $State 'services')
        CourtRuntime__RecoveryScriptSha256=(Get-CourtTransferPolicy).RuntimeControllerSha256
        CourtRuntime__ManifestPath='D:\LAC-CourtAI-V3-Office\court-model-manifest.json';CourtRuntime__ManifestSha256=$script:OfficePortableSha
        CourtRuntime__PackageSha256=$PackageSha;CourtRuntime__ModelVersion=(Get-CourtTransferPolicy).ModelVersion
        Storage__ExtractionRoot=$ExtractionRoot
    }
}
function Set-OfficeWebEnvironment($Xml, $Values) {
    $asp=$Xml.SelectSingleNode('//system.webServer/aspNetCore')
    $env=$asp.SelectSingleNode('environmentVariables')
    if ($null -eq $env) { $env=$Xml.CreateElement('environmentVariables');[void]$asp.AppendChild($env) }
    foreach ($key in ($Values.Keys | Sort-Object)) {
        $node=$env.SelectSingleNode("environmentVariable[@name='$key']")
        if ($null -eq $node) { $node=$Xml.CreateElement('environmentVariable');$node.SetAttribute('name',$key);[void]$env.AppendChild($node) }
        $node.SetAttribute('value',$Values[$key])
    }
}
function Assert-OfficeRuntimePaths([string]$Publish, [string]$Repository, [string]$State, [string]$Extraction) {
    foreach ($path in @($Publish,$Repository,$State,$Extraction)) { [void](Get-CourtAbsolutePath $path) }
    Assert-CourtSeparateOutput $State @($Publish,$Repository,'D:\LAC-CourtAI-V3-Office',$Extraction)
}
function Set-OfficePrivateAcl([string]$Path, [string]$AppPool, [switch]$RuntimeAccess, [switch]$ReadAccess) {
    [void](Get-CourtAbsolutePath $Path)
    $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    }
    if ($RuntimeAccess) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('IIS AppPool\'+$AppPool,'Modify','ContainerInherit,ObjectInherit','None','Allow')) }
    elseif ($ReadAccess) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('IIS AppPool\'+$AppPool,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')) }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function Assert-OfficeAdministrator {
    if (-not [Environment]::Is64BitProcess) { throw 'Use x64 PowerShell on the Office PC.' }
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run in an elevated PowerShell on the Office PC.' }
}
function Get-OfficeHealth([int]$Port) {
    if ($Port -notin @(8096,8097)) { throw 'Only fixed loopback health endpoints are allowed.' }
    $handler=[Net.Http.HttpClientHandler]::new();$handler.UseProxy=$false;$handler.AllowAutoRedirect=$false;$handler.UseCookies=$false
    $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(4)
    try {
        $response=$client.GetAsync('http://127.0.0.1:'+ $Port+'/health',[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            $stream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();$buffer=New-Object byte[] 8193;$count=0
            # Per-read timeout prevents an oversized/stalled health body from hanging a manual check.
            while ($count -lt $buffer.Length) {
                $read=$stream.ReadAsync($buffer,$count,$buffer.Length-$count)
                if (-not $read.Wait(4000)) { throw 'Health body timeout.' }
                $n=$read.Result;if ($n -eq 0) { break };$count+=$n
            }
            if ($count -gt 8192) { throw 'Oversized health response.' }
            $data=[Text.Encoding]::UTF8.GetString($buffer,0,$count) | ConvertFrom-Json
            [pscustomobject]@{httpStatus=[int]$response.StatusCode;data=$data}
        } finally { $response.Dispose() }
    } catch { return $null } finally { $client.Dispose();$handler.Dispose() }
}
function Assert-OfficeListeners($Listeners) {
    foreach ($listener in $Listeners) { if ($listener.LocalAddress -notin @('127.0.0.1','::1')) { throw 'NonLoopbackCourtListener' } }
}
function Get-OfficeServiceSummary($Question, $Model, $Recovery) {
    $q=if ($Question -and $Question.httpStatus -eq 200) { 'Ready' } else { 'QuestionServiceOffline' }
    $m=if ($Model -and $Model.httpStatus -eq 200 -and $Model.data.status -eq 'ok') { 'Ready' } elseif ($Model -and $Model.httpStatus -eq 503) { 'Starting' } else { 'ModelOffline' }
    $reason=if ($q -ne 'Ready') { 'QuestionServiceOffline' } elseif ($m -eq 'Ready') { 'VerifiedServicesReady' } elseif ($m -eq 'Starting') { 'ModelStarting' } else { 'ModelOffline' }
    if ($m -eq 'ModelOffline' -and $Recovery -and $Recovery.reasonCode -eq 'ModelInsufficientMemory') { $reason='ModelInsufficientMemory' }
    [pscustomobject]@{questionServiceState=$q;modelState=$m;reasonCode=$reason}
}
function Assert-OfficeProcessContract($Listeners, $Processes, $Receipt, $Manifest) {
    $package=Join-Path $Receipt.stateDirectory 'package'
    foreach ($port in @(8096,8097)) {
        $owners=@($Listeners | Where-Object { $_.LocalPort -eq $port } | Select-Object -ExpandProperty OwningProcess -Unique)
        if ($owners.Count -eq 0) { continue }
        if ($owners.Count -ne 1) { throw 'CourtListenerOwnerMismatch' }
        $process=@($Processes | Where-Object { $_.ProcessId -eq $owners[0] })
        if ($process.Count -ne 1) { throw 'CourtListenerOwnerMismatch' }
        # Fixed Windows command-line tokens, never evaluate commands or print their contents.
        $tokens=@([regex]::Matches($process[0].CommandLine,'"[^"]*"|[^\s"]+') | ForEach-Object { $_.Value.Trim('"') })
        if ($port -eq 8096) {
            if ($process[0].ExecutablePath -ine $Manifest.serverPath) { throw 'ExistingModelRuntimeMismatch' }
            $expected=@('--model',$Manifest.baseGgufPath,'--lora',$Manifest.loraGgufPath,'--host','127.0.0.1','--port','8096',
                '--ctx-size','3072','--threads','6','--parallel','1','--n-gpu-layers','0','--prio','-1','--poll','0',
                '--cache-type-k','f16','--cache-type-v','f16','--flash-attn','auto','--ubatch-size','128','--cache-ram','0')
        } else {
            if ($process[0].ExecutablePath -ine $Receipt.pythonExecutable) { throw 'ExistingQuestionRuntimeMismatch' }
            $expected=@((Join-Path $package 'serve_questions.py'),'--extraction-root',$Receipt.extractionRoot,'--model-version',(Get-CourtTransferPolicy).ModelVersion,
                '--require-model-health','--refresh-request-timeout-seconds','600','--refresh-case-timeout-seconds','1800')
        }
        if ($tokens.Count -ne ($expected.Count+1)) { throw 'ExistingCourtStartupContractMismatch' }
        for ($i=0;$i -lt $expected.Count;$i++) { if ($tokens[$i+1] -cne $expected[$i]) { throw 'ExistingCourtStartupContractMismatch' } }
    }
}
function Get-OfficeVerifiedRecovery([string]$Path, $Receipt) {
    if (-not [IO.File]::Exists($Path)) { return $null }
    $record=Read-CourtJson $Path
    # Starting verification records do not yet contain the identity fields.
    if ($record.runtimeState -eq 'Starting') {
        $alive=$false
        try {
            $owner=Get-Process -Id $record.recoveryPid -ErrorAction Stop
            $alive=$owner.Path -ieq $Receipt.pythonExecutable -and [Math]::Abs(($owner.StartTime.ToUniversalTime()-([datetime]$record.startedAt).ToUniversalTime()).TotalSeconds) -lt 10
        } catch { $alive=$false }
        if (-not $alive) { $record.runtimeState='Failed';$record.reasonCode='RecoveryInterrupted';return $record }
        if ($record.reasonCode -eq 'PackageVerification') { return $record }
    }
    # A pre-verification failure intentionally lacks identities; preserve its bounded reason without claiming a verified model.
    if ($record.runtimeState -eq 'Failed' -and $record.reasonCode -in @('CanonicalRootUnavailable','ManifestHashMismatch','QuestionPackageHashMismatch','PinnedRuntimePolicyMismatch','ModelPackageHashMismatch','RuntimePackageHashMismatch','RecoveryFailed')) { return $record }
    if ($record.modelVersion -cne (Get-CourtTransferPolicy).ModelVersion -or $record.manifestSha256 -cne $script:OfficePortableSha -or
        $record.questionPackageSha256 -cne $Receipt.packageSha256 -or
        $record.extractionRootFingerprint -cne (Get-OfficeRootFingerprint $Receipt.extractionRoot)) { throw 'RecoveryRecordMismatch' }
    return $record
}
function Write-OfficeConfigurationAtomic([string]$Path, $Xml, [string]$BeforeSha) {
    $temp=Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.court-web-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    try {
        $Xml.Save($temp)
        if ((Get-CourtSha256 $Path) -cne $BeforeSha) { throw 'Concurrent web.config change refused.' }
        [IO.File]::Replace($temp,$Path,[Management.Automation.Language.NullString]::Value)
    } finally { if ([IO.File]::Exists($temp)) { Remove-Item -LiteralPath $temp } }
}
function Get-OfficeRecoveryArguments($Receipt) {
    # No direct llama invocation, no obsolete manifest contextSize/threads startup profile.
    @((Join-Path $Receipt.stateDirectory 'package\runtime_recovery.py'),'--manifest','D:\LAC-CourtAI-V3-Office\court-model-manifest.json',
        '--manifest-sha256',$script:OfficePortableSha,'--package-sha256',$Receipt.packageSha256,
        '--extraction-root',$Receipt.extractionRoot,'--runtime-directory',(Join-Path $Receipt.stateDirectory 'services'))
}
function ConvertTo-OfficeNativeArgument([string]$Value) {
    # Windows argv quoting; no shell interpretation and no trailing slash ambiguity.
    if ($Value.Contains('"') -or $Value.EndsWith('\') -or $Value.Contains("`n") -or $Value.Contains("`r")) { throw 'Unsafe process argument.' }
    return '"'+$Value+'"'
}
function Read-OfficeInstallation([string]$State, [string]$Repository, [string]$Publish) {
    $receipt=Read-CourtJson (Join-Path $State 'OFFICE-RUNTIME-INSTALL.json')
    if ($receipt.formatVersion -ne 1 -or $receipt.stateDirectory -cne $State -or $receipt.repository -cne $Repository -or
        $receipt.publishRoot -cne $Publish -or $receipt.manifestSha256 -cne $script:OfficePortableSha) { throw 'OfficeInstallationMismatch' }
    Assert-OfficeRuntimePaths $Publish $Repository $State $receipt.extractionRoot
    if ($receipt.extractionRootFingerprint -cne (Get-OfficeRootFingerprint $receipt.extractionRoot)) { throw 'OfficeInstallationMismatch' }
    $pin=Read-CourtJson (Join-Path $Repository 'scripts\office-court-question-package.json')
    if ($receipt.packageSha256 -cne $pin.packageSha256) { throw 'QuestionPackageHashMismatch' }
    Assert-OfficeQuestionPackage (Join-Path $State 'package') $pin
    Assert-CourtFile $receipt.pythonExecutable $receipt.pythonSha256
    $hostEnv=Get-OfficeHostEnvironment $receipt.appPool
    $effective=Get-OfficeConfiguration $Publish $hostEnv (Read-OfficeXml (Join-Path $Publish 'web.config'))
    Assert-OfficeNoCloud $effective
    $expected=Get-OfficeRuntimeValues $receipt.pythonExecutable $State $receipt.extractionRoot $receipt.packageSha256
    foreach ($key in $expected.Keys) { if ($effective[$key] -cne $expected[$key]) { throw 'ApplicationRuntimeConfigurationMismatch' } }
    return $receipt
}
function Assert-OfficeQuestionHealth($Health, $Receipt) {
    if ($null -eq $Health) { return }
    if ($Health.httpStatus -ne 200 -or $Health.data.questionServiceState -cne 'Ready' -or
        $Health.data.modelVersion -cne (Get-CourtTransferPolicy).ModelVersion -or
        $Health.data.questionPackageSha256 -cne $Receipt.packageSha256 -or
        $Health.data.extractionRootFingerprint -cne (Get-OfficeRootFingerprint $Receipt.extractionRoot)) { throw 'QuestionRuntimeMismatch' }
}
function Get-OfficeVerifiedCache([Guid]$CaseId, [Management.Automation.PSCredential]$Credential) {
    # Normal local cookie login, followed by the API's read-only full source/claim validation.
    $handler=[Net.Http.HttpClientHandler]::new();$handler.UseProxy=$false;$handler.AllowAutoRedirect=$false;$handler.UseCookies=$true
    $handler.CookieContainer=[Net.CookieContainer]::new()
    $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(30)
    $client.MaxResponseContentBufferSize=128KB
    $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($Credential.Password)
    try {
        $loginBody=@{username=$Credential.UserName;password=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)} | ConvertTo-Json -Compress
        $content=[Net.Http.StringContent]::new($loginBody,[Text.Encoding]::UTF8,'application/json')
        try {
            $login=$client.PostAsync('http://127.0.0.1/api/auth/login',$content).GetAwaiter().GetResult()
            try { if (-not $login.IsSuccessStatusCode) { throw 'LocalLacAuthenticationFailed' } } finally { $login.Dispose() }
        } finally { $content.Dispose();$loginBody=$null }
        $response=$client.GetAsync('http://127.0.0.1/api/court-cases/'+$CaseId.ToString()+'/intelligence',[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) { throw 'CachedIntelligenceApiReadFailed' }
            $stream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();$memory=[IO.MemoryStream]::new();$buffer=New-Object byte[] 8192
            try {
                while ($true) { $read=$stream.ReadAsync($buffer,0,$buffer.Length);if (-not $read.Wait(30000)) { throw 'Cache response timeout.' };$n=$read.Result;if ($n -eq 0) { break };$memory.Write($buffer,0,$n);if ($memory.Length -gt 5MB) { throw 'Cache response oversized.' } }
                $data=[Text.Encoding]::UTF8.GetString($memory.ToArray()) | ConvertFrom-Json
            } finally { $memory.Dispose() }
            Assert-OfficeCachedIntelligenceResponse $CaseId $data
        } finally { $response.Dispose() }
    } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$client.Dispose();$handler.Dispose() }
}
function Assert-OfficeCachedIntelligenceResponse([Guid]$CaseId, $Data) {
    if ($Data.caseId -cne $CaseId.ToString() -or $Data.progressSummary.usableBriefs -lt 1) { throw 'NoVerifiedCachedBriefsForCase' }
    [pscustomobject]@{state='VerifiedCachedIntelligenceReadable';usableBriefs=$Data.progressSummary.usableBriefs;officialSources=$Data.progressSummary.officialSources}
}
