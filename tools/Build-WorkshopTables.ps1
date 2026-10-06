param([string]$LubanPath, [switch]$SchemaOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-WorkshopTables.ps1') -AllowUnconfigured:$SchemaOnly
if (!$LubanPath) { $LubanPath = Join-Path $root '.tools/luban/4.5.0/Luban/Luban.dll' }
if (!(Test-Path -LiteralPath $LubanPath)) { throw 'Install pinned Luban with tools/Build-Tables.ps1 -Install.' }
$version = & dotnet $LubanPath --version 2>&1
if (($version -join ' ') -notmatch '^Luban 4\.5\.0(?:[+\s]|$)') { throw "Unexpected Luban version: $version" }
$client = Join-Path $root 'client/unity/InsectSpaceClient/Assets/InsectSpace'
foreach ($target in @('client','server')) {
    $code = if ($target -eq 'client') { "$client/HotUpdate/Modules/Player/GuWorkshop/Generated" } else { "$root/server/Generated/GuWorkshop" }
    $data = if ($target -eq 'client') { "$client/Resources/LocalGuWorkshop" } else { "$root/server/Generated/Data/GuWorkshop" }
    & dotnet $LubanPath -t $target -c cs-bin -d bin --conf "$root/design/luban/GuWorkshop/luban.conf" -x "cs-bin.outputCodeDir=$code" -x "bin.outputDataDir=$data"
    if ($LASTEXITCODE -ne 0) { throw "Workshop generation failed: $target" }
}
foreach ($file in Get-ChildItem "$client/Resources/LocalGuWorkshop" -Filter '*.bytes') {
    if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash "$root/server/Generated/Data/GuWorkshop/$($file.Name)").Hash) { throw "Workshop table mismatch: $($file.Name)" }
}
Write-Host 'WORKSHOP_TABLES_PASSED: matching client/server Luban 4.5.0 tables.'
