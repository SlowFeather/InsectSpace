param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root '.artifacts/toolchains'
$sources = @(
    @{
        Name = 'hybridclr-8.5.0'
        Url = 'https://github.com/focus-creative-games/hybridclr.git'
        Tag = 'v8.5.0'
        Commit = 'f67c0de1b5f1a8cc844807fab4c92a03b3a3cb63'
    },
    @{
        Name = 'il2cpp-plus-2022-tuanjie-8.3.0'
        Url = 'https://github.com/focus-creative-games/il2cpp_plus.git'
        Tag = 'v2022-tuanjie-8.3.0'
        Commit = 'ea1bec1fbd5e7585e59fc98486821cd0d398751c'
    }
)
New-Item -ItemType Directory -Force $directory | Out-Null
foreach ($source in $sources) {
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
}
# Assemble a disposable overlay; keep both pinned source repositories unchanged.
$overlay = Join-Path $directory 'tuanjie-8.5.0/libil2cpp'
New-Item -ItemType Directory -Force $overlay | Out-Null
Copy-Item -Path "$directory/il2cpp-plus-2022-tuanjie-8.3.0/libil2cpp/*" -Destination $overlay -Recurse -Force
Copy-Item -LiteralPath "$directory/hybridclr-8.5.0/hybridclr" -Destination $overlay -Recurse -Force
Write-Host "Verified native overlay: $overlay"
Write-Host 'No global editor files were changed. Invoke-Unity -Action Native installs into the isolated project only.'
