param([ValidateRange(1024,65535)][int]$Port = 18089, [string]$Distro = 'Ubuntu-24.04')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root '.artifacts/unity/ValidationClient'
$build = Get-Content -Raw (Join-Path $project 'HybridCLRData/native-patch-output.json') | ConvertFrom-Json
$playerRoot = Join-Path $project 'HybridCLRData/ValidationPlayer'
$builtin = Join-Path $playerRoot 'InsectSpace.Validation_Data/StreamingAssets/yoo'
$backup = $builtin + '.wsl-validation-backup'
$cache = Join-Path $playerRoot 'InsectSpace.Validation_Data/yoo'
$cacheBackup = $cache + '.wsl-validation-backup'
$world = Join-Path $builtin 'WorldCommon'
$output = Join-Path $root '.artifacts/validation/backend-resources'
$log = Join-Path $output 'native-wsl-player.log'
$resultPath = Join-Path $output 'native-wsl-result.json'
if (Test-Path -LiteralPath $backup) { throw 'A previous builtin backup needs recovery before this test.' }
if (Test-Path -LiteralPath $cacheBackup) { throw 'A previous cache backup needs recovery before this test.' }
$nativeLibrary = Join-Path $playerRoot 'GameAssembly.dll'
$before = (Get-FileHash -LiteralPath $nativeLibrary -Algorithm SHA256).Hash
$package = [IO.Path]::GetFullPath($build.packageDirectory)
$allowed = [IO.Path]::GetFullPath((Join-Path $root '.artifacts/yoo')) + [IO.Path]::DirectorySeparatorChar
if (!$package.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Patch must be a validation release.' }
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$hostStarted = $false
$moved = $false
$cacheMoved = $false
$cachePrepared = $false
$run = $null
try {
    & "$PSScriptRoot/Manage-WSLResources.ps1" -Action Start -Port $Port -Distro $Distro -SourceRoot (Split-Path $package -Parent) -CoreDirectory $package -WorldDirectory $world
    $hostStarted = $true
    if (Test-Path -LiteralPath $cache) { Move-Item -LiteralPath $cache -Destination $cacheBackup; $cacheMoved = $true }
    $cachePrepared = $true
    # Keep the original mother package recoverable; no builtin resource can satisfy this run.
    Move-Item -LiteralPath $builtin -Destination $backup
    $moved = $true
    # YooAsset 3.0.6 initializes its builtin file system from a catalog even when
    # no files are bundled. Preserve the SDK header and emit zero catalog entries.
    foreach ($name in @('Core','WorldCommon')) {
        $original = [IO.File]::ReadAllBytes((Join-Path $backup "$name/BuiltinCatalog.bytes"))
        if ([BitConverter]::ToUInt32($original,0) -ne 0x133C5EE -or [BitConverter]::ToInt32($original,4) -ne 1) { throw 'Unsupported YooAsset catalog schema.' }
        $offset = 8
        for ($i=0; $i -lt 2; $i++) { $length = [BitConverter]::ToUInt16($original,$offset); $offset += 2 + $length }
        if ($offset + 4 -gt $original.Length) { throw 'Invalid builtin catalog header.' }
        $empty = [byte[]]::new($offset + 4)
        [Array]::Copy($original,$empty,$offset)
        $directory = Join-Path $builtin $name
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $directory 'BuiltinCatalog.bytes'),$empty)
    }
    $run = Start-Process -FilePath (Join-Path $playerRoot 'InsectSpace.Validation.exe') -ArgumentList @(
        '-batchmode','-nographics','-insectspace-validate','-insectspace-remote',"http://127.0.0.1:$Port/",
        '-logFile',('"{0}"' -f $log)) -PassThru -WindowStyle Hidden
    if (!$run.WaitForExit(180000)) { throw 'WSL native patch Player timed out.' }
    $text = Get-Content -Raw -LiteralPath $log
    foreach ($marker in @('NATIVE_CODE_LOADED','NATIVE_SESSION_CACHE_PASSED','GAMEPLAY_REVISION native-patch-002','CONTENT_LIFECYCLE_PASSED','NATIVE_VALIDATION_PASSED',$build.version)) {
        if (!$text.Contains($marker)) { throw "Missing native WSL marker: $marker" }
    }
    if ($run.ExitCode -ne 0 -or $text -match 'BOOT_FAILED|NATIVE_VALIDATION_FAILED|Exception:') { throw 'Native WSL Player failed; inspect its log.' }
    $after = (Get-FileHash -LiteralPath $nativeLibrary -Algorithm SHA256).Hash
    if ($before -ne $after) { throw 'The native mother package changed.' }
    $requestPath = if ($Port -eq 18088) { Join-Path $output 'requests.jsonl' } else { Join-Path $output "port-$Port/requests.jsonl" }
    $requests = @(Get-Content -LiteralPath $requestPath | ForEach-Object { $_ | ConvertFrom-Json })
    $paths = @($requests.path)
    if ('/Core/Core.version' -notin $paths -or "/Core/Core_$($build.version).bytes" -notin $paths) { throw 'Remote version or manifest was not fetched.' }
    $report = Get-Content -Raw (Join-Path $package ("Core_" + $build.version + '.report')) | ConvertFrom-Json
    $codeFile = @($report.BundleInfos | Where-Object { @($_.BundleContents.AssetPath) -contains 'Assets/InsectSpace/Content/Code/InsectSpace.Gameplay.HotUpdate.dll.bytes' })
    if ($codeFile.Count -ne 1 -or ('/Core/' + $codeFile[0].FileName) -notin $paths) { throw 'The new gameplay DLL was not downloaded from WSL.' }
    $bundles = @($paths | Where-Object { $_ -match '^/WorldCommon/.*\.bundle$' } | Select-Object -Unique)
    if ($bundles.Count -lt 2) { throw 'World prefab and scene were not downloaded from WSL; cache must be cleared before this test.' }
    [ordered]@{passed=$true; executedAt=[DateTimeOffset]::Now.ToString('o'); host='WSL2 native Linux'; version=$build.version; nativeLibrarySha256=$after; nativeLibraryUnchanged=$true; builtinResourcesDisabled=$true; codeDownloaded=$true; worldBundlesDownloaded=$bundles.Count; requests=$requests} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
    Write-Host "WSL_NATIVE_PATCH_PASS requests=$($requests.Count) worldBundles=$($bundles.Count) motherPackageUnchanged=true"
} finally {
    if ($run -and !$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue; $run.WaitForExit(10000) | Out-Null }
    if ($moved) {
        $expectedBuiltin = [IO.Path]::GetFullPath((Join-Path $root '.artifacts/unity/ValidationClient/HybridCLRData/ValidationPlayer/InsectSpace.Validation_Data/StreamingAssets/yoo'))
        if ([IO.Path]::GetFullPath($builtin) -ne $expectedBuiltin) { throw 'Unsafe validation builtin path.' }
        if (Test-Path -LiteralPath $builtin) { Remove-Item -LiteralPath $builtin -Recurse -Force }
        Move-Item -LiteralPath $backup -Destination $builtin
    }
    $expectedCache = [IO.Path]::GetFullPath((Join-Path $root '.artifacts/unity/ValidationClient/HybridCLRData/ValidationPlayer/InsectSpace.Validation_Data/yoo'))
    if ([IO.Path]::GetFullPath($cache) -ne $expectedCache) { throw 'Unsafe validation cache path.' }
    if ($cachePrepared -and (Test-Path -LiteralPath $cache)) { Remove-Item -LiteralPath $cache -Recurse -Force }
    if ($cacheMoved) { Move-Item -LiteralPath $cacheBackup -Destination $cache }
    if ($hostStarted) { & "$PSScriptRoot/Manage-WSLResources.ps1" -Action Stop -Port $Port -Distro $Distro }
}
