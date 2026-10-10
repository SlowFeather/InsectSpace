param([switch]$OpenOnly,[switch]$AfkReference,[ValidateSet('Day','Sunset','Night','Mist')][string]$Preset='Day',[ValidateRange(0,2)][int]$Quality=1)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$null=Invoke-DemoCommand @('command','eval',@'
for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
    if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new System.InvalidOperationException("Save open scenes first.");
return true;
'@)
$scene=if($AfkReference){'Assets/Temp/AFKStudy/AFKCloudStudy.unity'}else{'Assets/InsectSpace/Scenes/HeroCloudStudy.unity'}
if(!(Test-Path -LiteralPath (Join-Path $demoProject $scene))) { throw "Study scene is missing: $scene. Build the selected local study first." }
$null=Invoke-DemoCommand @('command','open_scene','--path',$scene)
$startCode='UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("SCENE");return true;'.Replace('SCENE',$scene)
$null=Invoke-DemoCommand @('command','eval',$startCode)
$null=Invoke-DemoCommand @('command','eval_file','--file',(Join-Path $PSScriptRoot 'unity-demo/ConfigureHeroView.cs'))
if(!$OpenOnly) {
    $null=Invoke-DemoCommand @('command','editor_play')
    Start-Sleep -Milliseconds 500
    $null=Invoke-DemoRead @('command','eval','return UnityEngine.Application.isPlaying;')
    $code=@'
UnityEngine.QualitySettings.SetQualityLevel(QUALITY,true);
var sky=UnityEngine.Object.FindAnyObjectByType<InsectSpace.Rendering.StylizedSkyController>();
var actor=UnityEngine.Object.FindAnyObjectByType<UnityEngine.Animator>();
if(!sky || !sky.RuntimeMaterial || !actor || !actor.avatar || !actor.avatar.isValid)throw new System.InvalidOperationException("Hero study did not initialize.");
actor.speed=1;sky.AnimateClouds=true;UnityEngine.Camera.main.ResetAspect();
sky.SetPreset(InsectSpace.Rendering.StylizedSkyController.Preset.PRESET,0);return true;
'@
    $code=$code.Replace('QUALITY',[string]$Quality).Replace('PRESET',$Preset)
    $null=Invoke-DemoCommand @('command','eval',$code)
    Write-Host "HERO_STUDY_READY preset=$Preset quality=$Quality. Local art study; controls at bottom right."
} else {Write-Host "$scene opened. Press Play for animated preview."}
