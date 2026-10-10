param([switch]$OpenOnly)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$created = Invoke-DemoCommand @('command','eval_file',(Join-Path $PSScriptRoot 'unity-demo/CreateWorldNavigationScene.cs'))
Write-Host $created.result
if (!$OpenOnly) { $null = Invoke-DemoCommand @('command','editor_play') }
Write-Host 'LOCAL WORLD NAVIGATION: 3D NavMesh travel, click-to-move and manual takeover. Standalone prototype; no world server or online AOI.'
