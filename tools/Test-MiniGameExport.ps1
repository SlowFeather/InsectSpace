param(
    [string]$ExportRoot,
    [ValidateSet('Raw','webgl')][string]$PlayerName = 'Raw'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$ExportRoot) {
    $ExportRoot = Join-Path $root '.artifacts/unity/ValidationClient/HybridCLRData/MiniGameValidation/Raw'
}
$ExportRoot = [IO.Path]::GetFullPath($ExportRoot)

function Require-File([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Missing or empty exported artifact: $Path"
    }
}

foreach ($name in @('index.html','boot.config',"Build/$PlayerName.loader.js","Build/$PlayerName.framework.js","Build/$PlayerName.data","Build/$PlayerName.wasm")) {
    Require-File (Join-Path $ExportRoot $name)
}
$wasm = Join-Path $ExportRoot "Build/$PlayerName.wasm"
$stream = [IO.File]::OpenRead($wasm)
try {
    $header = [byte[]]::new(8)
    if ($stream.Read($header, 0, 8) -ne 8 -or
        [Convert]::ToHexString($header) -ne '0061736D01000000') {
        throw 'Export does not contain an uncompressed WebAssembly v1 module.'
    }
} finally { $stream.Dispose() }

$yoo = Join-Path $ExportRoot 'StreamingAssets/yoo'
$catalogs = @{}
$payloadHashes = @{}
foreach ($name in @('Core','WorldCommon')) {
    $package = Join-Path $yoo $name
    Require-File (Join-Path $package "$name.version")
    $version = (Get-Content -LiteralPath (Join-Path $package "$name.version") -Raw).Trim()
    if ($version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw "Invalid $name version." }
    foreach ($file in @("${name}_$version.bytes","${name}_$version.hash",'BuiltinCatalog.bytes','BuiltinCatalog.json')) {
        Require-File (Join-Path $package $file)
    }
    $catalog = Get-Content -LiteralPath (Join-Path $package 'BuiltinCatalog.json') -Raw | ConvertFrom-Json
    if ($catalog.PackageName -cne $name -or $catalog.PackageVersion -cne $version -or @($catalog.Entries).Count -eq 0) {
        throw "Incorrect $name builtin catalog identity."
    }
    $seen = @{}
    foreach ($entry in $catalog.Entries) {
        $extension = if ($name -eq 'Core') { 'rawfile' } else { 'bundle' }
        if ($entry.FileName -cnotmatch "^[0-9a-f]{32}\.$extension$" -or $seen.ContainsKey($entry.FileName)) {
            throw "Invalid or duplicate $name catalog payload."
        }
        $seen[$entry.FileName] = $true
        $file = Join-Path $package $entry.FileName
        Require-File $file
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ($name -eq 'Core') { $payloadHashes[$hash] = $file }
    }
    $catalogs[$name] = $catalog
}

# Find the code descriptor inside the exported Core package, not the mutable staging folder.
$manifests = @(
    foreach ($file in $payloadHashes.Values) {
        if ((Get-Item -LiteralPath $file).Length -gt 65536) { continue }
        $text = Get-Content -LiteralPath $file -Raw
        if (!$text.TrimStart().StartsWith('{')) { continue }
        try { $candidate = $text | ConvertFrom-Json } catch { continue }
        if ($candidate.PSObject.Properties.Name -contains 'hotUpdate') { $candidate }
    }
)
if ($manifests.Count -ne 1) { throw 'Expected exactly one code manifest inside exported Core.' }
$manifest = $manifests[0]
if ($manifest.target -cne 'MiniGamePlayer' -or $manifest.contractVersion -ne 1 -or
    [string]::IsNullOrWhiteSpace($manifest.playerBuildId) -or @($manifest.hotUpdate).Count -ne 1 -or
    $manifest.hotUpdate[0].assemblyName -cne 'InsectSpace.Gameplay.HotUpdate' -or @($manifest.aot).Count -lt 1) {
    throw 'Invalid or wrong-platform MiniGame code descriptor.'
}
$assemblies = @{}
foreach ($payload in @($manifest.aot) + @($manifest.hotUpdate)) {
    if ($payload.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or !$payloadHashes.ContainsKey($payload.sha256) -or
        [string]::IsNullOrWhiteSpace($payload.assemblyName) -or $assemblies.ContainsKey($payload.assemblyName)) {
        throw 'Missing, corrupt or duplicate exported code payload.'
    }
    $assemblies[$payload.assemblyName] = $true
}
if (@($manifest.contentPackages).Count -ne 1 -or $manifest.contentPackages[0].name -cne 'WorldCommon' -or
    $manifest.contentPackages[0].version -cne $catalogs.WorldCommon.PackageVersion) {
    throw 'Exported content version does not match the code manifest.'
}

$result = @{
    passed = $true
    scope = 'MiniGame Player export integrity only; not WeChat conversion or execution'
    exportRoot = $ExportRoot
    target = $manifest.target
    playerBuildId = $manifest.playerBuildId
    aotAssemblies = @($manifest.aot).Count
    hotUpdateAssemblies = @($manifest.hotUpdate).Count
    coreVersion = $catalogs.Core.PackageVersion
    corePayloads = @($catalogs.Core.Entries).Count
    contentBundles = @($catalogs.WorldCommon.Entries).Count
    wasmSha256 = (Get-FileHash -LiteralPath $wasm -Algorithm SHA256).Hash
}
$output = Join-Path $root '.artifacts/validation'
New-Item -ItemType Directory -Force $output | Out-Null
$resultName = if ($PlayerName -eq 'Raw') { 'minigame-export-result.json' } else { 'wechat-player-export-result.json' }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output $resultName) -Encoding utf8NoBOM
Write-Host "MINIGAME_EXPORT_VERIFIED aot=$($result.aotAssemblies) core=$($result.corePayloads) bundles=$($result.contentBundles)"
