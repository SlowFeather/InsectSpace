$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$lock = Get-Content -Raw (Join-Path $root 'vendor/dependencies.lock.json') | ConvertFrom-Json
foreach ($file in $lock.files) {
    $path = Join-Path $root $file.path
    if (!(Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ine $file.sha256) {
        throw "Protected SDK checksum mismatch: $($file.path)"
    }
}
$assets = Join-Path $root 'client/unity/InsectSpaceClient/Assets/InsectSpace'
$definitions = @(Get-ChildItem $assets -Recurse -Filter '*.asmdef')
$definitions += @(Get-ChildItem (Join-Path $root 'shared') -Recurse -Filter '*.asmdef')
$byName = @{}
foreach ($file in $definitions) {
    $definition = Get-Content -Raw $file.FullName | ConvertFrom-Json
    if ($byName.ContainsKey($definition.name)) { throw "Duplicate assembly: $($definition.name)" }
    $byName[$definition.name] = $definition
    if ($definition.name -notmatch 'HotUpdate|Tests' -and $definition.references -contains 'InsectSpace.Gameplay.HotUpdate') {
        throw "AOT assembly illegally references hot update: $($definition.name)"
    }
}
$visited = @{}
function Visit-Assembly([string]$name) {
    if ($visited.ContainsKey($name)) {
        if ($visited[$name] -eq 'visiting') { throw "Assembly dependency cycle: $name" }
        return
    }
    $visited[$name] = 'visiting'
    foreach ($dependency in $byName[$name].references) {
        if ($dependency -like 'InsectSpace.*' -and !$byName.ContainsKey($dependency)) { throw "Missing assembly: $dependency" }
        if ($byName.ContainsKey($dependency)) { Visit-Assembly $dependency }
    }
    $visited[$name] = 'done'
}
foreach ($name in $byName.Keys) { Visit-Assembly $name }
foreach ($file in Get-ChildItem (Join-Path $root 'shared') -Recurse -Filter '*.cs') {
    if ($file.FullName -match '[\\/](obj|bin)[\\/]') { continue }
    $text = Get-Content -Raw $file.FullName
    if ($text -match '(?m)^\s*using\s+(UnityEngine|UnityEditor)') { throw "Unity dependency in shared source: $($file.FullName)" }
    if ($file.FullName -match 'com.insectspace.simulation' -and $text -match '\b(float|double)\b|DateTime\.|System\.Random|UnityEngine\.') {
        throw "Nondeterministic dependency in battle simulation: $($file.FullName)"
    }
}
$aotFiles = @(Get-ChildItem "$assets/Runtime" -Recurse -Filter '*.cs')
foreach ($file in $aotFiles) {
    if ((Get-Content -Raw $file.FullName) -match '(?m)^\s*using\s+InsectSpace\.(Config|Gameplay)') {
        throw "AOT source imports hot-update namespace: $($file.FullName)"
    }
}
$manifestFile = Join-Path $root 'client/unity/InsectSpaceClient/Packages/manifest.json'
$manifest = Get-Content -Raw $manifestFile | ConvertFrom-Json -AsHashtable
foreach ($name in $manifest.dependencies.Keys) {
    $version = $manifest.dependencies[$name]
    if ($name -like 'com.framework.*' -or $name -in @('com.tuyoogame.yooasset','com.code-philosophy.hybridclr','com.qq.weixin.minigame')) {
        if ($version -notlike 'file:*.tgz') { throw "SDK dependency is not version-pinned: $name" }
    }
    if ($version.StartsWith('file:')) {
        $path = [IO.Path]::GetFullPath((Join-Path (Split-Path $manifestFile -Parent) $version.Substring(5)))
        if (!(Test-Path -LiteralPath $path)) { throw "Unresolvable local package: $name" }
    }
}
Write-Host "ARCHITECTURE_PASSED: SDK hashes, $($byName.Count) assemblies, dependency direction, simulation purity, package paths."
