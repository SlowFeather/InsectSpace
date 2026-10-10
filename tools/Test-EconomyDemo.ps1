param([ValidateRange(1,65535)][int]$Port = 7779)
# Requires a running LOCAL economy server and the LocalEconomy Unity scene in Play.
# Sends real TCP/KCP requests; never invokes server economic mutators through Editor eval.
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
$evidence = Join-Path $demoRoot '.artifacts/validation/economy'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$read = 'var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Economy.LocalEconomyPanel>(); return new { ready = p != null && p.Ready, error = p?.Error, walletReady = p?.Transport?.Client.Ready ?? false, wallet = p?.Transport?.Client.Snapshot, battleReady = p?.Transport?.Battle.Ready ?? false, finished = p?.Transport?.Battle.Finished ?? false, room = p?.Transport?.Battle.RoomId, battle = p?.Transport?.Battle.Replica?.Snapshot, status = p?.Transport?.Battle.Status };'
function Read-Economy { (Invoke-DemoRead @('command','eval',$read)).result }
function Wait-Economy([string]$Stage, [scriptblock]$Predicate) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $state = Read-Economy
        if ($state.error) { throw "$Stage failed: $($state.error)" }
        if (& $Predicate $state) {
            $state | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $evidence "kcp-live-$Stage.json") -Encoding utf8
            Write-Host "ECONOMY_TCP_KCP $Stage passed"
            return $state
        }
        Start-Sleep -Milliseconds 150
    } while ($timer.Elapsed.TotalSeconds -lt 20)
    throw "$Stage timed out: $($state.status)"
}
function Invoke-Economy([string]$Code) {
    $null = Invoke-DemoCommand @('command','eval',('var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Gameplay.Economy.LocalEconomyPanel>(); ' + $Code + ' if (p.Error != null) throw new System.Exception(p.Error); return true;'))
}
$start = Wait-Economy 'initial' { param($s) $s.ready -and $s.walletReady }
if ($start.wallet.Phase -ne 0 -or $start.wallet.Wallet.YuanShi -lt 20 -or $start.wallet.Wallet.XianYuanShi -lt 4 -or $start.wallet.ImmortalEssence -ne 100) {
    throw 'This local acceptance sequence requires no active reserve, at least 20/4 stones, and 100 essence. Restart the explicit in-memory fixture server to reset it.'
}
$yuan = $start.wallet.Wallet.YuanShi
$xian = $start.wallet.Wallet.XianYuanShi
Invoke-Economy 'p.Prepare(20, 4);'
$null = Wait-Economy 'reserved' { param($s) $s.walletReady -and $s.wallet.Phase -eq 1 -and $s.wallet.Wallet.YuanShi -eq ($yuan - 20) -and $s.wallet.Wallet.XianYuanShi -eq ($xian - 4) }
Invoke-Economy 'p.BeginBattle();'
$admitted = Wait-Economy 'admitted' { param($s) $s.battleReady -and $s.walletReady -and $s.wallet.Phase -eq 2 }
$room = $admitted.room
Invoke-Economy 'p.BattleAction(InsectSpace.BattleEconomy.ResourceAction.CastSkill);'
$null = Wait-Economy 'cast' { param($s) $s.battleReady -and $s.battle.Essence -eq 70 -and $s.battle.Remaining.YuanShi -eq 20 -and $s.battle.Remaining.XianYuanShi -eq 4 }
Invoke-Economy 'p.BattleAction(InsectSpace.BattleEconomy.ResourceAction.UseYuanShiReserve);'
$null = Wait-Economy 'used-yuan' { param($s) $s.battleReady -and $s.battle.Essence -eq 80 -and $s.battle.Remaining.YuanShi -eq 19 }
Invoke-Economy ("p.Transport.Connect($Port);")
$null = Wait-Economy 'reconnected' { param($s) $s.walletReady -and $s.wallet.Phase -eq 2 -and $s.wallet.Reserve.YuanShi -eq 19 }
Invoke-Economy 'p.BeginBattle();'
$null = Wait-Economy 'recovered' { param($s) $s.battleReady -and $s.room -eq $room -and $s.battle.Essence -eq 80 -and $s.battle.Remaining.YuanShi -eq 19 }
Invoke-Economy 'p.BattleAction(InsectSpace.BattleEconomy.ResourceAction.UseXianYuanShiReserve);'
$null = Wait-Economy 'used-xian' { param($s) $s.battleReady -and $s.battle.Essence -eq 100 -and $s.battle.Remaining.XianYuanShi -eq 3 }
Invoke-Economy 'p.BattleAction(InsectSpace.BattleEconomy.ResourceAction.LeaveBattle);'
$null = Wait-Economy 'settled' { param($s) $s.finished -and $s.walletReady -and $s.wallet.Phase -eq 0 -and $s.wallet.Wallet.YuanShi -eq ($yuan - 1) -and $s.wallet.Wallet.XianYuanShi -eq ($xian - 1) }
Invoke-Economy 'p.SpendWorld(false);'
$null = Wait-Economy 'world-debit' { param($s) $s.walletReady -and $s.wallet.Wallet.YuanShi -eq ($yuan - 4) -and $s.wallet.Wallet.XianYuanShi -eq ($xian - 1) }
Write-Host 'ECONOMY_TCP_KCP_ACCEPTANCE_PASSED: Unity -> TCP admission -> KCP frames -> reconnect -> server settlement -> TCP world debit.'
