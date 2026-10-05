[CmdletBinding()]
param([string]$EnvironmentPath = '', [switch]$Build, [string]$Distro = 'Ubuntu-24.04')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$EnvironmentPath) { $EnvironmentPath = Join-Path $root '.artifacts/validation/backend/wsl.env' }
if (!(Test-Path -LiteralPath $EnvironmentPath)) { throw 'Run Initialize-WSLBackend.ps1 first.' }
$binary = Join-Path $root '.artifacts/backend-linux/InsectSpace.BackendHost'
if ($Build -or !(Test-Path -LiteralPath $binary)) {
    & dotnet publish (Join-Path $root 'server/InsectSpace.BackendHost/InsectSpace.BackendHost.csproj') -c Release -r linux-x64 --self-contained true -o (Split-Path $binary -Parent) --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Linux backend publish failed.' }
}
$linuxRoot = (& wsl.exe -d $Distro -- wslpath -a $root.Replace('\','/')).Trim()
$linuxEnvironment = (& wsl.exe -d $Distro -- wslpath -a ([IO.Path]::GetFullPath($EnvironmentPath).Replace('\','/'))).Trim()
& wsl.exe -d $Distro -- python3 "$linuxRoot/tools/backend/manage.py" start --root $linuxRoot --environment $linuxEnvironment
if ($LASTEXITCODE -ne 0) { throw 'Native WSL2 backend start failed.' }
