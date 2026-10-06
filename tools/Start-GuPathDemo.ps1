param([switch]$OpenOnly, [ValidateRange(1, 65535)][int]$Port = 7779)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$demoEvidence = Join-Path $demoRoot '.artifacts/validation/gu-paths'
Assert-DemoEditor
$created = Invoke-DemoCommand @('command', 'eval_file', (Join-Path $PSScriptRoot 'unity-demo/CreateGuPathScene.cs'))
Write-Host $created.result
$null = Invoke-DemoCommand @('command', 'eval', "var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.GuPaths.LocalGuPathPanel>(); p.Port = $Port; UnityEditor.SceneManagement.EditorSceneManager.SaveScene(p.gameObject.scene); return p.Port;")
if (!$OpenOnly) { $null = Invoke-DemoCommand @('command', 'editor_play') }
Write-Host 'LOCAL GU PATHS: tools/Run-LocalServer.ps1 -LocalEconomy -EconomyConsole. Server owns inventory and path classification; in-memory only.'
