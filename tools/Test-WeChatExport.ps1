param([string]$ExportRoot)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$ExportRoot) {
    $ExportRoot = Join-Path $root '.artifacts/unity/ValidationClient/HybridCLRData/MiniGameValidation/WeChat'
}
$ExportRoot = [IO.Path]::GetFullPath($ExportRoot)
& "$PSScriptRoot/Test-MiniGameExport.ps1" -ExportRoot (Join-Path $ExportRoot 'webgl') -PlayerName webgl
$mini = Join-Path $ExportRoot 'minigame'
foreach ($name in @('game.js','game.json','project.config.json','unity-namespace.js','webgl.wasm.framework.unityweb.js')) {
    $file = Join-Path $mini $name
    if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Missing converted WeChat artifact: $name"
    }
}
$receipt = Get-Content -LiteralPath (Join-Path $ExportRoot 'export-receipt.json') -Raw | ConvertFrom-Json
$project = Get-Content -LiteralPath (Join-Path $mini 'project.config.json') -Raw | ConvertFrom-Json
$game = Get-Content -LiteralPath (Join-Path $mini 'game.json') -Raw | ConvertFrom-Json
if ($receipt.sdkCommit -cne 'a09d4b29daa1dd8358b09b5b5639554ab08cfdc2' -or $receipt.engine -cne '2022.3.62t16') {
    throw 'The export does not match the selected SDK/editor baseline.'
}
if ($project.compileType -cne 'game' -or $project.appid -notmatch '^(|wx[0-9a-f]{16})$' -or
    $receipt.appIdConfigured -ne ![string]::IsNullOrEmpty($project.appid)) {
    throw 'Unexpected project identity. Do not substitute a demo AppID for an unconfigured project.'
}
foreach ($name in @('wasmcode','data-package')) {
    if (!(@($game.subpackages) | Where-Object { $_.name -eq $name -and $_.root.TrimEnd('/') -eq $name })) {
        throw "Missing expected WeChat subpackage: $name"
    }
}
$wasm = @(Get-ChildItem -LiteralPath (Join-Path $mini 'wasmcode') -File -Filter '*.wasm.br')
if ($wasm.Count -ne 1) { throw 'Expected one converted Brotli WASM payload.' }
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $ExportRoot 'webgl/Build/webgl.wasm') -Algorithm SHA256).Hash
$fileStream = [IO.File]::OpenRead($wasm[0].FullName)
$brotli = [IO.Compression.BrotliStream]::new($fileStream, [IO.Compression.CompressionMode]::Decompress)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $convertedHash = [Convert]::ToHexString($sha.ComputeHash($brotli))
    if ($convertedHash -ne $sourceHash) { throw 'Converted WASM does not match the native Player.' }
} finally { $sha.Dispose(); $brotli.Dispose(); $fileStream.Dispose() }
$data = @(Get-ChildItem -LiteralPath (Join-Path $mini 'data-package') -File |
    Where-Object { $_.Extension -in @('.br','.bin','.txt') -and $_.Length -gt 0 })
if ($data.Count -eq 0) { throw 'Missing WeChat first-data subpackage payload.' }
$result = @{
    passed = $true
    scope = 'Native export and official SDK conversion integrity; not SDK/device execution'
    sdkCommit = $receipt.sdkCommit
    appIdConfigured = $receipt.appIdConfigured
    resourceCdnConfigured = $receipt.resourceCdnConfigured
    wasmMatchesNativePlayer = $true
    wasmSha256 = $sourceHash
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root '.artifacts/validation/wechat-export-result.json') -Encoding utf8NoBOM
Write-Host "WECHAT_EXPORT_VERIFIED appIdConfigured=$($receipt.appIdConfigured) cdnConfigured=$($receipt.resourceCdnConfigured)"
