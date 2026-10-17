param([string]$Baseline = '6bf579df1876c00cd47c0086bef93ec6dad5d1d9')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $root
$scenePath = 'Assets/Game/Scenes/Prototype_LumberCamp.unity'
function Read-Records([string]$source) {
    $records = @{}
    $headers = [regex]::Matches($source, '(?m)^--- !u!(\d+) &(-?\d+)[^\r\n]*')
    for ($i=0; $i -lt $headers.Count; $i++) {
        $start = $headers[$i].Index
        $end = if ($i + 1 -lt $headers.Count) { $headers[$i+1].Index } else { $source.Length }
        $body = $source.Substring($start, $end-$start).Replace("`r", '')
        $body = [regex]::Replace($body, '(?m)[ \t]+$', '').Trim()
        $owner = [regex]::Match($body, 'm_GameObject: \{fileID: (-?\d+)\}').Groups[1].Value
        $name = [regex]::Match($body, '(?m)^  m_Name: (.+)$').Groups[1].Value
        $records[$headers[$i].Groups[2].Value] = @{ type=$headers[$i].Groups[1].Value; body=$body; owner=$owner; name=$name }
    }
    return $records
}
$baseline = (git show "${Baseline}:$scenePath") -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read scene audit baseline.' }
$before = Read-Records $baseline
$after = Read-Records ([IO.File]::ReadAllText((Join-Path $root $scenePath)))
$changed = @()
foreach ($id in $before.Keys) {
    if ($after.ContainsKey($id) -and $before[$id].body -ne $after[$id].body) {
        $record = $after[$id]
        $ownerName = if ($record.type -eq '1') { $record.name } elseif ($after.ContainsKey($record.owner)) { $after[$record.owner].name } else { '' }
        $changed += [pscustomobject]@{ id=$id; type=$record.type; owner=$ownerName }
    }
}
$uiOwners = @{}
foreach ($records in @($before, $after)) {
    foreach ($record in $records.Values) {
        if ($record.type -eq '224') { $uiOwners[$record.owner] = $true }
    }
}
foreach ($records in @($before, $after)) {
    foreach ($id in $records.Keys) {
        $other = if ($records -eq $before) { $after } else { $before }
        if ($other.ContainsKey($id) -and $other[$id].body -eq $records[$id].body) { continue }
        $record = $records[$id]
        $owner = if ($record.type -eq '1') { $id } else { $record.owner }
        if (-not $uiOwners.ContainsKey($owner)) { throw "Non-UI scene record changed: $id ($($record.type))" }
    }
}
$result = [ordered]@{
    baseline=$Baseline
    presentationOnly=$true
    changedExisting=$changed.Count
    added=@($after.Keys | Where-Object { -not $before.ContainsKey($_) }).Count
    removed=@($before.Keys | Where-Object { -not $after.ContainsKey($_) }).Count
    records=@($changed | Sort-Object owner,type)
}
$result | ConvertTo-Json -Depth 5 | Set-Content Documentation/UIAssetRebuild/scene-delta.json
$changed | Sort-Object owner,type | Format-Table -AutoSize
