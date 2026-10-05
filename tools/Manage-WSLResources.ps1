param(
    [ValidateSet('Start','Stop','Test')][string]$Action = 'Start',
    [string]$SourceRoot = '',
    [string]$Version = 'foundation-001',
    [ValidateRange(1024,65535)][int]$Port = 18088,
    [string]$Distro = 'Ubuntu-24.04'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$SourceRoot) { $SourceRoot = Join-Path $root '.artifacts/yoo/WebGL' }
$linuxRoot = (& wsl.exe -d $Distro -- wslpath -a $root.Replace('\','/')).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve repository in WSL.' }
$arguments = @('-d',$Distro,'--','python3',"$linuxRoot/tools/backend/resources.py",$Action.ToLowerInvariant(),'--root',$linuxRoot,'--port',$Port,'--version',$Version)
if ($Action -eq 'Start') {
    $source = [IO.Path]::GetFullPath($SourceRoot).Replace('\','/')
    if (!(Test-Path -LiteralPath $source -PathType Container)) { throw 'An existing YooAsset release directory is required. Build it with the existing release tools first.' }
    $linuxSource = (& wsl.exe -d $Distro -- wslpath -a $source).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve published resource directory.' }
    $arguments += @('--source',$linuxSource)
}
& wsl.exe @arguments
if ($LASTEXITCODE -ne 0) { throw "WSL resource $Action failed." }
if ($Action -in 'Start','Test') {
    $health = Invoke-RestMethod "http://127.0.0.1:$Port/healthz" -TimeoutSec 5
    if ($health.role -ne 'yooasset-local-host' -or $health.operatingSystem -ne 'Linux') { throw 'Windows-to-WSL resource endpoint check failed.' }
    Write-Host "LOCAL WSL resource root: http://127.0.0.1:$Port (published bundles only; target compatibility is checked by the existing client pipeline)."
}
