param([ValidateRange(1024,65535)][int]$Port = 18088)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
& (Join-Path $PSScriptRoot 'Manage-WSLResources.ps1') -Action Test -Port $Port
Assert-DemoEditor
$prepare = @'
for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++) if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save current scenes first.");
UnityEditor.SessionState.SetString("InsectSpace.ResourceTest.Scenes",Newtonsoft.Json.JsonConvert.SerializeObject(UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup()));
UnityEditor.SessionState.SetString("InsectSpace.ResourceTest.PlayScene",UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=null;
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
return true;
'@
$null = Invoke-DemoCommand @('command','eval',$prepare)
try {
    $null = Invoke-DemoCommand @('command','editor_play')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 500; $state=Invoke-DemoRead @('command','editor_status') } while ($state.playMode -ne 'playing' -and $timer.Elapsed.TotalSeconds -lt 30)
    if ($state.playMode -ne 'playing') { throw 'Editor did not enter resource validation Play mode.' }
    $null = Invoke-DemoRead @('command','eval','return UnityEngine.Application.unityVersion;')
    $null = Invoke-DemoCommand @('command','eval',('InsectSpace.Tests.BackendResourceProbe.Root="http://127.0.0.1:' + $Port + '"; new UnityEngine.GameObject("WSL RESOURCE ACCEPTANCE ONLY").AddComponent<InsectSpace.Tests.BackendResourceProbe>(); return true;'))
    $timer.Restart()
    do {
        Start-Sleep -Milliseconds 500
        $result=(Invoke-DemoRead @('command','eval','var p=UnityEngine.Object.FindFirstObjectByType<InsectSpace.Tests.BackendResourceProbe>(); return new { done=p.Done,error=p.Error,version=p.Version,preDownloads=p.Downloads,remoteTableReads=p.RemoteTableReads,tableBytes=p.TableBytes,progress=p.Progress,remotePrefabLoaded=p.RemotePrefabLoaded,remoteSceneLoaded=p.RemoteSceneLoaded,remoteSceneUnloaded=p.RemoteSceneUnloaded };')).result
    } while (!$result.done -and $timer.Elapsed.TotalSeconds -lt 90)
    if (!$result.done -or $result.error -or $result.remoteTableReads -ne 2 -or $result.tableBytes -lt 1 -or $result.progress -ne 1 -or !$result.remotePrefabLoaded -or !$result.remoteSceneLoaded -or !$result.remoteSceneUnloaded) { throw "YooAsset WSL download failed: $($result.error)" }
    $result | ConvertTo-Json | Set-Content (Join-Path $demoRoot '.artifacts/validation/backend-resources/unity-download.json')
    Write-Host "WSL_YOOASSET_PASS version=$($result.version) remoteTableReads=$($result.remoteTableReads) tableBytes=$($result.tableBytes)"
} finally {
    $state=Invoke-DemoRead @('command','editor_status')
    if ($state.playMode -ne 'stopped') { $null=Invoke-DemoCommand @('command','editor_stop') }
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 500; $state=Invoke-DemoRead @('command','editor_status') } while ($state.playMode -ne 'stopped' -and $timer.Elapsed.TotalSeconds -lt 30)
    if ($state.playMode -ne 'stopped') { throw 'Resource test cleanup is pending; scene setup is saved in SessionState.' }
    $null=Invoke-DemoRead @('command','eval','return UnityEngine.Application.unityVersion;')
    $null=Invoke-DemoCommand @('command','eval', @'
var value=UnityEditor.SessionState.GetString("InsectSpace.ResourceTest.Scenes","");
if(!string.IsNullOrEmpty(value)) UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(Newtonsoft.Json.JsonConvert.DeserializeObject<UnityEditor.SceneManagement.SceneSetup[]>(value));
var path=UnityEditor.SessionState.GetString("InsectSpace.ResourceTest.PlayScene","");
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(path);
UnityEditor.SessionState.EraseString("InsectSpace.ResourceTest.Scenes");
UnityEditor.SessionState.EraseString("InsectSpace.ResourceTest.PlayScene");
return true;
'@)
}
