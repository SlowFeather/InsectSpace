param([string]$ReferenceRoot = 'D:\Project\Unity_Project\GameFramework')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $ReferenceRoot 'Project/Build/LocalPackages'
$target = Join-Path $root 'vendor/upm'
New-Item -ItemType Directory -Force $target | Out-Null
$names = @(
    'com.framework.core-1.0.0-alpha.7.tgz',
    'com.framework.deterministic-1.0.0-alpha.7.tgz',
    'com.framework.deterministic.navigation-1.0.0-alpha.7.tgz',
    'com.framework.deterministic.physics-1.0.0-alpha.7.tgz',
    'com.framework.network.kcp-1.0.0-alpha.7.tgz',
    'com.framework.unity-1.0.0-alpha.7.tgz',
    'com.framework.zstring-2.5.0-framework.1.tgz',
    'com.code-philosophy.hybridclr-8.5.0.tgz',
    'com.tuyoogame.yooasset-3.0.5.tgz'
)
foreach ($name in $names) {
    $from = Join-Path $source $name
    $to = Join-Path $target $name
    if (Test-Path $to) {
        if ((Get-FileHash $from).Hash -ne (Get-FileHash $to).Hash) {
            throw "Refusing to overwrite a different vendored SDK: $name"
        }
    } else { Copy-Item -LiteralPath $from -Destination $to }
}
$dotnet = Join-Path $root 'vendor/dotnet'
New-Item -ItemType Directory -Force $dotnet | Out-Null
foreach ($package in @('com.framework.core','com.framework.deterministic','com.framework.network.kcp')) {
    $extracted = Join-Path $root ".tools/sdk-extract/$package"
    New-Item -ItemType Directory -Force $extracted | Out-Null
    & tar -xf (Join-Path $target "$package-1.0.0-alpha.7.tgz") -C $extracted
    if ($LASTEXITCODE -ne 0) { throw "Cannot extract the pinned SDK: $package" }
    Get-ChildItem $extracted -Recurse -Filter '*.dll' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dotnet }
}
$runtime = Join-Path $root 'client/unity/InsectSpaceClient/Assets/InsectSpace/LubanRuntime'
New-Item -ItemType Directory -Force $runtime | Out-Null
foreach ($name in @('ByteBuf.cs','BeanBase.cs','ITypeId.cs','LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $ReferenceRoot "Project/Client/FrameworkTest/Assets/Game/Runtime/Luban/Runtime/$name") -Destination $runtime
}
$tool = Join-Path $ReferenceRoot 'Project/.artifacts/luban/v4.5.0/Luban'
if (Test-Path $tool) {
    $destination = Join-Path $root '.tools/luban/4.5.0/Luban'
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -Path "$tool/*" -Destination $destination -Recurse -Force
}
$entries = @(Get-ChildItem (Join-Path $root 'vendor') -Recurse -File |
    Where-Object { $_.Extension -in '.tgz','.dll' } |
    Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
[ordered]@{ schemaVersion=1; source='GameFramework SDK local release'; files=$entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'vendor/dependencies.lock.json') -Encoding utf8NoBOM
Write-Host 'Imported version-pinned SDKs. Reference repository was not modified.'
& (Join-Path $PSScriptRoot 'Repair-SdkPackaging.ps1')
