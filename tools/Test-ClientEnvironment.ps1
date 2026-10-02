$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Client-Environment.ps1"
& "$PSScriptRoot/Initialize-Client.ps1" -Engine Tuanjie
& "$PSScriptRoot/Initialize-Client.ps1" -Engine Unity
$configuration = Get-ClientConfiguration
$unity = Get-ClientEngine Unity
$tuanjie = Get-ClientEngine Tuanjie
$unityManifest = Get-Content -LiteralPath (Join-Path $ClientRepositoryRoot "$($unity.project)/Packages/manifest.json") -Raw | ConvertFrom-Json -AsHashtable
$tuanjieManifest = Get-Content -LiteralPath (Join-Path $ClientRepositoryRoot "$($tuanjie.project)/Packages/manifest.json") -Raw | ConvertFrom-Json -AsHashtable
foreach ($key in $unityManifest.dependencies.Keys | Where-Object { $_ -like 'com.framework.*' -or $_ -like 'com.insectspace.*' }) {
    if ($key -in $configuration.engineSpecificPackages) { continue }
    if ($unityManifest.dependencies[$key] -cne $tuanjieManifest.dependencies[$key]) { throw "Shared dependency drift: $key" }
}
foreach ($engine in @('Unity', 'Tuanjie')) {
    $profile = Get-ClientEngine $engine
    $extension = if ($engine -eq 'Unity') { 'unity' } else { 'scene' }
    foreach ($scene in @('Assets/InsectSpace/Scenes/Bootstrap', 'Assets/InsectSpace/Content/WorldCommon/WorldSandbox')) {
        $path = Join-Path $ClientRepositoryRoot "$($profile.project)/$scene.$extension"
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or !(Test-Path -LiteralPath "$path.meta" -PathType Leaf)) {
            throw "Missing engine-specific scene or metadata: $path"
        }
    }
    foreach ($directory in @('ProjectSettings', 'Assets/Settings', 'Assets/InsectSpace/Scenes')) {
        $entry = Get-Item -LiteralPath (Join-Path $ClientRepositoryRoot "$($profile.project)/$directory")
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Engine-specific assets may not be linked: $directory" }
    }
}
$rejected = $false
try { & "$PSScriptRoot/Invoke-Unity.ps1" -Engine Unity -Action MiniGame }
catch {
    if ($_.Exception.Message -notmatch 'not supported on Unity') { throw }
    $rejected = $true
}
if (!$rejected) { throw 'Unity incorrectly accepted the Tuanjie MiniGame action.' }
Write-Host 'CLIENT_ENVIRONMENT_PASSED: shared links, SDK parity, independent engine assets, MiniGame target guard.'
