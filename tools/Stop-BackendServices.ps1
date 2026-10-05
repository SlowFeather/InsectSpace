[CmdletBinding()]
param([string]$StatePath = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($StatePath)) { $StatePath = Join-Path $root '.artifacts/validation/backend/services.json' }
if (!(Test-Path -LiteralPath $StatePath)) { Write-Host 'No backend service state file found.'; exit 0 }
$entries = @(Get-Content -Raw -LiteralPath $StatePath | ConvertFrom-Json)
foreach ($entry in $entries) {
    $process = Get-Process -Id ([int]$entry.Pid) -ErrorAction SilentlyContinue
    if ($process) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
Remove-Item -LiteralPath $StatePath -Force -ErrorAction SilentlyContinue
Write-Host 'Backend services stopped.'
