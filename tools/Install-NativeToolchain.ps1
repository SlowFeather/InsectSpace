param([string]$ArchiveDirectory = $env:INSECTSPACE_NATIVE_ARCHIVES)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root '.artifacts/toolchains'
$sources = @(
    @{
        Name = 'hybridclr-8.5.0'
        Url = 'https://github.com/focus-creative-games/hybridclr.git'
        Tag = 'v8.5.0'
        Commit = 'f67c0de1b5f1a8cc844807fab4c92a03b3a3cb63'
        Archive = 'hybridclr-f67c0de1.zip'
        ArchiveRoot = 'hybridclr-f67c0de1b5f1a8cc844807fab4c92a03b3a3cb63'
        ArchiveSha256 = 'FEE9ED20145F1E7FE5D356BDFFFD519A416B21E883A1223C2B05840524E75788'
    },
    @{
        Name = 'il2cpp-plus-2022-tuanjie-8.3.0'
        Url = 'https://github.com/focus-creative-games/il2cpp_plus.git'
        Tag = 'v2022-tuanjie-8.3.0'
        Commit = 'ea1bec1fbd5e7585e59fc98486821cd0d398751c'
        Archive = 'il2cpp-plus-ea1bec1f.zip'
        ArchiveRoot = 'il2cpp_plus-ea1bec1fbd5e7585e59fc98486821cd0d398751c'
        ArchiveSha256 = '09B628E0B3A2D20E31EA018FA3910C70CAA3424CA0E6DD2C56AD79B7D5D7FE89'
    }
)
New-Item -ItemType Directory -Force $directory | Out-Null
foreach ($source in $sources) {
    if ($ArchiveDirectory) {
        # Explicit offline transport for the same locked commits, not a version fallback.
        $archive = Join-Path $ArchiveDirectory $source.Archive
        if (!(Test-Path -LiteralPath $archive -PathType Leaf) -or
            (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $source.ArchiveSha256) {
            throw "Pinned native archive missing or checksum mismatch: $($source.Archive)"
        }
        $extract = Join-Path $directory ('archive-' + [Guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $archive -DestinationPath $extract
        $source.Path = Join-Path $extract $source.ArchiveRoot
        if (!(Test-Path -LiteralPath $source.Path -PathType Container)) { throw 'Pinned archive root mismatch.' }
        Write-Host "Verified pinned archive: $($source.Name) commit=$($source.Commit)"
        continue
    }
    $path = Join-Path $directory $source.Name
    if (!(Test-Path -LiteralPath $path)) {
        & git clone --depth 1 --branch $source.Tag $source.Url $path
        if ($LASTEXITCODE -ne 0) { throw "Cannot fetch $($source.Name)." }
    }
    $commit = & git -C $path rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $commit -ne $source.Commit) {
        throw "Native source revision mismatch: $path"
    }
    $changes = & git -C $path status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw "Native sources must be pristine: $path" }
    $source.Path = $path
}
# Assemble a disposable overlay; keep both pinned source repositories unchanged.
$overlay = Join-Path $directory 'tuanjie-8.5.0/libil2cpp'
New-Item -ItemType Directory -Force $overlay | Out-Null
Copy-Item -Path (Join-Path $sources[1].Path 'libil2cpp/*') -Destination $overlay -Recurse -Force
Copy-Item -LiteralPath (Join-Path $sources[0].Path 'hybridclr') -Destination $overlay -Recurse -Force
Write-Host "Verified native overlay: $overlay"
Write-Host 'No global editor files were changed. Invoke-Unity -Action Native installs into the isolated project only.'
