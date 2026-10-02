param(
    [ValidateSet('All', 'EditMode', 'PlayMode')][string]$Mode = 'All',
    [string]$Filter = 'InsectSpace.',
    [ValidateRange(30, 1200)][int]$TimeoutSeconds = 300
)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
# Keep the original saved scenes and Play override in SessionState across test domain reloads.
$prepare = @'
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
        throw new System.InvalidOperationException("Save current scenes before running tests.");
var setup = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
UnityEditor.SessionState.SetString("InsectSpace.Demo.TestScenes", Newtonsoft.Json.JsonConvert.SerializeObject(setup));
UnityEditor.SessionState.SetString("InsectSpace.Demo.TestPlayScene", UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null;
return "Prepared isolated Unity Test Runner session";
'@
$restore = @'
var value = UnityEditor.SessionState.GetString("InsectSpace.Demo.TestScenes", "");
if (!string.IsNullOrEmpty(value)) {
    var setup = Newtonsoft.Json.JsonConvert.DeserializeObject<UnityEditor.SceneManagement.SceneSetup[]>(value);
    UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(setup);
    UnityEditor.SessionState.EraseString("InsectSpace.Demo.TestScenes");
}
var path = UnityEditor.SessionState.GetString("InsectSpace.Demo.TestPlayScene", "");
UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(path);
UnityEditor.SessionState.EraseString("InsectSpace.Demo.TestPlayScene");
return "Restored scenes and Play Mode start setting";
'@
$null = Invoke-DemoCommand @('command', 'eval', $prepare)
$running = $false
try {
    $modes = if ($Mode -eq 'All') { @('EditMode', 'PlayMode') } else { @($Mode) }
    foreach ($currentMode in $modes) {
        # EditMode setup tests can legitimately pin Bootstrap again. Clear it before EACH run.
        $null = Invoke-DemoCommand @('command', 'eval', 'UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = null; return "Play override cleared for this test run";')
        $cliMode = if ($currentMode -eq 'EditMode') { 'editor' } else { 'playmode' }
        $running = $true
        $requestedAt = [DateTime]::UtcNow
        try {
            $null = Invoke-DemoCommand @('command', 'run_tests', '--mode', $cliMode, '--filter', $Filter, '--async_tests', 'true')
        } catch {
            # Entering Play may reload Pipeline after the test request was accepted but before
            # its HTTP reply arrives. Check the persisted request; never launch a duplicate run.
            $requestPath = Join-Path $demoProject 'Temp/pipeline_test_request.json'
            $requestFile = Get-Item -LiteralPath $requestPath -ErrorAction SilentlyContinue
            $request = if ($requestFile) { Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json } else { $null }
            if (!$requestFile -or $requestFile.LastWriteTimeUtc -lt $requestedAt.AddSeconds(-1) -or
                $request.mode -ne $currentMode -or $request.filter -ne $Filter) { throw }
            Write-Host "$currentMode request accepted before domain reload; waiting for its result."
        }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Seconds 1
            $result = Invoke-DemoRead @('command', 'test_status')
            if ($result.status -ne 'running') { break }
        } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
        $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $demoEvidence "$currentMode.json") -Encoding utf8
        if ($result.status -ne 'completed') { throw "$currentMode did not complete: $($result.status). See $demoEvidence." }
        $running = $false
        if ($result.summary.total -eq 0 -or $result.summary.passed -ne $result.summary.total) {
            throw "$currentMode failed or skipped tests. See $demoEvidence/$currentMode.json."
        }
        Write-Host "CLIENT_DEMO_TESTS $currentMode passed=$($result.summary.passed) total=$($result.summary.total)"
    }
} finally {
    if ($running) {
        $latest = Invoke-DemoRead @('command', 'test_status')
        if ($latest.status -eq 'running') { $null = Invoke-DemoCommand @('command', 'cancel_tests') }
    }
    # Unity finishes scene restoration shortly after its completed callback.
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 500
        $state = Invoke-DemoRead @('command', 'editor_status')
    } while (($state.playMode -ne 'stopped' -or $state.domainReloadInProgress) -and $timer.Elapsed.TotalSeconds -lt 30)
    if ($state.playMode -ne 'stopped') { throw 'Test Runner did not leave Play Mode; original scene setup remains in SessionState.' }
    Start-Sleep -Seconds 1
    $null = Invoke-DemoRead @('command', 'eval', 'return UnityEngine.Application.unityVersion;')
    $null = Invoke-DemoCommand @('command', 'eval', $restore)
}
