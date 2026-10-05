[CmdletBinding()]
param(
    [string]$EnvironmentPath = '',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($EnvironmentPath)) { $EnvironmentPath = Join-Path $root '.artifacts/validation/backend/wsl.env' }
if (!(Test-Path -LiteralPath $EnvironmentPath)) { throw "Backend environment not found: $EnvironmentPath. Run Initialize-WSLBackend.ps1 first." }

foreach ($line in Get-Content -LiteralPath $EnvironmentPath) {
    if ($line -match '^([A-Z0-9_]+)=(.*)$') {
        $name = $Matches[1]
        if ($name -eq 'INSECTSPACE_PHONE_WHITELIST' -and $env:INSECTSPACE_PHONE_WHITELIST) { continue }
        if ($name -eq 'INSECTSPACE_PHONE_AUTH_MODE' -and $env:INSECTSPACE_PHONE_AUTH_MODE) { continue }
        Set-Item -Path "Env:$name" -Value $Matches[2]
    }
}
$env:INSECTSPACE_SERVICE = 'gateway'
$hostProject = Join-Path $root 'server/InsectSpace.BackendHost/InsectSpace.BackendHost.csproj'
if ($Build) {
    & dotnet build $hostProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backend host build failed.' }
}

$logDirectory = Join-Path $root '.artifacts/validation/backend/services'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$serviceNames = @('gateway', 'identity', 'lobby', 'world', 'battle', 'worker')
$processes = foreach ($serviceName in $serviceNames) {
    $stdout = Join-Path $logDirectory "$serviceName.out.log"
    $stderr = Join-Path $logDirectory "$serviceName.err.log"
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', $hostProject, '--no-build', '--', '--service', $serviceName) -WorkingDirectory $root -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    [pscustomobject]@{ Role = $serviceName; Pid = $process.Id; Stdout = $stdout; Stderr = $stderr }
}
$processes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $root '.artifacts/validation/backend/services.json') -Encoding ascii
Write-Host "Started $($processes.Count) backend services. Health endpoints use ports 8080-8085."
