param([switch]$StartHost, [ValidateRange(1024,65535)][int]$Port = 18088)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
$code = @'
for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
    if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new Exception("Save current scenes before building resources.");
var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath,"../../../.."));
var result=System.IO.Path.Combine(root,".artifacts/validation/backend-resources/editor-build.json");
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(result));
if(System.IO.File.Exists(result)) System.IO.File.Delete(result);
UnityEditor.EditorApplication.delayCall += () => {
    var previous=System.Environment.GetEnvironmentVariable("INSECTSPACE_BUILD_OUTPUT_ROOT");
    var scenes=UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
    var playScene=UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;
    var buildScenes=UnityEditor.EditorBuildSettings.scenes;
    // Resource building updates URP shader prefilter flags; restore the loaded assets through Unity.
    var pipelines=UnityEditor.AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset",new[]{"Assets/Settings"})
        .Select(id=>UnityEditor.AssetDatabase.LoadMainAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(id))).ToArray();
    var pipelineJson=pipelines.Select(UnityEditor.EditorJsonUtility.ToJson).ToArray();
    var output=System.IO.Path.Combine(root,".artifacts/yoo/editor-wsl-"+System.Guid.NewGuid().ToString("N"));
    try {
        System.Environment.SetEnvironmentVariable("INSECTSPACE_BUILD_OUTPUT_ROOT",output);
        InsectSpace.Editor.HotUpdateBuild.ValidateBuildArtifacts();
        var config=InsectSpace.Client.BootConfiguration.Load();
        System.IO.File.WriteAllText(result,Newtonsoft.Json.JsonConvert.SerializeObject(new {passed=true,sourceRoot=System.IO.Path.Combine(output,UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString()),version=config.packageVersion,tables=config.tableLocations.Length,codeMode="compiled Editor gameplay; native execution validated separately"}));
    } catch(Exception e) {
        System.IO.File.WriteAllText(result,Newtonsoft.Json.JsonConvert.SerializeObject(new {passed=false,error=e.ToString()}));
    } finally {
        System.Environment.SetEnvironmentVariable("INSECTSPACE_BUILD_OUTPUT_ROOT",previous);
        for(int i=0;i<pipelines.Length;i++) {
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(pipelineJson[i],pipelines[i]);
            UnityEditor.EditorUtility.SetDirty(pipelines[i]);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(pipelines[i]);
        }
        UnityEditor.EditorBuildSettings.scenes=buildScenes;
        UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(scenes);
        UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene=playScene;
    }
};
return true;
'@
$null = Invoke-DemoCommand @('command','eval',$code)
$resultPath = Join-Path $demoRoot '.artifacts/validation/backend-resources/editor-build.json'
$timer = [Diagnostics.Stopwatch]::StartNew()
while (!(Test-Path -LiteralPath $resultPath) -and $timer.Elapsed.TotalSeconds -lt 600) { Start-Sleep -Seconds 2 }
if (!(Test-Path -LiteralPath $resultPath)) { throw 'Resource build timed out; inspect the Unity console.' }
$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
if (!$result.passed) { throw $result.error }
Write-Host "WSL_CLIENT_RESOURCES_BUILT tables=$($result.tables) version=$($result.version) source=$($result.sourceRoot)"
if ($StartHost) {
    & "$PSScriptRoot/Manage-WSLResources.ps1" -Action Stop -Port $Port
    & "$PSScriptRoot/Manage-WSLResources.ps1" -Action Start -Port $Port -SourceRoot $result.sourceRoot -Version $result.version
    & "$PSScriptRoot/Manage-WSLResources.ps1" -Action Test -Port $Port
}
