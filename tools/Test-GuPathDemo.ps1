param(
    [ValidateRange(1,65535)][int]$Port = 17779,
    [ValidateRange(1,65535)][int]$BattlePort = 17780,
    [ValidateRange(1,65535)][int]$DiagnosticTcpPort = 17777,
    [ValidateRange(1,65535)][int]$DiagnosticKcpPort = 17778
)
# Requires LocalGuPath in Play. Starts and stops ONLY its own disposable in-memory server.
# Client mutations go through Unity -> TCP. XP/evidence use that server's explicit LOCAL console.
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$evidence = Join-Path $demoRoot '.artifacts/validation/gu-paths'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$read = 'var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.GuPaths.LocalGuPathPanel>(); return new { ready = p != null && p.Ready, error = p?.Error, port = p?.Port, available = p?.Client.Ready ?? false, state = p?.Client.Snapshot, result = p?.Client.LastResponse?.Result, growth = p?.Transport?.Cultivation.Snapshot, growthReady = p?.Transport?.Cultivation.Ready ?? false, battleReady = p?.Transport?.Battle.Ready ?? false, battleFinished = p?.Transport?.Battle.Finished ?? false, wallet = p?.Transport?.Client.Snapshot?.Wallet };'

function Read-GuPath { (Invoke-DemoRead @('command','eval',$read)).result }
function Invoke-GuPath([string]$Code) {
    $null = Invoke-DemoCommand @('command','eval',('var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.GuPaths.LocalGuPathPanel>(); ' + $Code + ' if (p.Error != null) throw new System.Exception(p.Error); return true;'))
}
function Wait-GuPath([string]$Checkpoint, [scriptblock]$Predicate) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw "Acceptance server exited: $($process.ExitCode)" }
        $s = Read-GuPath
        if ($s.error) { throw "$Checkpoint failed: $($s.error)" }
        if (& $Predicate $s) {
            $s | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $evidence "live-$Checkpoint.json") -Encoding utf8
            Write-Host "GU_PATH_TCP $Checkpoint passed"
            return $s
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    $s | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $evidence "live-$Checkpoint-timeout.json") -Encoding utf8
    throw "$Checkpoint timed out"
}
$initial = Read-GuPath
if (!$initial.ready) { throw 'Open LocalGuPath and enter Play before running acceptance.' }
$originalPort = $initial.port
$ports = @($Port,$BattlePort,$DiagnosticTcpPort,$DiagnosticKcpPort)
if (@($ports | Select-Object -Unique).Count -ne 4) { throw 'Use four distinct acceptance ports.' }
$network = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
$occupied = @($network.GetActiveTcpListeners().Port) + @($network.GetActiveUdpListeners().Port)
if (@($ports | Where-Object { $_ -in $occupied }).Count) { throw 'An acceptance port is already in use; choose unused ports.' }
$dll = Join-Path $demoRoot '.artifacts/dotnet/bin/InsectSpace.Server/Debug/net10.0/InsectSpace.Server.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Build the server or run Test-Foundation.ps1 first.' }
$startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
$startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true; $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
$startInfo.WorkingDirectory = $demoRoot
@($dll,'--local-economy','--economy-console','--economy-port',"$Port",'--economy-battle-port',"$BattlePort",'--tcp-port',"$DiagnosticTcpPort",'--kcp-port',"$DiagnosticKcpPort") | ForEach-Object { $startInfo.ArgumentList.Add($_) }
$process = [Diagnostics.Process]::Start($startInfo)
$outputTask = $process.StandardOutput.ReadToEndAsync(); $errorTask = $process.StandardError.ReadToEndAsync()
try {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw 'Acceptance server failed to start.' }
        if (@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port) -contains $Port) { break }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 10)
    Invoke-GuPath "p.Port = $Port; p.Reconnect();"
    $null = Wait-GuPath 'initial' { param($s) $s.available -and $s.growthReady -and $s.state.PlayerRank -eq 0 }
    Invoke-GuPath 'p.Client.Equip(new int[] { 1001, 1002 });'
    $null = Wait-GuPath 'unawakened-rejected' { param($s) $s.available -and $s.result -eq 5 -and $s.state.Loadout.Count -eq 0 }
    Invoke-GuPath 'p.Transport.Cultivation.Awaken();'
    $null = Wait-GuPath 'awakened' { param($s) $s.growthReady -and $s.growth.Rank -eq 1 }
    Invoke-GuPath 'p.Refresh();'
    $null = Wait-GuPath 'rank-refreshed' { param($s) $s.available -and $s.state.PlayerRank -eq 1 }
    Invoke-GuPath 'p.SetDraft(new int[] { 1001, 1002 }); p.Submit();'
    $moon = Wait-GuPath 'moon-formed' { param($s) $s.available -and $s.result -eq 0 -and $s.state.Profile.Formation -eq 2 -and $s.state.Profile.DominantPath -eq 14 }
    Invoke-GuPath 'p.Client.Equip(new int[] { 3004, 3005 });'
    $null = Wait-GuPath 'higher-rank-rejected' { param($s) $s.available -and $s.result -eq 5 -and $s.state.Revision -eq $moon.state.Revision }
    Invoke-GuPath 'p.Reconnect();'
    $null = Wait-GuPath 'reconnected' { param($s) $s.available -and ($s.state.Loadout -join ',') -eq '1001,1002' }
    Invoke-GuPath 'p.Transport.BeginBattle();'
    $null = Wait-GuPath 'battle-start' { param($s) $s.battleReady }
    Invoke-GuPath 'p.Client.Equip(new int[0]);'
    $null = Wait-GuPath 'battle-lock' { param($s) $s.available -and $s.result -eq 7 -and ($s.state.Loadout -join ',') -eq '1001,1002' }
    Invoke-GuPath 'p.Transport.Battle.Send(InsectSpace.BattleEconomy.ResourceAction.LeaveBattle);'
    $null = Wait-GuPath 'battle-ended' { param($s) $s.battleFinished }
    Invoke-GuPath 'p.SetDraft(new int[] { 1001, 1008 }); p.Submit();'
    $null = Wait-GuPath 'mixed' { param($s) $s.available -and $s.result -eq 0 -and $s.state.Profile.Formation -eq 3 }
    $process.StandardInput.WriteLine('cultivation-xp'); $process.StandardInput.Flush()
    Invoke-GuPath 'p.Refresh();'
    $null = Wait-GuPath 'xp' { param($s) $s.growthReady -and $s.growth.Experience -ge 100000 }
    for ($rank = 1; $rank -le 2; $rank++) {
        for ($stage = 2; $stage -le 4; $stage++) {
            Invoke-GuPath 'p.Transport.Cultivation.Breakthrough();'
            $null = Wait-GuPath "rank-$rank-stage-$stage" { param($s) $s.growthReady -and $s.growth.Rank -eq $rank -and $s.growth.Stage -eq $stage }
        }
        $process.StandardInput.WriteLine('cultivation-proof'); $process.StandardInput.Flush()
        Invoke-GuPath 'p.Refresh();'
        $null = Wait-GuPath "rank-$rank-proof" { param($s) $s.growthReady -and ($s.growth.Proofs -band 1) -ne 0 }
        Invoke-GuPath 'p.Transport.Cultivation.Breakthrough();'
        $null = Wait-GuPath "rank-$($rank+1)" { param($s) $s.growthReady -and $s.growth.Rank -eq ($rank+1) }
    }
    Invoke-GuPath 'p.Refresh();'
    $null = Wait-GuPath 'rank-three-inventory' { param($s) $s.available -and $s.state.PlayerRank -eq 3 }
    Invoke-GuPath 'p.Client.Equip(new int[] { 1001, 2001 });'
    $null = Wait-GuPath 'duplicate-family' { param($s) $s.available -and $s.result -eq 6 -and $s.state.Profile.Formation -eq 3 }
    Invoke-GuPath 'p.SetDraft(new int[] { 3004, 3005, 1003 }); p.Submit();'
    $null = Wait-GuPath 'strength-formed' { param($s) $s.available -and $s.result -eq 0 -and $s.state.Profile.Formation -eq 2 -and $s.state.Profile.DominantPath -eq 37 }
    Invoke-GuPath 'p.Reconnect();'
    $final = Wait-GuPath 'final-reconnect' { param($s) $s.available -and ($s.state.Loadout -join ',') -eq '1003,3004,3005' }
    if ($final.wallet.YuanShi -ne 100 -or $final.wallet.XianYuanShi -ne 10) { throw 'Gu equipment changed the wallet.' }
    Invoke-GuPath 'p.UseConfirmed();'
    $size = (Invoke-DemoRead @('command', 'eval', 'return new { width = UnityEngine.Screen.width, height = UnityEngine.Screen.height };')).result
    $null = Invoke-DemoCommand @('command', 'capture_game_view', '--save_path', 'Assets/Screenshots/GuPaths-Final.png', '--width', "$($size.width)", '--height', "$($size.height)")
    Write-Host 'GU_PATH_TCP_ACCEPTANCE_PASSED: mortal catalog, rank gates, moon/mixed/strength, KCP battle lock, reconnect, unchanged wallet.'
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $outputTask.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $evidence 'Live-Server.log') -Encoding utf8
    $errorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $evidence 'Live-Server-errors.log') -Encoding utf8
    $process.Dispose()
    Invoke-GuPath "p.Port = $originalPort; p.Reconnect();"
}
