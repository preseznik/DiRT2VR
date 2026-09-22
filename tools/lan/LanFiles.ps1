Set-StrictMode -Version Latest
function Assert-LanPath([string]$Path) {
    $item=[IO.Path]::GetFullPath($Path)
    while ($item) {
        if ((Test-Path -LiteralPath $item) -and ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked paths are unsupported in the LAN lab: $item" }
        $item=[IO.Path]::GetDirectoryName($item)
    }
}
function Write-LanAtomic([string]$Path,[byte[]]$Bytes) {
    Assert-LanPath $Path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    $temporary=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try {
        $stream=[IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $stream.Write($Bytes,0,$Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary,$Path,[NullString]::Value) } else { [IO.File]::Move($temporary,$Path) }
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Get-LanHash([string]$Path) {
    if ([IO.File]::Exists($Path)) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return ''
}
function Restore-LanShim([string]$GameRoot) {
    $folder=Join-Path $GameRoot 'DiRT2VR/lan-backups'
    $pending=Join-Path $folder 'pending.json'
    Assert-LanPath $pending
    if (!(Test-Path -LiteralPath $pending)) { return }
    $journal=Get-Content -LiteralPath $pending -Raw | ConvertFrom-Json
    if ($journal.Version -ne 1 -or $journal.AppliedHash -notmatch '^[A-Fa-f0-9]{64}$' -or ($journal.OriginalHash -ne '' -and $journal.OriginalHash -notmatch '^[A-Fa-f0-9]{64}$')) { throw 'Invalid LAN recovery journal. Backups preserved.' }
    $target=Join-Path $GameRoot 'xlive.dll'
    $backup=Join-Path $folder 'xlive.original.dll'
    Assert-LanPath $target; Assert-LanPath $backup
    if ($journal.OriginalHash -ne '' -and (Get-LanHash $backup) -ne $journal.OriginalHash) { throw 'LAN original backup is missing or changed.' }
    $current=Get-LanHash $target
    if ($current -eq $journal.OriginalHash) { [IO.File]::Delete($pending); return }
    if ($current -ne $journal.AppliedHash) { throw 'xlive.dll changed outside the LAN test. Current file and backup preserved; resolve the recovery conflict first.' }
    if ($journal.OriginalHash -eq '') { [IO.File]::Delete($target) }
    else { Write-LanAtomic $target ([IO.File]::ReadAllBytes($backup)) }
    [IO.File]::Delete($pending)
}
function Install-LanShim([string]$GameRoot,[string]$Payload,[string]$ExpectedHash,[scriptblock]$AfterJournal) {
    $folder=Join-Path $GameRoot 'DiRT2VR/lan-backups'
    $pending=Join-Path $folder 'pending.json'
    $target=Join-Path $GameRoot 'xlive.dll'
    $backup=Join-Path $folder 'xlive.original.dll'
    Assert-LanPath $Payload; Assert-LanPath $pending; Assert-LanPath $target; Assert-LanPath $backup
    if (Test-Path -LiteralPath $pending) { throw 'LAN recovery is pending.' }
    if ($ExpectedHash -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-LanHash $Payload) -ne $ExpectedHash) { throw 'LAN payload hash mismatch.' }
    $original=Get-LanHash $target
    if ($original -eq $ExpectedHash) { throw 'LAN DLL is already installed without a journal. Original cannot be determined.' }
    if (Test-Path -LiteralPath $backup) {
        if ((Get-LanHash $backup) -ne $original) { throw 'An older original backup exists. It will not be overwritten.' }
    } elseif ($original -ne '') { Write-LanAtomic $backup ([IO.File]::ReadAllBytes($target)) }
    $journal=@{Version=1;OriginalHash=$original;AppliedHash=$ExpectedHash}
    Write-LanAtomic $pending ([Text.Encoding]::UTF8.GetBytes(($journal | ConvertTo-Json)))
    if ($AfterJournal) { & $AfterJournal }
    if ((Get-LanHash $target) -ne $original) { throw 'xlive.dll changed during preparation.' }
    Write-LanAtomic $target ([IO.File]::ReadAllBytes($Payload))
}
