#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SourceManifest = 'D:\LAC-CourtAI-V3-20261003-r1\court-model-manifest.json',
    [string]$OutputDirectory = 'D:\LAC-CourtAI-Office-Transfer'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'restore-court-v3-office-transfer.ps1') -LibraryOnly
$p=Get-CourtTransferPolicy
$output=Assert-CourtNewDestination $OutputDirectory
$sourcePath=Get-CourtAbsolutePath $SourceManifest
Assert-CourtFile $sourcePath $p.SourceManifestSha256
$source=Read-CourtJson $sourcePath
Assert-CourtSourceManifest $source
Assert-CourtSeparateOutput $output @([IO.Path]::GetDirectoryName($sourcePath),
    [IO.Path]::GetDirectoryName($source.baseGgufPath),
    [IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($source.serverPath)))
$repo=Split-Path -Parent $PSScriptRoot
$controller=Join-Path $repo 'tools\court-order-intelligence\runtime_recovery.py'
$controllerHash=Get-CourtSha256 $controller
# Pinned accepted controller, not a manifest-derived 4096/2-thread launch profile.
$acceptedControllerHash=$p.RuntimeControllerSha256
if ($controllerHash -cne $acceptedControllerHash) { throw 'Accepted runtime_recovery.py hash mismatch.' }
$inventory=@(Get-CourtBinaryInventory $source)
$sourceFiles=@()
$payloadBytes=[long]0
foreach ($file in $inventory) {
    $path=if ($file.path -ceq $source.baseGgufFile) { $source.baseGgufPath }
          elseif ($file.path -ceq $source.loraGgufFile) { $source.loraGgufPath }
          else { Join-Path ([IO.Path]::GetDirectoryName($source.serverPath)) $file.path.Substring(4) }
    $path=Get-CourtAbsolutePath $path
    Assert-CourtFile $path $file.sha256 $file.bytes
    $length=(Get-Item -LiteralPath $path).Length
    $payloadBytes += $length
    $sourceFiles += [pscustomobject]@{sourcePath=$path;path=$file.path;sha256=$file.sha256;bytes=[long]$length}
}
# Three independent copies coexist: payload, uncompressed TAR, transport parts.
Assert-CourtSpace $output ($payloadBytes*3+256MB)
$tar=Get-CourtTar
[IO.Directory]::CreateDirectory($output) | Out-Null
$payload=Join-Path $output 'payload'
[IO.Directory]::CreateDirectory((Join-Path $payload 'bin')) | Out-Null
foreach ($file in $sourceFiles) {
    $destination=Join-Path $payload $file.path
    [IO.File]::Copy($file.sourcePath,$destination,$false)
    Assert-CourtFile $destination $file.sha256 $file.bytes
}
$original=Join-Path $payload 'SOURCE-DEPLOYMENT-MANIFEST.json'
[IO.File]::Copy($sourcePath,$original,$false)
Assert-CourtFile $original $p.SourceManifestSha256
$portablePath=Join-Path $payload 'court-model-manifest.json'
Write-CourtJson $portablePath (New-CourtPortableManifest $source $controllerHash)
$portableHash=Get-CourtSha256 $portablePath
$files=@($sourceFiles | Select-Object path,sha256,bytes)
$files += [pscustomobject]@{path='SOURCE-DEPLOYMENT-MANIFEST.json';sha256=$p.SourceManifestSha256;bytes=[long](Get-Item -LiteralPath $original).Length}
$files += [pscustomobject]@{path='court-model-manifest.json';sha256=$portableHash;bytes=[long](Get-Item -LiteralPath $portablePath).Length}
$transfer=[ordered]@{formatVersion=1;modelVersion=$p.ModelVersion;intendedRoot=$p.OfficeRoot
    sourceManifestSha256=$p.SourceManifestSha256;portableManifestSha256=$portableHash;runtimeControllerSha256=$controllerHash
    files=$files;archive=$null;parts=@();modelStarted=$false}
Assert-CourtDeployment $payload ([pscustomobject]$transfer)
$archive=Join-Path $output $p.ArchiveName
# Explicit USTAR, no compression, no executable invocation except Windows tar.exe.
& $tar --format ustar -cf $archive -C $output payload
if ($LASTEXITCODE -ne 0) { throw "TAR creation failed; new staging retained: $output" }
Assert-CourtTarMembers $archive $files
$transfer.archive=[ordered]@{name=$p.ArchiveName;bytes=[long](Get-Item -LiteralPath $archive).Length;sha256=Get-CourtSha256 $archive}
$input=[IO.File]::OpenRead($archive);$buffer=New-Object byte[] (1MB);$partIndex=0
try {
    while ($input.Position -lt $input.Length) {
        $partIndex++;$name=$p.ArchiveName+('.part{0:D4}' -f $partIndex);$path=Join-Path $output $name
        $part=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try {
            $remaining=[Math]::Min($p.MaxPartBytes,$input.Length-$input.Position)
            while ($remaining -gt 0) {
                $count=$input.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining))
                if ($count -eq 0) { throw 'Unexpected TAR EOF during split.' }
                $part.Write($buffer,0,$count);$remaining-=$count
            }
        } finally { $part.Dispose() }
        $transfer.parts += [pscustomobject]@{index=$partIndex;name=$name;bytes=[long](Get-Item -LiteralPath $path).Length;sha256=Get-CourtSha256 $path}
    }
} finally { $input.Dispose() }
Assert-CourtParts $output ([pscustomobject]$transfer) | Out-Null
$transferPath=Join-Path $output 'TRANSFER-MANIFEST.json'
Write-CourtJson $transferPath $transfer
$transferHash=Get-CourtSha256 $transferPath
$report=[ordered]@{status='VERIFIED_TRANSFER_READY';createdUtc=[DateTime]::UtcNow.ToString('o');sourceManifest=$sourcePath
    sourceManifestSha256=$p.SourceManifestSha256;sourceFiles=$sourceFiles;portableManifestSha256=$portableHash
    runtimeControllerSha256=$controllerHash;transferManifestSha256=$transferHash;tar=$transfer.archive;parts=$transfer.parts
    installedBinaryBytes=$payloadBytes;estimatedOfficePeakBytes=([long]$transfer.archive.bytes*2+$payloadBytes+256MB)
    modelStarted=$false;weightsModified=$false
    laptopCommand="powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-court-v3-office-transfer.ps1 -OutputDirectory `"$output`""
    officeCommand="powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\restore-court-v3-office-transfer.ps1 -IncomingDirectory D:\LAC-CourtAI-Incoming -ExpectedTransferManifestSha256 $transferHash"}
Write-CourtJson (Join-Path $output 'TRANSFER-REPORT.json') $report
Write-Output ('VERIFIED: {0}; TAR {1:N0} bytes; {2} parts <=750 MiB. Model NOT started.' -f $output,$transfer.archive.bytes,$partIndex)
Write-Output ('TRANSFER-MANIFEST SHA256: '+$transferHash)
Write-Output $report.officeCommand
