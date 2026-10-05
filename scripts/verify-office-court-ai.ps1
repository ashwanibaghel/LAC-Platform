#requires -Version 5.1
[CmdletBinding()]
param([Guid]$VerifyCaseId=[Guid]::Empty, [Management.Automation.PSCredential]$Credential, [switch]$LibraryOnly)
. (Join-Path $PSScriptRoot 'office-court-runtime-common.ps1')
if ($LibraryOnly) { return }
Assert-OfficeAdministrator
$state='D:\LAC-CourtAI-Runtime'
$receipt=Read-OfficeInstallation $state 'C:\LAC-Platform' 'C:\LAC-Publish'
$manifest=Assert-OfficeDeployment 'D:\LAC-CourtAI-V3-Office'
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in @(8096,8097) })
Assert-OfficeListeners $listeners
Assert-OfficeProcessContract $listeners @(Get-CimInstance Win32_Process -ErrorAction Stop) $receipt $manifest
$question=Get-OfficeHealth 8097;Assert-OfficeQuestionHealth $question $receipt
$model=Get-OfficeHealth 8096
$recordPath=Join-Path $state 'services\recovery.json'
$recovery=Get-OfficeVerifiedRecovery $recordPath $receipt
$summary=Get-OfficeServiceSummary $question $model $recovery
# Readable files are diagnostics, never a substitute for the API's evidence validation.
$artifactDirectory=Join-Path $receipt.extractionRoot 'court-intelligence'
$readable=0
if ([IO.Directory]::Exists($artifactDirectory)) {
    [void](Get-CourtAbsolutePath $artifactDirectory)
    foreach ($file in @(Get-ChildItem -LiteralPath $artifactDirectory -Filter '*.json' -File -Recurse)) {
        [void](Get-CourtAbsolutePath $file.FullName)
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse cached file refused.' }
        $stream=[IO.File]::Open($file.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try { [void]$stream.ReadByte();$readable++ } finally { $stream.Dispose() }
    }
}
$cache=[pscustomobject]@{state=$(if ($readable -gt 0) { 'DiskReadCheckedApiValidationNotRequested' } else { 'NoCachedIntelligenceFiles' });readableJsonFiles=$readable}
if ($VerifyCaseId -ne [Guid]::Empty) {
    if ($null -eq $Credential) { $Credential=Get-Credential -Message 'Sign in to local LAC to verify cached Court intelligence' }
    if ($null -eq $Credential) { throw 'Authenticated cache verification cancelled.' }
    $cache=Get-OfficeVerifiedCache $VerifyCaseId $Credential
}
$runtimeState=if ($summary.reasonCode -eq 'ModelInsufficientMemory') { 'Failed' } elseif ($recovery -and $recovery.runtimeState -eq 'Starting') { 'Starting' } elseif ($summary.modelState -eq 'Starting') { 'Starting' } elseif ($summary.questionServiceState -ne 'Ready') { 'QuestionServiceOffline' } elseif ($summary.modelState -eq 'Ready') { 'Ready' } else { 'ModelOffline' }
if ($recovery -and $recovery.runtimeState -eq 'Failed' -and $summary.modelState -ne 'Ready') { $runtimeState='Failed';$summary.reasonCode=$recovery.reasonCode }
[ordered]@{manifestIdentity='Verified';runtimePackage='Verified';loopbackOnly=$true;cloudFallback=$false
    extractionRootFingerprint=$receipt.extractionRootFingerprint;runtimeState=$runtimeState
    questionServiceState=$summary.questionServiceState;modelState=$summary.modelState;reasonCode=$summary.reasonCode
    cachedIntelligence=$cache;inferenceAttempted=$false} | ConvertTo-Json -Depth 5 -Compress
if ($summary.questionServiceState -ne 'Ready') { exit 3 }
if ($summary.modelState -ne 'Ready') { exit 2 } # Cached verified facts can still be used; no fallback.
