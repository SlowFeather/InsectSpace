param([switch]$OpenOnly,[ValidateSet('Day','Sunset','Night')][string]$Preset='Day',[ValidateRange(0,2)][int]$Quality=1)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$null=Invoke-DemoCommand @('command','eval',@'
for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
    if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Save open scenes before opening SkyShowcase.");
return true;
'@)
$null=Invoke-DemoCommand @('command','open_scene','--path','Assets/InsectSpace/Scenes/SkyShowcase.unity')
$null=Invoke-DemoCommand @('command','eval','UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/InsectSpace/Scenes/SkyShowcase.unity"); return true;')
$null=Invoke-DemoCommand @('command','eval_file','--file',(Join-Path $PSScriptRoot 'unity-demo/ConfigureSkyView.cs'))
if(!$OpenOnly) {
    $null=Invoke-DemoCommand @('command','editor_play')
    Start-Sleep -Milliseconds 500
    $null=Invoke-DemoRead @('command','eval','return UnityEngine.Application.isPlaying;')
    $code=@"
UnityEngine.QualitySettings.SetQualityLevel($Quality,true);
var sky=UnityEngine.Object.FindFirstObjectByType<InsectSpace.Rendering.StylizedSkyController>();
if(sky==null || sky.RuntimeMaterial==null) throw new System.InvalidOperationException("Sky did not initialize.");
sky.SetPreset(InsectSpace.Rendering.StylizedSkyController.Preset.$Preset,0);
return new { scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, preset=sky.CurrentPreset.ToString(), localArtStudy=true };
"@
    $result=Invoke-DemoCommand @('command','eval',$code)
    if($result.result.scene -ne 'Assets/InsectSpace/Scenes/SkyShowcase.unity') {throw 'Unexpected Play scene.'}
    Write-Host "SKY_SHOWCASE_READY preset=$Preset quality=$Quality. Game view controls: presets, clouds, quality, sky-only."
} else {Write-Host 'SkyShowcase opened. Click Play for interactive preview.'}
