param(
    [ValidateSet('Tuanjie', 'Unity')][string]$Engine = 'Tuanjie',
    [switch]$Open,
    [string]$Editor
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Client-Environment.ps1"
$configuration = Get-ClientConfiguration
$profile = Get-ClientEngine $Engine
$project = Join-Path $ClientRepositoryRoot $profile.project
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (!(Test-Path -LiteralPath $versionFile) -or
    !(Select-String -LiteralPath $versionFile -Pattern "^m_EditorVersion: $([regex]::Escape($profile.version))$" -Quiet)) {
    throw "Wrong editor version in $versionFile. Do not upgrade one engine's project with the other editor."
}
if ($Engine -eq 'Tuanjie') {
    foreach ($relative in $configuration.sharedAssetDirectories) {
        $source = [IO.Path]::GetFullPath((Join-Path $ClientRepositoryRoot "$($configuration.sharedAssetsProject)/Assets/$relative"))
        $target = Join-Path $project "Assets/$relative"
        if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Shared source missing: $source" }
        $entry = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        if ($entry) {
            if (!($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                [IO.Path]::GetFullPath($entry.Target) -ine $source) {
                throw "Expected a link to $source at $target. Existing files were left untouched."
            }
        } else {
            Assert-ClientProjectClosed $project
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            $kind = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
            New-Item -ItemType $kind -Path $target -Target $source | Out-Null
        }
    }
}
Write-Host "CLIENT_READY engine=$Engine version=$($profile.version) project=$project"
if ($Open) {
    $binary = Resolve-ClientEditor $Engine $Editor
    if ($IsWindows) {
        Start-Process -FilePath $binary -ArgumentList @('-projectPath', ('"' + $project + '"')) -WindowStyle Hidden | Out-Null
    } else { Start-Process -FilePath $binary -ArgumentList @('-projectPath', $project) | Out-Null }
}
