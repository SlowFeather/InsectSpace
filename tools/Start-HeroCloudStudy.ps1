param([switch]$OpenOnly,[ValidateSet('Day','Sunset','Night','Mist')][string]$Preset='Day',[ValidateRange(0,2)][int]$Quality=1)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$null=Invoke-DemoCommand @('command','eval',@'
for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
    if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new System.InvalidOperationException("Save open scenes first.");
return true;
'@)
$scene='Assets/InsectSpace/Scenes/HeroCloudStudy.unity'
$null=Invoke-DemoCommand @('command','open_scene','--path',$scene)
$null=Invoke-DemoCommand @('command','eval','UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/InsectSpace/Scenes/HeroCloudStudy.unity");return true;')
$null=Invoke-DemoCommand @('command','eval_file','--file',(Join-Path $PSScriptRoot 'unity-demo/ConfigureHeroView.cs'))
if(!$OpenOnly) {
    $null=Invoke-DemoCommand @('command','editor_play')
    Start-Sleep -Milliseconds 500
    $null=Invoke-DemoRead @('command','eval','return UnityEngine.Application.isPlaying;')
    $code=@'
UnityEngine.QualitySettings.SetQualityLevel(QUALITY,true);
var sky=UnityEngine.Object.FindFirstObjectByType<InsectSpace.Rendering.StylizedSkyController>();
var actor=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Animator>();
if(!sky || !sky.RuntimeMaterial || !actor || !actor.isHuman)throw new System.InvalidOperationException("Hero study did not initialize.");
sky.SetPreset(InsectSpace.Rendering.StylizedSkyController.Preset.PRESET,0);return true;
'@
    $code=$code.Replace('QUALITY',[string]$Quality).Replace('PRESET',$Preset)
    $null=Invoke-DemoCommand @('command','eval',$code)
    Write-Host "HERO_STUDY_READY preset=$Preset quality=$Quality. Local art study; controls at bottom right."
} else {Write-Host 'HeroCloudStudy opened. Press Play for animated preview.'}
