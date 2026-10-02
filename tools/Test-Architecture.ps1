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
. "$PSScriptRoot/Client-Environment.ps1"
$configuration = Get-ClientConfiguration
foreach ($engine in $configuration.engines.Keys) {
$profile = Get-ClientEngine $engine
$manifestFile = Join-Path $root "$($profile.project)/Packages/manifest.json"
$manifest = Get-Content -Raw $manifestFile | ConvertFrom-Json -AsHashtable
$versionFile = Join-Path $root "$($profile.project)/ProjectSettings/ProjectVersion.txt"
if (!(Select-String -LiteralPath $versionFile -Pattern "^m_EditorVersion: $([regex]::Escape($profile.version))$" -Quiet)) {
    throw "$engine project has the wrong editor version."
}
if ($engine -eq 'Tuanjie' -and $manifest.dependencies.ContainsKey('com.unity.pipeline')) {
    throw 'Unity Pipeline must not enter the Tuanjie 2022 project.'
}
if ($engine -eq 'Unity' -and @($manifest.dependencies.Keys | Where-Object { $_ -like 'cn.tuanjie.*' }).Count -gt 0) {
    throw 'Tuanjie assistant packages must not enter the Unity project.'
}
if ($engine -eq 'Unity' -and $manifest.dependencies.ContainsKey('com.unity.modules.infinity')) {
    throw 'Tuanjie Infinity module is not available in Unity 6.'
}
if ($engine -eq 'Unity' -and $manifest.dependencies.ContainsKey('com.qq.weixin.minigame')) {
    throw 'The Tuanjie WeChat conversion SDK must not be compiled into the Unity Web project.'
}
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
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'client/unity/InsectSpaceClient/Assets'), (Join-Path $root 'shared') -Recurse -File -Filter '*.meta') {
    if ($file.FullName -match '[\\/](bin|obj)[\\/]') { continue }
    if ((Get-Content -LiteralPath $file.FullName -Raw) -notmatch '(?m)^guid: [0-9a-f]{32}\s*$') {
        throw "Non-portable asset GUID: $($file.FullName). See docs/Dual-Engine.md."
    }
}
Write-Host "ARCHITECTURE_PASSED: SDK hashes, $($byName.Count) assemblies, dependency direction, simulation purity, both engine manifests and versions."
