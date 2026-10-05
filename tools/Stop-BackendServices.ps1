[CmdletBinding()]
param([string]$Distro = 'Ubuntu-24.04')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$linuxRoot = (& wsl.exe -d $Distro -- wslpath -a $root.Replace('\','/')).Trim()
& wsl.exe -d $Distro -- python3 "$linuxRoot/tools/backend/manage.py" stop --root $linuxRoot --environment "$linuxRoot/.artifacts/validation/backend/wsl.env"
if ($LASTEXITCODE -ne 0) { throw 'Native WSL2 backend stop failed.' }
