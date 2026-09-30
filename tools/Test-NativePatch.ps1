param([string]$Project, [string]$ExpectedMotherHash)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Project) { $Project = Join-Path $root '.artifacts/unity/ValidationClient' }
$expected = [IO.Path]::GetFullPath((Join-Path $root '.artifacts/unity/ValidationClient'))
if ([IO.Path]::GetFullPath($Project) -ne $expected) { throw 'Native patch tests must use the isolated project.' }
$build = Get-Content -Raw (Join-Path $Project 'HybridCLRData/native-patch-output.json') | ConvertFrom-Json
$package = [IO.Path]::GetFullPath($build.packageDirectory)
$allowedPackages = [IO.Path]::GetFullPath((Join-Path $root '.artifacts/yoo')).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if (!$package.StartsWith($allowedPackages, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to serve a package outside the validation artifact directory.'
}
$output = Join-Path $root '.artifacts/validation'
$resultPath = Join-Path $output 'native-patch-result.json'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$playerRoot = Join-Path $Project 'HybridCLRData/ValidationPlayer'
$nativeLibrary = Join-Path $playerRoot 'GameAssembly.dll'
$before = (Get-FileHash -LiteralPath $nativeLibrary -Algorithm SHA256).Hash
if ($ExpectedMotherHash -and $before -ne $ExpectedMotherHash) {
    throw 'The mother package changed while compiling a gameplay-only patch.'
}
$player = Join-Path $playerRoot 'InsectSpace.Validation.exe'
$packageRoots = @{ Core = $package }
$builtinRoot = Join-Path $playerRoot 'InsectSpace.Validation_Data/StreamingAssets/yoo'
foreach ($directory in Get-ChildItem -LiteralPath $builtinRoot -Directory) {
    if ($directory.Name -ne 'Core') { $packageRoots[$directory.Name] = $directory.FullName }
}
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = $probe.LocalEndpoint.Port
$probe.Stop()
$listener = [Net.HttpListener]::new()
$endpoint = "http://127.0.0.1:$port/"
$listener.Prefixes.Add($endpoint)
$requests = [Collections.Generic.List[string]]::new()
$playerLog = Join-Path $output 'native-patch-player.log'
$run = $null
try {
    $listener.Start()
    $pending = $listener.GetContextAsync()
    $run = Start-Process -FilePath $player -ArgumentList @('-batchmode','-nographics',
        '-insectspace-validate','-insectspace-remote',$endpoint,'-logFile',"`"$playerLog`"") -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while (!$run.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        if (!$pending.Wait(50)) { continue }
        $context = $pending.GetAwaiter().GetResult()
        $pending = $listener.GetContextAsync()
        try {
            $requestPath = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath)
            $requests.Add($requestPath)
            # YooAsset requests flat hash-named files inside the package directory.
            if ($context.Request.HttpMethod -notin @('GET','HEAD') -or $requestPath -notmatch '^/([A-Za-z0-9_-]+)/([^/\\]+)$') {
                $context.Response.StatusCode = 404
            } else {
                $name = $Matches[1]
                $fileName = $Matches[2]
                if (!$packageRoots.ContainsKey($name)) {
                    $context.Response.StatusCode = 404
                    continue
                }
                $packageRoot = $packageRoots[$name]
                $file = [IO.Path]::GetFullPath((Join-Path $packageRoot $fileName))
                if ([IO.Path]::GetDirectoryName($file) -ne $packageRoot.TrimEnd('\','/') -or !(Test-Path -LiteralPath $file -PathType Leaf)) {
                    $context.Response.StatusCode = 404
                } else {
                    $stream = [IO.File]::OpenRead($file)
                    try {
                        $context.Response.ContentType = 'application/octet-stream'
                        $context.Response.ContentLength64 = $stream.Length
                        if ($context.Request.HttpMethod -eq 'GET') { $stream.CopyTo($context.Response.OutputStream) }
                    } finally { $stream.Dispose() }
                }
            }
        } finally { $context.Response.Close() }
    }
    if (!$run.HasExited) { throw "Native patch Player timed out. See $playerLog" }
    $run.WaitForExit()
    if ($run.ExitCode -ne 0 -or
        !(Select-String -LiteralPath $playerLog -Pattern 'GAMEPLAY_REVISION native-patch-002' -Quiet) -or
        !(Select-String -LiteralPath $playerLog -Pattern 'NATIVE_VALIDATION_PASSED' -Quiet) -or
        !(Select-String -LiteralPath $playerLog -SimpleMatch $build.version -Quiet) -or
        (Select-String -LiteralPath $playerLog -Pattern 'BOOT_FAILED|NATIVE_VALIDATION_FAILED|Exception:' -Quiet)) {
        throw "Native patch validation failed. See $playerLog"
    }
    $after = (Get-FileHash -LiteralPath $nativeLibrary -Algorithm SHA256).Hash
    if ($before -ne $after) { throw 'Native mother package was modified during patch validation.' }
    if (!$requests.Contains('/Core/Core.version')) { throw 'The Player did not request the remote Core version.' }
    @{
        passed = $true
        version = $build.version
        nativeLibrarySha256 = $after
        nativeLibraryUnchanged = $true
        coreVersionRequested = $true
        coreManifestFetched = $requests.Contains("/Core/Core_$($build.version).bytes")
        requests = $requests
    } | ConvertTo-Json -Depth 5 | Set-Content $resultPath -Encoding utf8NoBOM
    Write-Host "NATIVE_PATCH_PASSED version=$($build.version) requests=$($requests.Count) motherPackageUnchanged=true"
} finally {
    $listener.Close()
    if ($run -and !$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue }
}
