param([switch]$CaptureOnly)
$ErrorActionPreference = 'Stop'
$evidence = Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/validation/meadow-showcase'
New-Item -ItemType Directory -Force $evidence | Out-Null
if (!$CaptureOnly) {
    & (Join-Path $PSScriptRoot 'Test-ClientDemo.ps1') -Mode PlayMode -Filter 'InsectSpace.Tests.MeadowShowcaseTests' -TimeoutSeconds 180
    Copy-Item (Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/validation/client-demo/PlayMode.json') (Join-Path $evidence 'MeadowPlayMode.json')
}
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$null = Invoke-DemoCommand @('command', 'eval', @'
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Save your current scenes before capturing MeadowShowcase.");
return true;
'@)
$initial = Invoke-DemoCommand @('command', 'eval', 'return UnityEngine.QualitySettings.GetQualityLevel();')
$previousQuality = [int]$initial.result
$before = Invoke-DemoCommand @('command', 'console_status')
$before | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'console-before.json')
$null = Invoke-DemoCommand @('command', 'open_scene', '--path', 'Assets/InsectSpace/Scenes/MeadowShowcase.unity')
$null = Invoke-DemoCommand @('command', 'eval', 'UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/InsectSpace/Scenes/MeadowShowcase.unity"); return true;')
$captureResults = @()
try {
    $null = Invoke-DemoCommand @('command', 'editor_play')
    Start-Sleep -Milliseconds 500
    $null = Invoke-DemoRead @('command', 'eval', 'return UnityEngine.Application.isPlaying;')
    foreach ($q in 0..2) {
        $null = Invoke-DemoCommand @('command', 'eval', "UnityEngine.QualitySettings.SetQualityLevel($q,true); return true;")
        Start-Sleep -Milliseconds 250
        $state = Invoke-DemoCommand @('command', 'eval', @'
var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var objects=scene.GetRootGameObjects();
var meshes=objects.SelectMany(g=>g.GetComponentsInChildren<UnityEngine.MeshFilter>()).ToArray();
var grass=meshes.Where(m=>m.name.StartsWith("Grass_")).ToArray();
var hero=objects.First(g=>g.name=="KayKit Mage - CC0 animated hero");
var animator=hero.GetComponent<UnityEngine.Animator>();
var lod=objects.SelectMany(g=>g.GetComponentsInChildren<InsectSpace.Rendering.MeadowGrassLod>()).Single();
return new {quality=UnityEngine.QualitySettings.GetQualityLevel(),lod=lod.AppliedQuality,
    qualityName=UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()],
    grassChunks=grass.Length,grassBlades=grass.Sum(m=>m.sharedMesh.vertexCount/5),
    grassTriangles=grass.Sum(m=>m.sharedMesh.triangles.Length/3),
    allMeshTriangles=meshes.Sum(m=>m.sharedMesh.triangles.Length/3),
    graphics=UnityEngine.SystemInfo.graphicsDeviceType.ToString(),
    animatorTime=animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
    heroPosition=hero.transform.position.ToString(),
    shaderErrors=UnityEditor.ShaderUtil.GetShaderMessages(UnityEngine.Shader.Find("InsectSpace/Meadow Painterly"))};
'@)
        if ($state.result.quality -ne $q -or $state.result.lod -ne $q -or $state.result.grassChunks -ne 36) {
            throw 'Quality selection or grass chunk validation failed.'
        }
        $png = "Assets/Screenshots/Meadow_Quality_$q.png"
        $shot = Invoke-DemoCommand @('command', 'capture_game_view', '--source', 'camera', '--camera', 'Showcase Camera', '--width', '1440', '--height', '900', '--save_path', $png, '--include_inline_image', 'false')
        $captureResults += [ordered]@{state=$state.result;capture=$shot}
        Write-Host "MEADOW_CAPTURE quality=$q blades=$($state.result.grassBlades) png=$png"
    }
    $null = Invoke-DemoCommand @('command', 'eval', 'UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().First(g=>g.name=="Optional tactical hex overlay - presentation only").SetActive(true); return true;')
    $null = Invoke-DemoCommand @('command', 'capture_game_view', '--source', 'camera', '--camera', 'Showcase Camera', '--width', '1440', '--height', '900', '--save_path', 'Assets/Screenshots/Meadow_Tactical.png', '--include_inline_image', 'false')
    $captureResults | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'quality-captures.json')
} finally {
    $status=Invoke-DemoRead @('command', 'editor_status')
    if ($status.playMode -ne 'stopped') { $null=Invoke-DemoCommand @('command', 'editor_stop') }
    Start-Sleep -Milliseconds 500
    $null=Invoke-DemoRead @('command', 'eval', 'return UnityEngine.Application.isPlaying;')
    $null=Invoke-DemoCommand @('command', 'eval', "UnityEngine.QualitySettings.SetQualityLevel($previousQuality,true); return true;")
}
$after=Invoke-DemoCommand @('command', 'console', '--level', 'warn', '--since', ([string]$before.cursor), '--since_session', $before.session, '--tail', '100')
$after | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'console-capture-session.json')
if (@($after.entries | Where-Object level -eq 'error').Count -gt 0) { throw 'Rendering session emitted an error. Inspect console-capture-session.json.' }
Write-Host "MEADOW_VALIDATED evidence=$evidence. Screenshots require visual review; this is not device performance certification."
