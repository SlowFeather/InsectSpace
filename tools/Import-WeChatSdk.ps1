param(
    [string]$Baseline = '.artifacts/toolchains/wechat-tuanjie-0.1.32-source/wechat-miniprogram-minigame-tuanjie-transform-sdk-f67e785',
    [string]$ArchivePath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$commit = 'a09d4b29daa1dd8358b09b5b5639554ab08cfdc2'
$repository = 'wechat-miniprogram/minigame-tuanjie-transform-sdk'
$stage = Join-Path $root '.artifacts/toolchains/wechat-0.1.34'
$package = Join-Path $stage 'package'
$output = Join-Path $root 'vendor/upm/com.qq.weixin.minigame-0.1.34-a09d4b2.tgz'
if (![IO.Path]::IsPathRooted($Baseline)) { $Baseline = Join-Path $root $Baseline }
New-Item -ItemType Directory -Force $package | Out-Null

function Get-GitBlobHash([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
    try {
        $hash.AppendData([Text.Encoding]::UTF8.GetBytes("blob $($bytes.Length)`0"))
        $hash.AppendData($bytes)
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally { $hash.Dispose() }
}

$treeFile = Join-Path $stage 'upstream-tree.json'
curl.exe -L --fail --silent --show-error --connect-timeout 20 --max-time 90 `
    -o $treeFile "https://api.github.com/repos/$repository/git/trees/$($commit)?recursive=1"
if ($LASTEXITCODE -ne 0) { throw 'Could not retrieve the pinned upstream tree.' }
$tree = Get-Content -LiteralPath $treeFile -Raw | ConvertFrom-Json
if ($tree.truncated -or !$tree.tree) { throw 'Incomplete upstream tree.' }
$files = @($tree.tree | Where-Object type -eq 'blob')
if ($ArchivePath) {
    $archive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $entries = @(tar -tzf $archive)
    if ($LASTEXITCODE -ne 0 -or $entries.Count -eq 0) { throw 'SDK archive is incomplete.' }
    $prefix = 'wechat-miniprogram-minigame-tuanjie-transform-sdk-a09d4b2'
    foreach ($entry in $entries) {
        if (!$entry.StartsWith($prefix + '/') -or $entry -match '(^|/)\.\.(/|$)|^[A-Za-z]:|\\') {
            throw "Unexpected SDK archive entry: $entry"
        }
    }
    $extract = Join-Path $stage ('archive-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $extract | Out-Null
    tar -xzf $archive -C $extract
    if ($LASTEXITCODE -ne 0) { throw 'SDK archive extraction failed.' }
    $Baseline = Join-Path $extract $prefix
}
$config = [Collections.Generic.List[string]]::new()
$reused = 0
foreach ($entry in $files) {
    if ($entry.mode -notin @('100644','100755') -or $entry.path -match '(^|/)\.\.(/|$)|^[A-Za-z]:|^/|\\') {
        throw "Unsupported upstream file: $($entry.path)"
    }
    $target = Join-Path $package $entry.path
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    if ((Get-GitBlobHash $target) -eq $entry.sha) { $reused++; continue }
    $cached = Join-Path $Baseline $entry.path
    if ((Get-GitBlobHash $cached) -eq $entry.sha) {
        Copy-Item -LiteralPath $cached -Destination $target -Force
        $reused++
        continue
    }
    $urlPath = ($entry.path.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
    $config.Add('url = "https://raw.githubusercontent.com/' + $repository + '/' + $commit + '/' + $urlPath + '"')
    $config.Add('output = "' + $target.Replace('\','/') + '"')
}
if ($config.Count -gt 0) {
    if ($ArchivePath) { throw 'The archive does not match the complete pinned Git tree.' }
    $jobs = Join-Path $stage 'download-jobs.txt'
    $config | Set-Content -LiteralPath $jobs -Encoding utf8NoBOM
    Write-Host "Downloading $($config.Count / 2) complete upstream files; $reused blobs reused after Git hash verification."
    curl.exe -L --parallel --parallel-immediate --parallel-max 8 --fail --silent --show-error `
        --retry 2 --retry-all-errors --connect-timeout 20 --max-time 180 --config $jobs
    if ($LASTEXITCODE -ne 0) { throw 'Some upstream downloads failed. Re-run to reuse only verified files.' }
}
foreach ($entry in $files) {
    if ((Get-GitBlobHash (Join-Path $package $entry.path)) -ne $entry.sha) {
        throw "Upstream Git blob verification failed: $($entry.path)"
    }
}
$actual = @(Get-ChildItem -LiteralPath $package -Recurse -File -Force)
if ($actual.Count -ne $files.Count) { throw 'The package contains files absent from the pinned upstream tree.' }
$manifest = Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json
if ($manifest.name -ne 'com.qq.weixin.minigame' -or $manifest.version -ne '0.1.1') {
    throw 'Unexpected upstream package metadata; review this baseline before importing.'
}
if (Test-Path -LiteralPath $output) { throw 'Never overwrite a versioned SDK archive. Review the existing artifact.' }
tar -czf $output -C $stage package
if ($LASTEXITCODE -ne 0) { throw 'UPM archive packaging failed.' }
$receipt = @{
    repository = $repository; commit = $commit; changelogVersion = '0.1.34'
    declaredPackageVersion = $manifest.version; files = $files.Count
    allGitBlobsVerified = $true
    sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'receipt.json') -Encoding utf8NoBOM
$receipt | ConvertTo-Json
