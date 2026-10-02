param([string]$Report = '.artifacts/validation/portable-asset-guids.json')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Client-Environment.ps1"
$configuration = Get-ClientConfiguration
$reportPath = [IO.Path]::GetFullPath((Join-Path $ClientRepositoryRoot $Report))
$data = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$mapping = @{}
foreach ($entry in $data.entries) {
    if ($entry.storedGuid -match '^[0-9a-f]{32}$') { continue }
    if ($entry.guid -notmatch '^[0-9a-f]{32}$') { throw "Native editor did not return a portable GUID: $($entry.path)" }
    if ($mapping.ContainsKey($entry.storedGuid) -and $mapping[$entry.storedGuid] -cne $entry.guid) {
        throw 'One stored GUID maps to two different native identities.'
    }
    $mapping[$entry.storedGuid] = $entry.guid
}
$files = @()
foreach ($engine in $configuration.engines.Keys) {
    $assetPath = Join-Path $ClientRepositoryRoot "$($configuration.engines[$engine].project)/Assets"
    # Do not follow Tuanjie's shared links a second time.
    $files += @(Get-ChildItem -LiteralPath $assetPath -Recurse -File -Filter '*.meta')
}
$files += @(Get-ChildItem -LiteralPath (Join-Path $ClientRepositoryRoot 'shared') -Recurse -File -Filter '*.meta' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$edits = @()
foreach ($file in $files | Sort-Object FullName -Unique) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $match = [regex]::Match($text, '(?m)^guid:\s*(\S+)')
    if (!$match.Success -or $match.Groups[1].Value -match '^[0-9a-f]{32}$') { continue }
    $old = $match.Groups[1].Value
    if (!$mapping.ContainsKey($old)) { throw "Missing native GUID mapping; no files changed: $($file.FullName)" }
    $value = $mapping[$old]
    $edits += @{ Path = $file.FullName; Text = $text.Replace("guid: $old", "guid: $value") }
}
foreach ($edit in $edits) { [IO.File]::WriteAllText($edit.Path, $edit.Text, [Text.UTF8Encoding]::new($false)) }
Write-Host "PORTABLE_METADATA_CONVERTED files=$($edits.Count); native asset identities preserved."
