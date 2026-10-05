#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$IncomingDirectory = 'D:\LAC-CourtAI-Incoming',
    [string]$Destination = 'D:\LAC-CourtAI-V3-Office',
    [string]$ExpectedTransferManifestSha256,
    [switch]$LibraryOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-CourtTransferPolicy {
    [pscustomobject]@{
        ModelVersion = 'Pilot-V3-Qwen3-4B-Instruct-2507-Q4_K_M-LoRA-F32-b11321'
        SourceManifestSha256 = '926a2dc2002941987c40b4ddd656e5e4b12d5c5e2fa74423fb3500b8f026631b'
        BaseSha256 = '9657e9d21175ed290fa4ec3662fffb61a001463ac4ec1abd62deaa509b440710'
        LoraSha256 = 'f002c12aef178431cd56bc482350f3733e92f51972a668ed119a7d946e68daf1'
        ServerSha256 = '9c2cfb0c15c3acca4587aa0c877f27b342c3c4ee4e1d2a6a5e68316064142ebe'
        RuntimeControllerSha256 = 'dd2638ed49685a4e47a2e61c91352e6f9b8d4b8353e51fa12394ddf9147c0102'
        OfficeRoot = 'D:\LAC-CourtAI-V3-Office'
        MaxPartBytes = [long](750 * 1MB)
        ArchiveName = 'court-v3-office.tar'
    }
}
function Get-CourtSha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Assert-CourtFile([string]$Path, [string]$Sha256, [long]$Bytes = -1) {
    if ($Sha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid SHA256 value.' }
    if (-not [IO.File]::Exists($Path)) { throw "Missing file: $Path" }
    $file = Get-Item -LiteralPath $Path -Force
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse file refused: $Path" }
    if ($Bytes -ge 0 -and $file.Length -ne $Bytes) { throw "Wrong file size: $Path" }
    if ((Get-CourtSha256 $Path) -cne $Sha256) { throw "Wrong SHA256: $Path" }
}
function Get-CourtAbsolutePath([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]') { throw 'An absolute local Windows path is required.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Length -le 3) { throw 'A drive root cannot be the target directory.' }
    $cursor = $full
    while ($cursor.Length -gt 3) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse directories are not allowed.'
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}
function Assert-CourtNewDestination([string]$Path) {
    $full = Get-CourtAbsolutePath $Path
    if (Test-Path -LiteralPath $full) { throw "Existing destination refused: $full" }
    return $full
}
function Assert-CourtSpace([string]$Path, [long]$RequiredBytes) {
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Path))
    if ($drive.AvailableFreeSpace -lt $RequiredBytes) {
        throw ('Insufficient disk space on {0}: need {1:N0} bytes; free {2:N0} bytes. No source files changed.' -f $drive.Name,$RequiredBytes,$drive.AvailableFreeSpace)
    }
}
function Assert-CourtSeparateOutput([string]$Output, [string[]]$SourceRoots) {
    $out=Get-CourtAbsolutePath $Output
    foreach ($root in $SourceRoots) {
        $source=Get-CourtAbsolutePath $root
        if ($out.Equals($source,[StringComparison]::OrdinalIgnoreCase) -or
            $out.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or
            $source.StartsWith($out+'\',[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Output overlaps a preserved source package.'
        }
    }
}
function Write-CourtJson([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 100) + "`n")
    $stream = [IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
}
function Read-CourtJson([string]$Path) {
    if (-not [IO.File]::Exists($Path) -or (Get-Item -LiteralPath $Path).Length -gt 1MB) { throw 'Missing or oversized JSON manifest.' }
    [IO.File]::ReadAllText($Path,[Text.Encoding]::UTF8) | ConvertFrom-Json
}
function Assert-CourtSourceManifest($Manifest) {
    $p = Get-CourtTransferPolicy
    if ($Manifest.modelVersion -cne $p.ModelVersion -or $Manifest.pilotVersion -cne 'V3' -or
        $Manifest.baseGgufSha256 -cne $p.BaseSha256 -or $Manifest.loraGgufSha256 -cne $p.LoraSha256 -or
        $Manifest.serverSha256 -cne $p.ServerSha256 -or $Manifest.quantization -cne 'Q4_K_M' -or
        $Manifest.loraPrecision -cne 'F32' -or $Manifest.cloudFallback -ne $false -or
        $Manifest.modelEndpoint -cne 'http://127.0.0.1:8096' -or $Manifest.questionEndpoint -cne 'http://127.0.0.1:8097' -or
        $Manifest.parallel -ne 1 -or $Manifest.gpuLayers -ne 0) { throw 'Accepted V3 identity or policy mismatch.' }
    foreach ($name in @($Manifest.baseGgufFile,$Manifest.loraGgufFile) + @($Manifest.runtimeFileSha256.PSObject.Properties.Name)) {
        if ($name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw 'Unsafe runtime filename.' }
    }
    if ($Manifest.runtimeFileSha256.'llama-server.exe' -cne $p.ServerSha256) { throw 'Pinned llama-server inventory mismatch.' }
}
function New-CourtPortableManifest($Source, [string]$RecoverySha256) {
    $p = Get-CourtTransferPolicy
    $copy = ($Source | ConvertTo-Json -Depth 100) | ConvertFrom-Json
    $copy.baseGgufPath = $p.OfficeRoot + '\' + $Source.baseGgufFile
    $copy.loraGgufPath = $p.OfficeRoot + '\' + $Source.loraGgufFile
    $copy.serverPath = $p.OfficeRoot + '\bin\llama-server.exe'
    $copy | Add-Member -NotePropertyName portableDeployment -NotePropertyValue ([pscustomobject][ordered]@{
        formatVersion = 1; intendedRoot = $p.OfficeRoot; sourceManifestSha256 = $p.SourceManifestSha256
        runtimeController = 'tools/court-order-intelligence/runtime_recovery.py'; runtimeControllerSha256 = $RecoverySha256
        startupProfileAuthority = 'Accepted runtime_recovery.py; historical contextSize/threads metadata is not a startup command'
        modelStarted = $false
    })
    return $copy
}
function Assert-CourtPortableManifest($Source, $Portable, [string]$RecoverySha256) {
    $expected = New-CourtPortableManifest $Source $RecoverySha256
    # Recursive comparison ignores only JSON property order, not any value or extra field.
    function Compare-JsonValue($A,$B) {
        if ($null -eq $A -or $null -eq $B) { return ($null -eq $A -and $null -eq $B) }
        if ($A -is [pscustomobject]) {
            if ($B -isnot [pscustomobject]) { return $false }
            $keys = @($A.PSObject.Properties.Name | Sort-Object)
            $other = @($B.PSObject.Properties.Name | Sort-Object)
            if (($keys -join '|') -cne ($other -join '|')) { return $false }
            foreach ($key in $keys) { if (-not (Compare-JsonValue $A.$key $B.$key)) { return $false } }
            return $true
        }
        if ($A -is [Array]) {
            if ($B -isnot [Array] -or $A.Count -ne $B.Count) { return $false }
            for ($i=0;$i -lt $A.Count;$i++) { if (-not (Compare-JsonValue $A[$i] $B[$i])) { return $false } }
            return $true
        }
        return (($A | ConvertTo-Json -Compress -Depth 100) -ceq ($B | ConvertTo-Json -Compress -Depth 100))
    }
    if (-not (Compare-JsonValue $expected $Portable)) { throw 'Portable manifest differs beyond approved runtime paths/packaging metadata.' }
}
function Get-CourtBinaryInventory($Source) {
    @([pscustomobject]@{path=$Source.baseGgufFile;sha256=$Source.baseGgufSha256;bytes=[long]$Source.baseGgufBytes},
      [pscustomobject]@{path=$Source.loraGgufFile;sha256=$Source.loraGgufSha256;bytes=[long]$Source.loraGgufBytes})
    foreach ($file in ($Source.runtimeFileSha256.PSObject.Properties | Sort-Object Name)) {
        [pscustomobject]@{path=('bin/'+$file.Name);sha256=$file.Value;bytes=[long]-1}
    }
}
function Assert-CourtParts([string]$Incoming, $Transfer) {
    $p=Get-CourtTransferPolicy
    if ($Transfer.formatVersion -ne 1 -or $Transfer.modelVersion -cne $p.ModelVersion -or
        $Transfer.intendedRoot -cne $p.OfficeRoot -or $Transfer.archive.name -cne $p.ArchiveName -or
        $Transfer.sourceManifestSha256 -cne $p.SourceManifestSha256 -or
        $Transfer.runtimeControllerSha256 -cne $p.RuntimeControllerSha256 -or
        $Transfer.archive.bytes -le 0 -or $Transfer.archive.bytes -gt 5GB) { throw 'Transfer identity or size mismatch.' }
    $parts=@($Transfer.parts | Sort-Object index)
    if ($parts.Count -lt 1 -or $parts.Count -gt 128) { throw 'Invalid part count.' }
    $sum=[long]0
    for ($i=0;$i -lt $parts.Count;$i++) {
        $part=$parts[$i];$expectedName=$p.ArchiveName + ('.part{0:D4}' -f ($i+1))
        if ($part.index -ne ($i+1) -or $part.name -cne $expectedName -or $part.bytes -le 0 -or $part.bytes -gt $p.MaxPartBytes) { throw 'Invalid part order, name or size.' }
        Assert-CourtFile (Join-Path $Incoming $part.name) $part.sha256 ([long]$part.bytes)
        $sum += [long]$part.bytes
    }
    if ($sum -ne [long]$Transfer.archive.bytes) { throw 'Part sizes do not equal whole archive size.' }
    return $parts
}
function Join-CourtParts([string]$Incoming, $Parts, [string]$Archive, [string]$ExpectedSha256) {
    $output=[IO.File]::Open($Archive,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {
        foreach ($part in ($Parts | Sort-Object index)) {
            $input=[IO.File]::OpenRead((Join-Path $Incoming $part.name))
            try { $input.CopyTo($output,1MB) } finally { $input.Dispose() }
        }
    } finally { $output.Dispose() }
    Assert-CourtFile $Archive $ExpectedSha256
}
function Get-CourtTar {
    $path=Join-Path ([Environment]::SystemDirectory) 'tar.exe'
    if (-not [IO.File]::Exists($path)) { throw 'Windows tar.exe is required; nothing will be downloaded.' }
    return $path
}
function Assert-CourtTarMembers([string]$Archive, $Files) {
    # Do not extract untrusted paths or links. Preparation uses bounded USTAR (no PAX/GNU extensions).
    $allowed=@{};$seen=@{};$directories=@{'payload/'=$true;'payload/bin/'=$true}
    foreach ($file in $Files) {
        if ($file.path -cnotmatch '^(?:bin/)?[A-Za-z0-9][A-Za-z0-9._-]*$' -or $file.bytes -lt 0 -or $allowed.ContainsKey('payload/'+$file.path)) { throw 'Unsafe/duplicate inventory member.' }
        $allowed['payload/'+$file.path]=[long]$file.bytes
    }
    if ($allowed.Count -gt 128 -or $allowed.Count -lt 4) { throw 'Invalid archive inventory.' }
    $stream=[IO.File]::OpenRead($Archive);$block=New-Object byte[] 512
    try {
        $zeros=0
        while ($stream.Position -lt $stream.Length) {
            $read=0
            while ($read -lt 512) { $n=$stream.Read($block,$read,512-$read);if ($n -eq 0) { throw 'Truncated TAR header.' };$read+=$n }
            if (@($block | Where-Object { $_ -ne 0 }).Count -eq 0) { $zeros++;continue }
            if ($zeros -gt 0) { throw 'Nonzero TAR data after end marker.' }
            $checksumText=[Text.Encoding]::ASCII.GetString($block,148,8).Trim([char]0,[char]32)
            if ($checksumText -notmatch '^[0-7]+$') { throw 'Invalid TAR checksum field.' }
            $sum=0;for ($i=0;$i -lt 512;$i++) { if ($i -ge 148 -and $i -lt 156) { $sum+=32 } else { $sum+=$block[$i] } }
            if ($sum -ne [Convert]::ToInt64($checksumText,8)) { throw 'Wrong TAR header checksum.' }
            $name=[Text.Encoding]::ASCII.GetString($block,0,100).Split([char]0)[0]
            $prefix=[Text.Encoding]::ASCII.GetString($block,345,155).Split([char]0)[0]
            $link=[Text.Encoding]::ASCII.GetString($block,157,100).Split([char]0)[0]
            if ($prefix -or $link -or [Text.Encoding]::ASCII.GetString($block,257,5) -cne 'ustar') { throw 'Unsupported TAR extension/link.' }
            $sizeText=[Text.Encoding]::ASCII.GetString($block,124,12).Trim([char]0,[char]32)
            if ($sizeText -notmatch '^[0-7]+$') { throw 'Invalid TAR size.' }
            $size=[Convert]::ToInt64($sizeText,8);$type=$block[156]
            if ($seen.ContainsKey($name)) { throw 'Duplicate TAR member.' };$seen[$name]=$true
            if ($type -eq 53) {
                if (-not $directories.ContainsKey($name) -or $size -ne 0) { throw 'Unsafe TAR directory.' }
            } elseif ($type -eq 48 -or $type -eq 0) {
                if (-not $allowed.ContainsKey($name) -or $allowed[$name] -ne $size) { throw "Unexpected TAR member/size: $name" }
            } else { throw 'TAR links/devices/extensions refused.' }
            $skip=[long]([Math]::Ceiling($size/512.0)*512)
            if ($skip -gt $stream.Length-$stream.Position) { throw 'Truncated TAR member.' }
            $stream.Position += $skip
        }
        if ($zeros -lt 2) { throw 'Missing TAR end markers.' }
        foreach ($name in $allowed.Keys) { if (-not $seen.ContainsKey($name)) { throw "Missing TAR member: $name" } }
    } finally { $stream.Dispose() }
}
function Assert-CourtDeployment([string]$Payload, $Transfer) {
    $p=Get-CourtTransferPolicy
    Assert-CourtFile (Join-Path $Payload 'SOURCE-DEPLOYMENT-MANIFEST.json') $p.SourceManifestSha256
    Assert-CourtFile (Join-Path $Payload 'court-model-manifest.json') $Transfer.portableManifestSha256
    $source=Read-CourtJson (Join-Path $Payload 'SOURCE-DEPLOYMENT-MANIFEST.json')
    $portable=Read-CourtJson (Join-Path $Payload 'court-model-manifest.json')
    Assert-CourtSourceManifest $source
    Assert-CourtPortableManifest $source $portable $Transfer.runtimeControllerSha256
    $expected=@(Get-CourtBinaryInventory $source)
    $expected += [pscustomobject]@{path='SOURCE-DEPLOYMENT-MANIFEST.json';sha256=$p.SourceManifestSha256;bytes=-1}
    $expected += [pscustomobject]@{path='court-model-manifest.json';sha256=$Transfer.portableManifestSha256;bytes=-1}
    if (@($Transfer.files).Count -ne $expected.Count) { throw 'Incomplete deployment inventory.' }
    foreach ($file in $expected) {
        $rows=@($Transfer.files | Where-Object { $_.path -ceq $file.path })
        if ($rows.Count -ne 1 -or $rows[0].sha256 -cne $file.sha256) { throw 'Deployment inventory pin mismatch.' }
        Assert-CourtFile (Join-Path $Payload $file.path) $file.sha256 ([long]$rows[0].bytes)
        if ($file.bytes -ge 0 -and [long]$rows[0].bytes -ne $file.bytes) { throw 'Pinned model byte count mismatch.' }
    }
    $actual=@(Get-ChildItem -LiteralPath $Payload -Recurse -File -Force)
    if ($actual.Count -ne $expected.Count) { throw 'Unexpected deployment file.' }
}

if ($LibraryOnly) { return }
# Destination refusal happens before joining, extraction or any overwrite.
$target=Assert-CourtNewDestination $Destination
$incoming=Get-CourtAbsolutePath $IncomingDirectory
$p=Get-CourtTransferPolicy
if ($target -cne $p.OfficeRoot) { throw 'Destination must be the intended office root.' }
$manifestPath=Join-Path $incoming 'TRANSFER-MANIFEST.json'
if (-not $ExpectedTransferManifestSha256) { throw 'Supply ExpectedTransferManifestSha256 from the trusted GitHub report, not the incoming manifest itself.' }
Assert-CourtFile $manifestPath $ExpectedTransferManifestSha256
$transfer=Read-CourtJson $manifestPath
$parts=@(Assert-CourtParts $incoming $transfer) # ALL parts verified before joining
$payloadBytes=[long]0;foreach ($file in $transfer.files) { $payloadBytes += [long]$file.bytes }
if ($payloadBytes -le 0 -or $payloadBytes -gt 5GB) { throw 'Invalid payload size.' }
Assert-CourtSpace $target ([long]$transfer.archive.bytes+$payloadBytes+256MB)
$tar=Get-CourtTar
$parent=[IO.Path]::GetDirectoryName($target)
$work=Join-Path $parent ('.court-v3-restore-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work) | Out-Null
$archive=Join-Path $work $p.ArchiveName
Join-CourtParts $incoming $parts $archive $transfer.archive.sha256
Assert-CourtTarMembers $archive $transfer.files
& $tar -xf $archive -C $work
if ($LASTEXITCODE -ne 0) { throw "TAR extraction failed; new staging retained: $work" }
$payload=Join-Path $work 'payload'
Assert-CourtDeployment $payload $transfer
# Same-volume rename; race with a new destination fails rather than overwrites it.
[IO.Directory]::Move($payload,$target)
$receipt=[ordered]@{status='VERIFIED_INSTALLED';installedUtc=[DateTime]::UtcNow.ToString('o');destination=$target
    modelVersion=$p.ModelVersion;transferManifestSha256=$ExpectedTransferManifestSha256
    sourceManifestSha256=$p.SourceManifestSha256;portableManifestSha256=$transfer.portableManifestSha256
    archiveSha256=$transfer.archive.sha256;partsVerified=$parts.Count;filesVerified=@($transfer.files).Count;modelStarted=$false}
Write-CourtJson (Join-Path $target 'INSTALL-RECEIPT.json') $receipt
# Only remove the exact newly created joined TAR; no recursive delete or incoming-file deletion.
if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($archive)) -cne $work) { throw 'Cleanup boundary mismatch.' }
Remove-Item -LiteralPath $archive
[IO.Directory]::Delete($work,$false)
Write-Output ('VERIFIED: installed {0}; {1} parts / {2} files checked. Model NOT started. Manifest SHA256: {3}' -f $target,$parts.Count,@($transfer.files).Count,$transfer.portableManifestSha256)
