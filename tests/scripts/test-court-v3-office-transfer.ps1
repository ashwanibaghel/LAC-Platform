#requires -Version 5.1
[CmdletBinding()]
param([string]$SourceManifest='D:\LAC-CourtAI-V3-20261003-r1\court-model-manifest.json')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'scripts\restore-court-v3-office-transfer.ps1') -LibraryOnly
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('court-transfer-tests-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$script:passed=0
function Assert-True($Condition,[string]$Name) { if (-not $Condition) { throw "FAIL: $Name" };$script:passed++;Write-Output "PASS: $Name" }
function Assert-Rejected([scriptblock]$Action,[string]$Message,[string]$Name) {
    $rejected=$false
    try { & $Action | Out-Null } catch { if ($_.Exception.Message -notlike "*$Message*") { throw };$rejected=$true }
    Assert-True $rejected $Name
}
$policy=Get-CourtTransferPolicy
Assert-CourtFile (Join-Path $repo 'tools\court-order-intelligence\runtime_recovery.py') $policy.RuntimeControllerSha256
Assert-True $true 'Accepted recovery-controller bytes/settings are unchanged'
$before=@(Get-Process -Name llama-server -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$incoming=Join-Path $testRoot 'incoming';[IO.Directory]::CreateDirectory($incoming) | Out-Null
$bytes=[Text.Encoding]::UTF8.GetBytes('deterministic uncompressed transport bytes')
$whole=Join-Path $testRoot 'whole.tar';[IO.File]::WriteAllBytes($whole,$bytes)
$parts=@()
for ($i=0;$i -lt 3;$i++) {
    $offset=$i*14;$length=[Math]::Min(14,$bytes.Length-$offset)
    $chunk=New-Object byte[] $length;[Array]::Copy($bytes,$offset,$chunk,0,$length)
    $name=$policy.ArchiveName+('.part{0:D4}' -f ($i+1));$path=Join-Path $incoming $name
    [IO.File]::WriteAllBytes($path,$chunk)
    $parts += [pscustomobject]@{index=$i+1;name=$name;bytes=$length;sha256=Get-CourtSha256 $path}
}
$transfer=[pscustomobject]@{formatVersion=1;modelVersion=$policy.ModelVersion;intendedRoot=$policy.OfficeRoot
    sourceManifestSha256=$policy.SourceManifestSha256;runtimeControllerSha256=$policy.RuntimeControllerSha256
    archive=[pscustomobject]@{name=$policy.ArchiveName;bytes=$bytes.Length;sha256=Get-CourtSha256 $whole};parts=@($parts[2],$parts[0],$parts[1])}
$ordered=@(Assert-CourtParts $incoming $transfer)
$joined=Join-Path $testRoot 'joined.tar';Join-CourtParts $incoming $ordered $joined $transfer.archive.sha256
Assert-True ((Get-CourtSha256 $joined) -ceq $transfer.archive.sha256) 'Valid parts join in deterministic index order'
$first=Join-Path $incoming $parts[0].name
$original=[IO.File]::ReadAllBytes($first);$tampered=[byte[]]$original.Clone();$tampered[0]=$tampered[0] -bxor 1
[IO.File]::WriteAllBytes($first,$tampered)
Assert-Rejected { Assert-CourtParts $incoming $transfer } 'Wrong SHA256' 'Wrong part hash rejected before joining'
[IO.File]::WriteAllBytes($first,$original)
$last=Join-Path $incoming $parts[2].name;$hidden=$last+'.held';[IO.File]::Move($last,$hidden)
Assert-Rejected { Assert-CourtParts $incoming $transfer } 'Missing file' 'Missing part rejected before joining'
[IO.File]::Move($hidden,$last)
Assert-Rejected { Join-CourtParts $incoming $ordered (Join-Path $testRoot 'wrong-whole.tar') ('0'*64) } 'Wrong SHA256' 'Wrong whole archive hash rejected'
$existing=Join-Path $testRoot 'existing';[IO.Directory]::CreateDirectory($existing) | Out-Null
$sentinel=Join-Path $existing 'accepted.txt';[IO.File]::WriteAllText($sentinel,'accepted deployment')
Assert-Rejected { Assert-CourtNewDestination $existing } 'Existing destination' 'Existing destination refused'
Assert-True ([IO.File]::ReadAllText($sentinel) -ceq 'accepted deployment') 'Existing destination is untouched'
Assert-Rejected { Assert-CourtNewDestination 'D:\' } 'drive root' 'Drive root cannot be an install target'
Assert-Rejected { Assert-CourtSeparateOutput 'D:\source\nested-output' @('D:\source') } 'overlaps' 'Output cannot be inside a source package'
Assert-Rejected { Assert-CourtSeparateOutput 'D:\source' @('D:\source\nested-package') } 'overlaps' 'Output cannot contain a source package'

# Actual pinned manifest, tiny bad model: rejection does not require loading a model.
Assert-CourtFile $SourceManifest $policy.SourceManifestSha256
$source=Read-CourtJson $SourceManifest;Assert-CourtSourceManifest $source
$portable=New-CourtPortableManifest $source $policy.RuntimeControllerSha256
$portablePath=Join-Path $testRoot 'portable.json';Write-CourtJson $portablePath $portable
Assert-CourtPortableManifest $source (Read-CourtJson $portablePath) $policy.RuntimeControllerSha256
Assert-True $true 'Only runtime paths/packaging metadata change in portable manifest'
$bad=Read-CourtJson $portablePath;$bad.threads=4
Assert-Rejected { Assert-CourtPortableManifest $source $bad $policy.RuntimeControllerSha256 } 'differs beyond' 'Runtime setting mutation rejected'
$bad=$null
$payload=Join-Path $testRoot 'bad-model';[IO.Directory]::CreateDirectory($payload) | Out-Null
[IO.File]::Copy($SourceManifest,(Join-Path $payload 'SOURCE-DEPLOYMENT-MANIFEST.json'))
[IO.File]::Copy($portablePath,(Join-Path $payload 'court-model-manifest.json'))
[IO.File]::WriteAllBytes((Join-Path $payload $source.baseGgufFile),[byte[]]@(1))
$files=@(Get-CourtBinaryInventory $source)
foreach ($file in $files) { if ($file.path -ceq $source.baseGgufFile) { $file.bytes=1 } }
$files += [pscustomobject]@{path='SOURCE-DEPLOYMENT-MANIFEST.json';sha256=$policy.SourceManifestSha256;bytes=(Get-Item -LiteralPath $SourceManifest).Length}
$files += [pscustomobject]@{path='court-model-manifest.json';sha256=Get-CourtSha256 $portablePath;bytes=(Get-Item -LiteralPath $portablePath).Length}
$deployment=[pscustomobject]@{files=$files;portableManifestSha256=Get-CourtSha256 $portablePath;runtimeControllerSha256=$policy.RuntimeControllerSha256}
Assert-Rejected { Assert-CourtDeployment $payload $deployment } 'Wrong SHA256' 'Wrong model hash rejected independently'

$tarRoot=Join-Path $testRoot 'tar-root';$tarPayload=Join-Path $tarRoot 'payload'
[IO.Directory]::CreateDirectory((Join-Path $tarPayload 'bin')) | Out-Null
$tarFiles=@()
foreach ($name in @('a','b','c','bin/d')) {
    $path=Join-Path $tarPayload $name;[IO.File]::WriteAllText($path,'synthetic bytes')
    $tarFiles += [pscustomobject]@{path=$name;bytes=(Get-Item -LiteralPath $path).Length}
}
$archive=Join-Path $testRoot 'safe.tar';$tar=Get-CourtTar
& $tar --format ustar -cf $archive -C $tarRoot payload
if ($LASTEXITCODE -ne 0) { throw 'Fixture tar failed.' }
Assert-CourtTarMembers $archive $tarFiles
Assert-True $true 'Windows USTAR files/headers/end markers verified before extraction'
$wrongInventory=@($tarFiles | Select-Object path,bytes);$wrongInventory[0].path='../escape'
Assert-Rejected { Assert-CourtTarMembers $archive $wrongInventory } 'Unsafe/duplicate' 'Traversal inventory rejected'
$unexpected=Join-Path $tarPayload 'not-allowed';[IO.File]::WriteAllText($unexpected,'unexpected')
$archive2=Join-Path $testRoot 'extra.tar'; & $tar --format ustar -cf $archive2 -C $tarRoot payload
Assert-Rejected { Assert-CourtTarMembers $archive2 $tarFiles } 'Unexpected TAR member' 'Unexpected archive file rejected before extraction'

foreach ($scriptPath in @((Join-Path $repo 'scripts\prepare-court-v3-office-transfer.ps1'),(Join-Path $repo 'scripts\restore-court-v3-office-transfer.ps1'))) {
    $text=[IO.File]::ReadAllText($scriptPath)
    Assert-True ($text -notmatch 'Start-Process|Invoke-Expression|Diagnostics\.Process|Invoke-WebRequest|WebClient') 'Script contains no process starter or downloader'
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$errors)
    Assert-True ($errors.Count -eq 0) 'PowerShell script parses'
    $calls=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand},$true))
    Assert-True ($calls.Count -eq 1 -and $calls[0].CommandElements[0].Extent.Text -ceq '$tar') 'Only external invocation is fixed Windows tar.exe'
}
$after=@(Get-Process -Name llama-server -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
Assert-True (($before -join ',') -ceq ($after -join ',')) 'No model process launched'
Write-Output "PASS: $script:passed focused assertions; fixtures retained at $testRoot"
