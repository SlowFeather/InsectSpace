param([string]$LubanPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$LubanPath) { $LubanPath = Join-Path $root '.tools/luban/4.5.0/Luban/Luban.dll' }
if (!(Test-Path -LiteralPath $LubanPath)) { throw 'Install pinned Luban with tools/Build-Tables.ps1 -Install.' }
$version = & dotnet $LubanPath --version 2>&1
if (($version -join ' ') -notmatch '^Luban 4\.5\.0(?:[+\s]|$)') { throw "Unexpected Luban version: $version" }
$client = Join-Path $root 'client/unity/InsectSpaceClient/Assets/InsectSpace'
foreach ($target in @('client','server')) {
    $code = if ($target -eq 'client') { "$client/HotUpdate/Config/Generated" } else { "$root/server/Generated/Config" }
    $data = if ($target -eq 'client') { "$client/Content/Data" } else { "$root/server/Generated/Data" }
    & dotnet $LubanPath -t $target -c cs-bin -d bin --conf "$root/design/luban/luban.conf" -x "cs-bin.outputCodeDir=$code" -x "bin.outputDataDir=$data"
    if ($LASTEXITCODE -ne 0) { throw "Battle generation failed: $target" }
}
foreach ($file in Get-ChildItem "$client/Content/Data" -Filter '*.bytes') {
    $server = Join-Path $root "server/Generated/Data/$($file.Name)"
    if (!(Test-Path $server) -or (Get-FileHash $file.FullName).Hash -ne (Get-FileHash $server).Hash) { throw "Client/server table mismatch: $($file.Name)" }
}
Write-Host 'BATTLE_TABLES_PASSED: client/server Luban tables match.'
