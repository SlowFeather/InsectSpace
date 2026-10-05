param([switch]$SkipNetworkInterruption)
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$evidence = Join-Path $demoRoot '.artifacts/validation/backend-client'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$prefix = @'
var b = UnityEngine.Object.FindFirstObjectByType<InsectSpace.Client.InsectSpaceBootstrap>();
var p = UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsSortMode.None).FirstOrDefault(x => x.GetType().Name == "BackendLobbyPanel");
var t = p?.GetType();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
'@
function Eval-Client([string]$Code) { (Invoke-DemoRead @('command','eval',($prefix + "`n" + $Code))).result }
function Get-ClientState {
    Eval-Client @'
return new { ready=b != null && b.Ready, bootError=b?.LastError, phase=b?.Context?.Session.Phase.ToString(), player=b?.Context?.Session.PlayerId,
busy=p == null || (bool)t.GetProperty("Busy").GetValue(p), error=p == null ? null : t.GetProperty("LastError").GetValue(p),
cached=p != null && (bool)t.GetProperty("HasCachedSession").GetValue(p), restored=p != null && (bool)t.GetProperty("RestoredFromCache").GetValue(p),
worldLoaded=p != null && (bool)t.GetProperty("WorldSceneLoaded").GetValue(p) };
'@
}
function Wait-Client([scriptblock]$Predicate, [string]$Label) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 500
        $state = Get-ClientState
        if ($state.bootError) { throw "Bootstrap failed during $Label." }
        if (& $Predicate $state) { Write-Host "BACKEND_CLIENT_PASS $Label"; return $state }
    } while ($timer.Elapsed.TotalSeconds -lt 60)
    throw "Timed out during $Label; phase=$($state.phase), busy=$($state.busy), error=$($state.error)."
}
function Click-Client([string]$Field) {
    $null = Eval-Client ('((UnityEngine.UI.Button)t.GetField("' + $Field + '",flags).GetValue(p)).onClick.Invoke(); return true;')
}
function Restart-Client {
    $null = Invoke-DemoCommand @('command','editor_stop')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 500; $s=Invoke-DemoRead @('command','editor_status') } while ($s.playMode -ne 'stopped' -and $timer.Elapsed.TotalSeconds -lt 30)
    if ($s.playMode -ne 'stopped') { throw 'Client restart could not stop Play mode.' }
    $null = Invoke-DemoRead @('command','eval','return UnityEngine.Application.unityVersion;')
    $null = Invoke-DemoCommand @('command','editor_play')
}
$passed = [Collections.Generic.List[string]]::new()
$networkStopped = $false
$prepared = $false
try {
    Assert-DemoEditor
    # Preserve any existing encrypted user cache. No credential is returned to PowerShell.
    $null = Invoke-DemoCommand @('command','eval', @'
var cache = new InsectSpace.Gameplay.Modules.BackendSessionCache("http://127.0.0.1:8081");
var path = (string)cache.GetType().GetField("path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(cache);
var backup = path + ".acceptance-backup";
if (System.IO.File.Exists(backup)) throw new System.InvalidOperationException("Previous acceptance cache backup exists; restore it first.");
if (System.IO.File.Exists(path)) System.IO.File.Move(path, backup);
UnityEditor.SessionState.SetString("InsectSpace.Backend.TestCache",path);
return true;
'@)
    $prepared = $true
    & (Join-Path $PSScriptRoot 'Start-BackendClient.ps1')
    $null = Wait-Client { param($s) $s.ready -and !$s.busy -and $s.phase -eq 'SignedOut' } 'ready-signed-out'
    $null = Eval-Client @'
var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../../../.."));
var line = System.IO.File.ReadAllLines(System.IO.Path.Combine(root,".artifacts/validation/backend/wsl.env")).First(x => x.StartsWith("INSECTSPACE_PHONE_WHITELIST="));
var number = line.Substring(line.IndexOf('=')+1).Split(new[]{',',';'},System.StringSplitOptions.RemoveEmptyEntries).First().Trim();
((UnityEngine.UI.InputField)t.GetField("phone",flags).GetValue(p)).text = number;
((UnityEngine.UI.InputField)t.GetField("code",flags).GetValue(p)).text = "";
((UnityEngine.UI.Button)t.GetField("login",flags).GetValue(p)).onClick.Invoke();
return true;
'@
    $first = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'Lobby' -and $s.cached -and !$s.restored } 'whitelist-empty-code'
    $passed.Add('whitelist-empty-code')
    $null = Eval-Client '((UnityEngine.UI.InputField)t.GetField("phone",flags).GetValue(p)).text = ""; return true;'
    Click-Client 'enterWorld'
    $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'World' -and $s.worldLoaded } 'world-resource-scene'
    $passed.Add('world-resource-scene')
    $null = Invoke-DemoCommand @('command','capture_game_view','--source','screen','--width','1440','--height','900','--save_path','Temp/backend-client-world.png')
    Copy-Item (Join-Path $demoProject 'Assets/Temp/backend-client-world.png') (Join-Path $evidence 'world.png')
    Restart-Client
    $restored = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'Lobby' -and $s.restored -and $s.cached } 'token-after-restart'
    if ($restored.player -ne $first.player) { throw 'Token restored a different player.' }
    $passed.Add('token-after-restart')
    if (!$SkipNetworkInterruption) {
        & (Join-Path $PSScriptRoot 'Stop-BackendServices.ps1')
        $networkStopped = $true
        Restart-Client
        $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'SignedOut' -and $s.cached -and $s.error } 'outage-keeps-cache'
        & (Join-Path $PSScriptRoot 'Start-BackendServices.ps1')
        $networkStopped = $false
        Click-Client 'resume'
        $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'Lobby' -and $s.restored } 'retry-token-after-outage'
        $passed.Add('outage-keeps-cache'); $passed.Add('retry-token-after-outage')
    }
    $null = Eval-Client @'
var path=UnityEditor.SessionState.GetString("InsectSpace.Backend.TestCache","");
System.IO.File.Copy(path,path+".revoked-test",true); return true;
'@
    Click-Client 'logout'
    $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'SignedOut' -and !$s.cached } 'logout-revokes-and-clears'
    $null = Eval-Client @'
var path=UnityEditor.SessionState.GetString("InsectSpace.Backend.TestCache","");
System.IO.File.Move(path+".revoked-test",path); return true;
'@
    Restart-Client
    $null = Wait-Client { param($s) $s.ready -and !$s.busy -and $s.phase -eq 'SignedOut' -and !$s.cached -and $s.error } 'revoked-token-cleared'
    $passed.Add('logout-revokes-and-clears'); $passed.Add('revoked-token-cleared')
    $null = Eval-Client @'
var suffix = (System.BitConverter.ToUInt32(System.Guid.NewGuid().ToByteArray(),0) % 100000000).ToString("D8");
((UnityEngine.UI.InputField)t.GetField("phone",flags).GetValue(p)).text = "+86138" + suffix;
((UnityEngine.UI.Button)t.GetField("requestCode",flags).GetValue(p)).onClick.Invoke(); return true;
'@
    $null = Wait-Client { param($s) !$s.busy -and !$s.error -and $s.phase -eq 'SignedOut' } 'dev-test-otp-issued'
    Click-Client 'login'
    $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'Lobby' -and $s.cached -and !$s.restored } 'dev-test-otp-login'
    Restart-Client
    $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'Lobby' -and $s.restored } 'otp-then-token-only'
    $passed.Add('dev-test-otp-login'); $passed.Add('otp-then-token-only')
    Click-Client 'logout'
    $null = Wait-Client { param($s) !$s.busy -and $s.phase -eq 'SignedOut' -and !$s.cached } 'test-session-cleanup'
    [ordered]@{ passed=$true; executedAt=[DateTimeOffset]::Now.ToString('o'); tests=$passed.ToArray(); resourceMode='YooAsset Editor simulation; remote hosting verified separately'; realSms=$false } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'result.json')
} finally {
    if ($networkStopped) { & (Join-Path $PSScriptRoot 'Start-BackendServices.ps1') }
    if ($prepared) {
        & (Join-Path $PSScriptRoot 'Start-BackendClient.ps1') -Stop
        $null = Invoke-DemoCommand @('command','eval', @'
var path=UnityEditor.SessionState.GetString("InsectSpace.Backend.TestCache","");
if (!string.IsNullOrEmpty(path)) {
    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    if (System.IO.File.Exists(path+".acceptance-backup")) System.IO.File.Move(path+".acceptance-backup",path);
    if (System.IO.File.Exists(path+".revoked-test")) System.IO.File.Delete(path+".revoked-test");
    UnityEditor.SessionState.EraseString("InsectSpace.Backend.TestCache");
}
UnityEditor.AssetDatabase.DeleteAsset("Assets/Temp/backend-client-world.png");
if (System.IO.Directory.Exists("Assets/Temp") && !System.IO.Directory.EnumerateFileSystemEntries("Assets/Temp").Any()) UnityEditor.AssetDatabase.DeleteAsset("Assets/Temp");
return true;
'@)
    }
}
