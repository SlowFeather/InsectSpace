param([switch]$CaptureOnly)
$ErrorActionPreference='Stop'
$evidence=Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/validation/stylized-sky'
New-Item -ItemType Directory -Force $evidence | Out-Null
if(!$CaptureOnly) {
    & (Join-Path $PSScriptRoot 'Test-ClientDemo.ps1') -Mode PlayMode -Filter 'InsectSpace.Tests.StylizedSkyTests' -TimeoutSeconds 180
    Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/validation/client-demo/PlayMode.json') -Destination (Join-Path $evidence 'SkyPlayMode.json')
}
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$status=Invoke-DemoCommand @('command','editor_status')
if($status.playMode -ne 'stopped') {throw 'Stop Play before sky validation.'}
$initial=Invoke-DemoCommand @('command','eval','return UnityEngine.QualitySettings.GetQualityLevel();')
$previousQuality=[int]$initial.result
$before=Invoke-DemoCommand @('command','console_status')
$before | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'console-before.json')
$captures=@()
try {
    & (Join-Path $PSScriptRoot 'Start-SkyShowcase.ps1') -Preset Day -Quality 1
    $null=Invoke-DemoCommand @('command','eval',@'
var sky=UnityEngine.Object.FindFirstObjectByType<InsectSpace.Rendering.StylizedSkyController>();
sky.AnimateClouds=false;
return true;
'@)
    foreach($preset in @('Day','Sunset','Night')) {
        $null=Invoke-DemoCommand @('command','eval',"UnityEngine.Object.FindFirstObjectByType<InsectSpace.Rendering.StylizedSkyController>().SetPreset(InsectSpace.Rendering.StylizedSkyController.Preset.$preset,0);return true;")
        foreach($q in 0..2) {
            $null=Invoke-DemoCommand @('command','eval',"UnityEngine.QualitySettings.SetQualityLevel($q,true);return true;")
            Start-Sleep -Milliseconds 200
            $state=Invoke-DemoCommand @('command','eval',@'
var sky=UnityEngine.Object.FindFirstObjectByType<InsectSpace.Rendering.StylizedSkyController>();
var m=sky.RuntimeMaterial; var texture=m.GetTexture("_CloudAtlas");
return new {preset=sky.CurrentPreset.ToString(),quality=sky.AppliedQuality,details=m.IsKeywordEnabled("_SKY_DETAILS"),
    shaderSupported=m.shader.isSupported,shaderMessages=UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
    atlas=texture.name,atlasBytes=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture),
    graphics=UnityEngine.SystemInfo.graphicsDeviceType.ToString(),playing=UnityEngine.Application.isPlaying,
    screen=new[]{UnityEngine.Screen.width,UnityEngine.Screen.height},scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path};
'@)
            if($state.result.quality -ne $q -or !$state.result.shaderSupported -or @($state.result.shaderMessages).Count -ne 0) {throw 'Sky shader or quality verification failed.'}
            $path="Screenshots/Sky_$($preset)_Q$q.png"
            $capture=Invoke-DemoCommand @('command','capture_game_view','--source','camera','--camera','Sky Showcase Camera','--width','900','--height','1600','--save_path',$path)
            $captures += [ordered]@{state=$state.result;capture=$capture}
        }
        $null=Invoke-DemoCommand @('command','capture_game_view','--source','screen','--width','900','--height','1600','--save_path',"Screenshots/Sky_$($preset)_UI.png")
        Write-Host "SKY_CAPTURE preset=$preset quality=0,1,2"
    }
    $null=Invoke-DemoCommand @('command','eval',@'
UnityEngine.Object.FindFirstObjectByType<InsectSpace.Rendering.StylizedSkyController>().SetPreset(InsectSpace.Rendering.StylizedSkyController.Preset.Day,0);
UnityEngine.Camera.main.cullingMask=0;
return true;
'@)
    $null=Invoke-DemoCommand @('command','capture_game_view','--source','camera','--camera','Sky Showcase Camera','--width','1600','--height','900','--save_path','Screenshots/Sky_Only_Landscape.png')
    $captures | ConvertTo-Json -Depth 16 | Set-Content (Join-Path $evidence 'quality-captures.json')
} finally {
    $status=Invoke-DemoRead @('command','editor_status')
    if($status.playMode -ne 'stopped') {$null=Invoke-DemoCommand @('command','editor_stop')}
    Start-Sleep -Milliseconds 500
    $null=Invoke-DemoRead @('command','eval','return UnityEngine.Application.isPlaying;')
    $null=Invoke-DemoCommand @('command','eval',"UnityEngine.QualitySettings.SetQualityLevel($previousQuality,true);return true;")
}
$after=Invoke-DemoCommand @('command','console','--level','warn','--since',([string]$before.cursor),'--since_session',$before.session,'--tail','100')
$after | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'console-captures.json')
if(@($after.entries | Where-Object level -eq 'error').Count -gt 0) {throw 'Sky capture emitted an error; see console-captures.json.'}
Write-Host "SKY_VALIDATED evidence=$evidence. Mobile device performance remains a separate check."
