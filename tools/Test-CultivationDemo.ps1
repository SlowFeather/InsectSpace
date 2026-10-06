param(
    [ValidateRange(1,65535)][int]$Port = 17779,
    [ValidateRange(1,65535)][int]$BattlePort = 17780,
    [ValidateRange(1,65535)][int]$DiagnosticTcpPort = 17777,
    [ValidateRange(1,65535)][int]$DiagnosticKcpPort = 17778
)
# Requires LocalCultivation in Play. Starts and stops ONLY its own disposable in-memory server.
# Client mutations go through Unity -> TCP. XP/evidence use that server's explicit LOCAL console.
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$evidence = Join-Path $demoRoot '.artifacts/validation/cultivation'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$read = 'var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Cultivation.LocalCultivationPanel>(); return new { ready = p != null && p.Ready, error = p?.Error, port = p?.Port, connected = p?.Client.Connected ?? false, available = p?.Client.Ready ?? false, state = p?.Client.Snapshot, result = p?.Client.LastResponse?.Result, wallet = p?.Transport?.Client.Snapshot?.Wallet };'
function Read-Cultivation { (Invoke-DemoRead @('command','eval',$read)).result }
function Invoke-Cultivation([string]$Code) {
    $null = Invoke-DemoCommand @('command','eval',('var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Cultivation.LocalCultivationPanel>(); ' + $Code + ' if (p.Error != null) throw new System.Exception(p.Error); return true;'))
}
function Wait-Cultivation([string]$Checkpoint, [scriptblock]$Predicate) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw "Acceptance server exited: $($process.ExitCode)" }
        $s = Read-Cultivation
        if ($s.error) { throw "$Checkpoint failed: $($s.error)" }
        if (& $Predicate $s) {
            $s | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $evidence "live-$Checkpoint.json") -Encoding utf8
            Write-Host "CULTIVATION_TCP $Checkpoint passed"
            return $s
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    $s | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $evidence "live-$Checkpoint-timeout.json") -Encoding utf8
    throw "$Checkpoint timed out"
}
function Grant-Fixture([string]$Command, [long]$PreviousRevision) {
    $process.StandardInput.WriteLine($Command); $process.StandardInput.Flush()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Invoke-Cultivation 'p.Refresh();'
        $s = Wait-Cultivation 'fixture-refresh' { param($s) $s.available }
        if ($s.state.Revision -gt $PreviousRevision) { return $s }
    } while ($timer.Elapsed.TotalSeconds -lt 10)
    throw 'Server fixture was not observed through TCP.'
}
$initial = Read-Cultivation
if (!$initial.ready) { throw 'Open LocalCultivation and enter Play before running acceptance.' }
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
    Invoke-Cultivation "p.Port = $Port; p.Reconnect();"
    $null = Wait-Cultivation 'initial' { param($s) $s.available -and $s.state.Rank -eq 0 }
    Invoke-Cultivation 'p.Awaken();'
    $born = Wait-Cultivation 'awakened' { param($s) $s.available -and $s.state.Rank -eq 1 }
    $aptitude = $born.state.Aptitude | ConvertTo-Json -Compress
    Invoke-Cultivation 'p.Awaken();'
    $null = Wait-Cultivation 'reroll-rejected' { param($s) $s.available -and $s.result -eq 5 -and ($s.state.Aptitude | ConvertTo-Json -Compress) -eq $aptitude }
    Invoke-Cultivation 'p.Reconnect();'
    $null = Wait-Cultivation 'reconnected' { param($s) $s.available -and ($s.state.Aptitude | ConvertTo-Json -Compress) -eq $aptitude }
    Invoke-Cultivation 'p.Breakthrough();'
    $s = Wait-Cultivation 'experience-gate' { param($s) $s.available -and $s.result -eq 7 -and $s.state.Stage -eq 1 }
    $s = Grant-Fixture 'cultivation-xp' $s.state.Revision
    for ($rank = 1; $rank -le 8; $rank++) {
        if ($rank -le 5) {
            for ($stage = 2; $stage -le 4; $stage++) {
                Invoke-Cultivation 'p.Breakthrough();'
                $s = Wait-Cultivation "rank-$rank-stage-$stage" { param($s) $s.available -and $s.state.Rank -eq $rank -and $s.state.Stage -eq $stage }
            }
        }
        Invoke-Cultivation 'p.Breakthrough();'
        $s = Wait-Cultivation "rank-$rank-evidence-gate" { param($s) $s.available -and $s.state.Rank -eq $rank -and $s.result -eq 8 }
        $requiredProofs = if ($rank -le 5) { 1 } else { 3 }
        for ($i = 0; $i -lt $requiredProofs; $i++) { $s = Grant-Fixture 'cultivation-proof' $s.state.Revision }
        Invoke-Cultivation 'p.Breakthrough();'
        $s = Wait-Cultivation "rank-$($rank + 1)" { param($s) $s.available -and $s.state.Rank -eq ($rank + 1) }
        if (($s.state.Aptitude | ConvertTo-Json -Compress) -ne $aptitude) { throw 'Aptitude changed during cultivation.' }
    }
    Invoke-Cultivation 'p.Breakthrough();'
    $s = Wait-Cultivation 'rank-nine-cap' { param($s) $s.available -and $s.state.Rank -eq 9 -and $s.result -eq 9 }
    Invoke-Cultivation 'p.Reconnect();'
    $s = Wait-Cultivation 'final-reconnect' { param($s) $s.available -and $s.state.Rank -eq 9 -and ($s.state.Aptitude | ConvertTo-Json -Compress) -eq $aptitude }
    if ($s.wallet.YuanShi -ne 100 -or $s.wallet.XianYuanShi -ne 10) { throw 'Cultivation unexpectedly changed wallet balances.' }
    Write-Host 'CULTIVATION_TCP_ACCEPTANCE_PASSED: once-only aptitude, reconnect, authority gates, ranks 1-9, unchanged wallet.'
} finally {
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $outputTask.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $evidence 'Live-Server.log') -Encoding utf8
    $errorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $evidence 'Live-Server-errors.log') -Encoding utf8
    $process.Dispose()
    Invoke-Cultivation "p.Port = $originalPort; p.Reconnect();"
}
