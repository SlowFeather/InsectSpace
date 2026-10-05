param([switch]$Stop)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
if ($Stop) {
    $state = Invoke-DemoRead @('command', 'editor_status')
    if ($state.playMode -ne 'stopped') { $null = Invoke-DemoCommand @('command', 'editor_stop') }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 500
        $state = Invoke-DemoRead @('command', 'editor_status')
    } while (($state.playMode -ne 'stopped' -or $state.domainReloadInProgress) -and $timer.Elapsed.TotalSeconds -lt 30)
    if ($state.playMode -ne 'stopped') { throw 'Editor did not leave Play; scene restoration remains pending.' }
    $restore = @'
System.Environment.SetEnvironmentVariable("INSECTSPACE_EDITOR_BACKEND", null);
var value = UnityEditor.SessionState.GetString("InsectSpace.Backend.PreviousScenes", "");
if (!string.IsNullOrEmpty(value)) {
    var setup = Newtonsoft.Json.JsonConvert.DeserializeObject<UnityEditor.SceneManagement.SceneSetup[]>(value);
    var valid = setup.Where(x => !string.IsNullOrEmpty(x.path) && System.IO.File.Exists(x.path)).ToArray();
    if (valid.Length > 0) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(valid);
    UnityEditor.SessionState.EraseString("InsectSpace.Backend.PreviousScenes");
}
var path = UnityEditor.SessionState.GetString("InsectSpace.Backend.PreviousPlayScene", "");
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(path);
UnityEditor.SessionState.EraseString("InsectSpace.Backend.PreviousPlayScene");
return "Restored scene setup and disabled the explicit backend switch";
'@
    $null = Invoke-DemoRead @('command', 'eval', 'return UnityEngine.Application.unityVersion;')
    $null = Invoke-DemoCommand @('command', 'eval', $restore)
    Write-Host 'Editor backend mode disabled. Backend services remain under Start/Stop-BackendServices.'
    return
}
Assert-DemoEditor
foreach ($port in 8081,8082,8083) {
    $health = Invoke-RestMethod "http://127.0.0.1:$port/healthz" -TimeoutSec 3
    if (!$health.mySql -or !$health.redis -or $health.operatingSystem -ne 'Linux') { throw 'Start the native WSL backend first.' }
}
$setup = @'
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Save current scenes before opening backend client.");
UnityEditor.SessionState.SetString("InsectSpace.Backend.PreviousScenes", Newtonsoft.Json.JsonConvert.SerializeObject(UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup()));
UnityEditor.SessionState.SetString("InsectSpace.Backend.PreviousPlayScene", UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/InsectSpace/Scenes/Bootstrap.unity");
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/InsectSpace/Scenes/Bootstrap.unity");
System.Environment.SetEnvironmentVariable("INSECTSPACE_EDITOR_BACKEND", "true");
return "Explicit WSL DEV/TEST client selected";
'@
$null = Invoke-DemoCommand @('command','eval',$setup)
$null = Invoke-DemoCommand @('command','eval_file',(Join-Path $PSScriptRoot 'unity-demo/ConfigureDemoView.cs'))
$null = Invoke-DemoCommand @('command','editor_play')
Write-Host 'WSL client started. Enter your phone, leave the code empty for a server whitelist entry; later launches restore the protected Token.'
