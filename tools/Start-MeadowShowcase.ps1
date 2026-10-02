param([switch]$OpenOnly, [ValidateRange(0, 2)][int]$Quality = 2, [switch]$TacticalGrid)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$null = Invoke-DemoCommand @('command', 'eval', @'
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Save your current scenes before opening MeadowShowcase.");
return true;
'@)
$null = Invoke-DemoCommand @('command', 'open_scene', '--path', 'Assets/InsectSpace/Scenes/MeadowShowcase.unity')
$null = Invoke-DemoCommand @('command', 'eval', @'
// This local Editor preference prevents the Foundation start-scene override from opening Bootstrap.
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene =
    UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/InsectSpace/Scenes/MeadowShowcase.unity");
return "LOCAL RENDER STUDY: no login, world connection, or battle simulation.";
'@)
if (!$OpenOnly) {
    $null = Invoke-DemoCommand @('command', 'editor_play')
    Start-Sleep -Milliseconds 500
    $null = Invoke-DemoRead @('command', 'eval', 'return UnityEngine.Application.isPlaying;')
    $grid = if ($TacticalGrid) { 'true' } else { 'false' }
    $code = @"
UnityEngine.QualitySettings.SetQualityLevel($Quality, true);
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var hero=scene.GetRootGameObjects().First(g=>g.name=="KayKit Mage - CC0 animated hero");
scene.GetRootGameObjects().First(g=>g.name=="Optional tactical hex overlay - presentation only").SetActive($grid);
return new { scene=scene.path, localRenderStudy=true, animated=hero.GetComponent<UnityEngine.Animator>().enabled };
"@
    $result = Invoke-DemoCommand @('command', 'eval', $code)
    if ($result.result.scene -ne 'Assets/InsectSpace/Scenes/MeadowShowcase.unity' -or !$result.result.animated) {
        throw 'The expected local rendering scene did not start.'
    }
    Write-Host "MEADOW_SHOWCASE_READY quality=$Quality local-render-only. Select the Game tab."
} else { Write-Host 'MeadowShowcase opened. Play Mode is stopped.' }
