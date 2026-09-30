$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vendor = Join-Path $root 'vendor/upm'
$records = @()
foreach ($name in @('com.framework.deterministic','com.framework.deterministic.navigation','com.framework.deterministic.physics')) {
    $original = Join-Path $vendor "$name-1.0.0-alpha.7.tgz"
    $version = '1.0.0-alpha.7.insectspace.1'
    $output = Join-Path $vendor "$name-$version.tgz"
    $stage = Join-Path $root ".tools/packaging/$name"
    New-Item -ItemType Directory -Force $stage | Out-Null
    & tar -xf $original -C $stage
    if ($LASTEXITCODE -ne 0) { throw "Cannot extract $name" }
    $package = Join-Path $stage 'package'
    $before = @{}
    Get-ChildItem $package -Recurse -Filter '*.dll' | ForEach-Object { $before[$_.Name] = (Get-FileHash $_.FullName).Hash }
    $manifestPath = Join-Path $package 'package.json'
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    $manifest.version = $version
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding utf8NoBOM
    foreach ($file in Get-ChildItem $package -Recurse -File | Where-Object Extension -ne '.meta') {
        if (Test-Path -LiteralPath ($file.FullName + '.meta')) { continue }
        $relative = [IO.Path]::GetRelativePath($package, $file.FullName).Replace('\','/')
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $guid = [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("$name/$relative"))).Substring(0,32).ToLowerInvariant() }
        finally { $sha.Dispose() }
        $importer = if ($file.Name -eq 'package.json') { 'PackageManifestImporter' } else { 'DefaultImporter' }
        @("fileFormatVersion: 2","guid: $guid","${importer}:","  externalObjects: {}","  userData:","  assetBundleName:","  assetBundleVariant:") |
            Set-Content -LiteralPath ($file.FullName + '.meta') -Encoding utf8NoBOM
    }
    foreach ($file in Get-ChildItem $package -Recurse -Filter '*.dll') {
        if ((Get-FileHash $file.FullName).Hash -ne $before[$file.Name]) { throw 'Metadata repair changed a protected binary.' }
    }
    if (!(Test-Path $output)) {
        & tar -czf $output -C $stage package
        if ($LASTEXITCODE -ne 0) { throw "Cannot package $name" }
    }
    $records += [ordered]@{
        package=$name; originalVersion='1.0.0-alpha.7'; derivedVersion=$version
        reason='Add missing UPM .meta files only; runtime binaries unchanged.'
        originalSha256=(Get-FileHash $original).Hash.ToLowerInvariant()
        derivedSha256=(Get-FileHash $output).Hash.ToLowerInvariant()
        binaryHashes=$before
    }
}
$records | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $root 'vendor/packaging-overrides.json') -Encoding utf8NoBOM
$entries = @(Get-ChildItem (Join-Path $root 'vendor') -Recurse -File |
    Where-Object { $_.Extension -in '.tgz','.dll' } | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
            sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()
        }
    })
[ordered]@{schemaVersion=1;source='GameFramework local release plus documented metadata-only derivatives';files=$entries} |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'vendor/dependencies.lock.json') -Encoding utf8NoBOM
Write-Host 'SDK packaging derivatives created; original archives and DLLs unchanged.'
