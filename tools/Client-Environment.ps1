$script:ClientRepositoryRoot = Split-Path $PSScriptRoot -Parent

function Get-ClientConfiguration {
    Get-Content -LiteralPath (Join-Path $script:ClientRepositoryRoot 'client/engine-projects.json') -Raw |
        ConvertFrom-Json -AsHashtable
}

function Get-ClientEngine([string]$Engine) {
    $configuration = Get-ClientConfiguration
    if (!$configuration.engines.ContainsKey($Engine)) { throw "Unknown client engine: $Engine" }
    $configuration.engines[$Engine]
}

function Assert-ClientProjectClosed([string]$Project) {
    foreach ($name in @('UnityLockfile', 'TuanjieLockfile')) {
        $lockPath = Join-Path $Project "Temp/$name"
        if (Test-Path -LiteralPath $lockPath) {
            try { $handle = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $handle.Dispose() }
            catch { throw "Project is open; refusing to replace its files: $Project" }
        }
    }
}

function Resolve-ClientEditor([string]$Engine, [string]$Editor) {
    $profile = Get-ClientEngine $Engine
    if (!$Editor) { $Editor = [Environment]::GetEnvironmentVariable($profile.editorEnvironment) }
    if (!$Editor) {
        $candidates = @()
        if ($Engine -eq 'Unity') {
            $cli = Get-Command unity -ErrorAction SilentlyContinue
            if ($cli) {
                $previousTelemetry = $env:UNITY_NO_CLI_INVOKED_TELEMETRY
                try {
                    $env:UNITY_NO_CLI_INVOKED_TELEMETRY = '1'
                    $report = & $cli.Source --no-banner --no-pager --non-interactive --no-log-proxy --json editors --installed |
                        ConvertFrom-Json
                    if ($LASTEXITCODE -eq 0 -and $report.success) {
                        $candidates += @($report.data | Where-Object version -EQ $profile.version | ForEach-Object location)
                    }
                } finally { $env:UNITY_NO_CLI_INVOKED_TELEMETRY = $previousTelemetry }
            }
        }
        if ($IsWindows) {
            # Known installation roots are fallbacks; -Editor and environment variables work on other machines.
            $candidates += "D:/APP/TuanjieEngine/$($profile.version)/Editor/$($profile.executable)"
            $candidates += "D:/APP/UnityHubEditor/$($profile.version)/Editor/$($profile.executable)"
            $candidates += "${env:ProgramFiles}/Unity/Hub/Editor/$($profile.version)/Editor/$($profile.executable)"
        }
        $Editor = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (!$Editor -or !(Test-Path -LiteralPath $Editor -PathType Leaf)) {
        throw "Install $Engine $($profile.version), set $($profile.editorEnvironment), or supply -Editor."
    }
    $resolved = (Resolve-Path -LiteralPath $Editor).Path
    if ([IO.Path]::GetFileName($resolved) -ine $profile.executable) {
        throw "Expected $($profile.executable) for $Engine, got $resolved"
    }
    $version = (Get-Item -LiteralPath $resolved).VersionInfo.ProductVersion
    if ($version -and !$version.StartsWith($profile.version, [StringComparison]::Ordinal)) {
        throw "Editor version mismatch: expected $($profile.version), got $version"
    }
    return $resolved
}

function Copy-ClientDirectory([string]$Source, [string]$Destination) {
    # Materialize shared directory links: validation must never retain links back into a live project.
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        $target = Join-Path $Destination $entry.Name
        if ($entry.PSIsContainer) { Copy-ClientDirectory $entry.FullName $target }
        else { Copy-Item -LiteralPath $entry.FullName -Destination $target -Force }
    }
}
