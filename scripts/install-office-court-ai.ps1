#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PythonExecutable,
    [string]$AppPool='DefaultAppPool',
    [switch]$RestoreIncoming,
    [switch]$LibraryOnly
)
. (Join-Path $PSScriptRoot 'office-court-runtime-common.ps1')
if ($LibraryOnly) { return }
Assert-OfficeAdministrator
$repository='C:\LAC-Platform';$publish='C:\LAC-Publish';$state='D:\LAC-CourtAI-Runtime';$deployment='D:\LAC-CourtAI-V3-Office'
if ((Get-CourtAbsolutePath (Split-Path -Parent $PSScriptRoot)) -cne $repository) { throw 'Run this reviewed script from C:\LAC-Platform on the Office PC.' }
$webPath=Join-Path $publish 'web.config';$xml=Read-OfficeXml $webPath
$beforeSha=Get-CourtSha256 $webPath
$effective=Get-OfficeConfiguration $publish (Get-OfficeHostEnvironment $AppPool) $xml
Assert-OfficeNoCloud $effective
$root=$effective['Storage__ExtractionRoot']
Assert-OfficeRuntimePaths $publish $repository $state $root
$pin=Read-CourtJson (Join-Path $PSScriptRoot 'office-court-question-package.json')
$sourcePackage=Join-Path $repository 'tools\court-order-intelligence'
Assert-OfficeQuestionPackage $sourcePackage $pin
if (-not $PythonExecutable) { $PythonExecutable=$effective['CourtRuntime__PythonExecutable'] }
if (-not $PythonExecutable) { $PythonExecutable=(Get-Command python.exe -CommandType Application -ErrorAction Stop).Source }
$python=Get-CourtAbsolutePath $PythonExecutable
if (-not [IO.File]::Exists($python) -or $python -match '\\WindowsApps\\') { throw 'Existing x64 Python with Court dependencies is required; nothing will be downloaded.' }
# Import/version preflight only. No model, services, inference or package installer.
$preflight=& $python -I -c "import sys,struct,requests,psutil,fitz,jsonschema;from importlib.metadata import version;assert sys.version_info >= (3,10) and struct.calcsize('P') == 8;assert (2,32) <= tuple(map(int,requests.__version__.split('.')[:2])) < (3,0);assert (1,24) <= tuple(map(int,fitz.VersionBind.split('.')[:2])) < (2,0);assert (4,23) <= tuple(map(int,version('jsonschema').split('.')[:2])) < (5,0)" 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Python dependency/version preflight failed. Use a provisioned x64 Python environment; no installation was attempted.' }
if (-not [IO.Directory]::Exists($deployment)) {
    if (-not $RestoreIncoming) { throw 'Restored V3 deployment missing. Use -RestoreIncoming to run the accepted checksum-verified restore.' }
    & (Join-Path $PSScriptRoot 'restore-court-v3-office-transfer.ps1') -ExpectedTransferManifestSha256 $script:OfficeTransferSha | Out-Null
}
$manifest=Assert-OfficeDeployment $deployment
[IO.Directory]::CreateDirectory($state) | Out-Null
Set-OfficePrivateAcl $state $AppPool -ReadAccess
$guard=[IO.File]::Open((Join-Path $state 'install.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $receiptPath=Join-Path $state 'OFFICE-RUNTIME-INSTALL.json'
    $values=Get-OfficeRuntimeValues $python $state $root $pin.packageSha256
    if ([IO.File]::Exists($receiptPath)) {
        $existing=Read-OfficeInstallation $state $repository $publish
        if ($existing.pythonExecutable -cne $python -or $existing.appPool -cne $AppPool) { throw 'Existing installation differs; explicit rollback/reinstall required.' }
        Write-Output 'Verified existing installation. No changes; no services started.'
        return
    }
    $package=Join-Path $state 'package'
    if (Test-Path -LiteralPath $package) { throw 'Unreceipted runtime package exists; inspect/rollback before reinstalling.' }
    [IO.Directory]::CreateDirectory($package) | Out-Null
    foreach ($file in $pin.files) { [IO.File]::Copy((Join-Path $sourcePackage $file.name),(Join-Path $package $file.name)) }
    Assert-OfficeQuestionPackage $package $pin
    Set-OfficePrivateAcl $package $AppPool -ReadAccess
    $services=Join-Path $state 'services';[IO.Directory]::CreateDirectory($services) | Out-Null
    Set-OfficePrivateAcl $services $AppPool -RuntimeAccess
    $backups=Join-Path $state 'backups';[IO.Directory]::CreateDirectory($backups) | Out-Null
    Set-OfficePrivateAcl $backups $AppPool
    $backup=Join-Path $backups ('web.config.'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.backup')
    [IO.File]::Copy($webPath,$backup)
    Assert-CourtFile $backup $beforeSha
    Set-OfficeWebEnvironment $xml $values
    Write-OfficeConfigurationAtomic $webPath $xml $beforeSha
    $receipt=[ordered]@{formatVersion=1;installedUtc=[DateTime]::UtcNow.ToString('o');repository=$repository;publishRoot=$publish
        stateDirectory=$state;appPool=$AppPool;extractionRoot=$root;extractionRootFingerprint=Get-OfficeRootFingerprint $root
        pythonExecutable=$python;pythonSha256=Get-CourtSha256 $python;packageSha256=$pin.packageSha256
        manifestSha256=$script:OfficePortableSha;originalWebConfigSha256=$beforeSha;backupPath=$backup
        installedWebConfigSha256=Get-CourtSha256 $webPath;modelStarted=$false;servicesStarted=$false}
    Write-CourtJson $receiptPath $receipt
    Write-Output 'Verified Court runtime installed/configured. Existing extraction root preserved. No services or inference started.'
} finally { $guard.Dispose() }
