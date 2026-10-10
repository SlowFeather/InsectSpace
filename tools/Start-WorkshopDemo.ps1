param([switch]$OpenOnly, [ValidateRange(1,65535)][int]$Port = 7779)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$created = Invoke-DemoCommand @('command', 'eval_file', (Join-Path $PSScriptRoot 'unity-demo/CreateWorkshopScene.cs'))
Write-Host $created.result
$null = Invoke-DemoCommand @('command', 'eval', "var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.GuWorkshop.LocalWorkshopPanel>(); p.Port = $Port; UnityEditor.SceneManagement.EditorSceneManager.SaveScene(p.gameObject.scene); return p.Port;")
if (!$OpenOnly) { $null = Invoke-DemoCommand @('command', 'editor_play') }
Write-Host 'LOCAL GU WORKSHOP: tools/Run-LocalServer.ps1 -LocalWorkshop. NPC purchases, server time feeding, refinement and configurable recipes; memory only.'
