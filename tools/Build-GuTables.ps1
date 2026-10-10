param([string]$LubanPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$LubanPath) { $LubanPath = Join-Path $root '.tools/luban/4.5.0/Luban/Luban.dll' }
if (!(Test-Path -LiteralPath $LubanPath)) { throw 'Install the pinned tool using tools/Build-Tables.ps1 -Install first.' }
$version = & dotnet $LubanPath --version 2>&1
if (($version -join ' ') -notmatch '^Luban 4\.5\.0(?:[+\s]|$)') { throw "Unexpected Luban version: $version" }
$client = Join-Path $root 'client/unity/InsectSpaceClient/Assets/InsectSpace'
foreach ($target in @('client', 'server')) {
    $code = if ($target -eq 'client') { "$client/HotUpdate/Modules/Player/GuPaths/Generated" } else { "$root/server/Generated/GuPaths" }
    $data = if ($target -eq 'client') { "$client/Resources/LocalGuPaths" } else { "$root/server/Generated/Data/GuPaths" }
    & dotnet $LubanPath -t $target -c cs-bin -d bin --conf "$root/design/luban/GuPaths/luban.conf" -x "cs-bin.outputCodeDir=$code" -x "bin.outputDataDir=$data"
    if ($LASTEXITCODE -ne 0) { throw "Gu catalog generation failed: $target" }
}
foreach ($file in Get-ChildItem "$client/Resources/LocalGuPaths" -Filter '*.bytes') {
    if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash "$root/server/Generated/Data/GuPaths/$($file.Name)").Hash) { throw "Gu catalog mismatch: $($file.Name)" }
}
Write-Host 'GU_TABLES_PASSED: Luban 4.5.0 generated matching client/server catalogs.'
