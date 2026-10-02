param([switch]$Build, [int]$Port = 8086, [int]$TimeoutSeconds = 1800)
$ErrorActionPreference = 'Stop'
if ($Build) { & "$PSScriptRoot/Invoke-Unity.ps1" -Engine Unity -Action Web -TimeoutSeconds $TimeoutSeconds }
$node = Get-Command node -ErrorAction Stop
& $node.Source "$PSScriptRoot/Serve-Web.mjs" $Port
if ($LASTEXITCODE -ne 0) { throw 'Local Web development server stopped with an error.' }
