param([string]$LubanPath, [switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools/luban/4.5.0'
if ($Install) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    $archive = Join-Path $cache 'Luban.7z'
    Invoke-WebRequest 'https://api.github.com/repos/focus-creative-games/luban/releases/assets/311462681' `
        -Headers @{ Accept='application/octet-stream' } -OutFile $archive -TimeoutSec 120
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne '7CB57624A029291371EC2D96B86B63CE587F837EE30E1427953BF9C4D8B9B7E1') {
        throw 'Luban archive checksum mismatch.'
    }
    & tar -xf $archive -C $cache
    if ($LASTEXITCODE -ne 0) { throw 'Luban extraction failed.' }
}
if (!$LubanPath) { $LubanPath = Join-Path $cache 'Luban/Luban.dll' }
if (!(Test-Path -LiteralPath $LubanPath)) { throw 'Run tools/Build-Tables.ps1 -Install or specify -LubanPath.' }
$version = & dotnet $LubanPath --version 2>&1
# Luban's CLI reports --version on stderr and returns 1.
if (($version -join ' ') -notmatch '^Luban 4\.5\.0(?:[+\s]|$)') { throw "Unexpected Luban version: $version" }
$client = Join-Path $root 'client/unity/InsectSpaceClient/Assets/InsectSpace'
foreach ($target in @('client','server')) {
    $code = if ($target -eq 'client') { "$client/HotUpdate/Config/Generated" } else { "$root/server/Generated/Config" }
    $data = if ($target -eq 'client') { "$client/Content/Data" } else { "$root/server/Generated/Data" }
    & dotnet $LubanPath -t $target -c cs-bin -d bin --conf "$root/design/luban/luban.conf" `
        -x "cs-bin.outputCodeDir=$code" -x "bin.outputDataDir=$data"
    if ($LASTEXITCODE -ne 0) { throw "Luban generation failed for $target." }
}
$clientData = Get-ChildItem "$client/Content/Data" -Filter '*.bytes'
foreach ($file in $clientData) {
    if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash "$root/server/Generated/Data/$($file.Name)").Hash) {
        throw "Client/server table mismatch: $($file.Name)"
    }
}
Write-Host "Luban client/server generation passed; $($clientData.Count) binary tables match."
