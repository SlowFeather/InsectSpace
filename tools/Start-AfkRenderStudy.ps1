param(
    [switch]$OpenOnly,
    [switch]$Rebuild,
    [ValidateSet('Day', 'Night', 'Dawn', 'Dusk', 'RainNight')][string]$Preset = 'Day',
    [switch]$Cycle,
    [switch]$HideFairy,
    [ValidateRange(10, 3600)][int]$CycleSeconds = 240,
    [switch]$FullMap
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor

$scene = if ($FullMap) { 'Assets/Temp/AFKStudy/Homestead/AFKHomestead.unity' } else { 'Assets/Temp/AFKStudy/Homestead/AFKRecoveredLakeside.unity' }
$sceneFile = Join-Path $demoProject $scene

if ($Rebuild) {
    $script = Join-Path $demoRoot 'AgentScripts/BuildAfkHomestead.cs'
    $entry = if ($FullMap) { 'BuildAfkHomestead.Run' } else { 'BuildAfkHomestead.BuildStudy' }
    $result = Invoke-DemoCommand @('command', 'run_script', '--file', $script, '--entry', $entry, '--timeout_ms', '180000', '--timeout', '180')
    $built = if ($result.result) { $result.result } else { $result }
    Write-Host ("AFK_RECOVERED_BUILT scene={0}" -f $built.scene)
}

if (!(Test-Path -LiteralPath $sceneFile)) {
    throw "AFK recovered scene is missing: $scene. Run with -Rebuild after the local reference import is available."
}

$null = Invoke-DemoCommand @('command', 'open_scene', '--path', $scene)
$startCode = 'UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("SCENE"); return true;'.Replace('SCENE', $scene)
$null = Invoke-DemoCommand @('command', 'eval', $startCode)

if (!$OpenOnly) {
    $null = Invoke-DemoCommand @('command', 'editor_play')
    Start-Sleep -Milliseconds 500
    $time = switch ($Preset) { 'Day' { '0.5f' } 'Dawn' { '0.25f' } 'Dusk' { '0.75f' } default { '0.05f' } }
    $cycleValue = if ($Cycle) { 'true' } else { 'false' }
    $fairyValue = if ($HideFairy) { 'false' } else { 'true' }
    $code = @"
var environment = UnityEngine.Object.FindAnyObjectByType<InsectSpace.Rendering.AfkHomesteadEnvironment>();
if (!environment) throw new System.InvalidOperationException("AFK recovered environment did not initialize.");
environment.TimeOfDay = $time;
environment.Cycle = $cycleValue;
environment.CycleSeconds = $CycleSeconds;
environment.Apply();
var study = UnityEngine.Object.FindAnyObjectByType<InsectSpace.Rendering.AfkRecoveredStudy>();
if (study) study.SetFairyVisible($fairyValue);
return environment.TimeOfDay;
"@
    $active = Invoke-DemoRead @('command', 'eval', $code)
    Write-Host "AFK_RENDER_STUDY_READY preset=$Preset cycle=$Cycle scene=$scene. Local reference; time, cycle, animation and zoom controls are available. RainNight is a legacy alias for Night; rain is not recovered."
} else {
    Write-Host "$scene opened. Press Play for the local AFK render study."
}
