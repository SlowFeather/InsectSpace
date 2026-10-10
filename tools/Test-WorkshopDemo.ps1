param(
    [ValidateRange(1,65535)][int]$Port = 18779,
    [ValidateRange(1,65535)][int]$BattlePort = 18780
)
# Runs real Unity -> TCP/KCP against a disposable TEST-ONLY host. Live design tables stay untouched.
. (Join-Path $PSScriptRoot 'unity-demo/UnityDemoCli.ps1')
Assert-DemoEditor
if ($Port -eq $BattlePort) { throw 'Use distinct acceptance ports.' }
$network = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
$occupied = @($network.GetActiveTcpListeners().Port) + @($network.GetActiveUdpListeners().Port)
if ($Port -in $occupied -or $BattlePort -in $occupied) { throw 'Acceptance port occupied; choose unused ports.' }
$evidence = Join-Path $demoRoot ('.artifacts/validation/workshop-live/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path "$evidence/input/Tables","$evidence/data" -Force | Out-Null
$liveFiles = @(Get-ChildItem "$demoRoot/design/luban/GuWorkshop/Tables" -Filter '*.json') +
    @(Get-ChildItem "$demoProject/Assets/InsectSpace/Resources/LocalGuWorkshop" -Filter '*.bytes') +
    @(Get-ChildItem "$demoRoot/server/Generated/Data/GuWorkshop" -Filter '*.bytes')
$liveHashes = @{}; foreach ($file in $liveFiles) { $liveHashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
Copy-Item -LiteralPath "$demoRoot/design/luban/GuWorkshop/Defines" -Destination "$evidence/input/Defines" -Recurse
Copy-Item -LiteralPath "$demoRoot/design/luban/GuWorkshop/luban.conf" -Destination "$evidence/input/luban.conf"
$fixture = @{
    items = @(@{id=1;name='TEST ONLY food/material';note='Synthetic integration fixture, not game design'})
    offers = @(@{id=1;gu=1001;item=0;quantity=1;yuanShi=3},@{id=2;gu=1002;item=0;quantity=1;yuanShi=2},@{id=3;gu=0;item=1;quantity=10;yuanShi=1})
    care = @(1001,1002,2001 | ForEach-Object { @{id=$_;food=1;foodCount=1;fedSeconds=10;refineFee=1;success=5000;destroy=2500;note='TEST ONLY'} })
    recipes = @(@{id=1;name='TEST ONLY fusion';ingredients=@(@{id=1001;count=1},@{id=1002;count=2});output=2001;minimumRank=2;materials=@(@{id=1;count=2});yuanShi=4;success=5000;destroy=2500;source='Synthetic integration fixture, not approved gameplay'})
}
foreach ($key in $fixture.Keys) { ConvertTo-Json -InputObject $fixture[$key] -Depth 10 | Set-Content -LiteralPath "$evidence/input/Tables/$key.json" -Encoding utf8 }
& "$PSScriptRoot/Test-WorkshopTables.ps1" -TablesPath "$evidence/input/Tables"
$luban = Join-Path $demoRoot '.tools/luban/4.5.0/Luban/Luban.dll'
$version = & dotnet $luban --version 2>&1
# Luban 4.5.0 prints --version on stderr and returns 1; generation below must return 0.
if (($version -join ' ') -notmatch '^Luban 4\.5\.0(?:[+\s]|$)') { throw 'Pinned Luban 4.5.0 required.' }
foreach ($target in @('client','server')) {
    & dotnet $luban -t $target -c cs-bin -d bin --conf "$evidence/input/luban.conf" -x "cs-bin.outputCodeDir=$evidence/code/$target" -x "bin.outputDataDir=$evidence/data/$target" *> "$evidence/luban-$target.log"
    if ($LASTEXITCODE -ne 0) { throw "Luban fixture generation failed; see $evidence/luban-$target.log" }
    Set-Content -LiteralPath "$evidence/data/$target/TEST_ONLY.txt" -Value 'WORKSHOP_TEST_FIXTURE' -Encoding utf8
}
foreach ($file in Get-ChildItem "$evidence/data/client" -Filter '*.bytes') {
    if ((Get-FileHash $file.FullName).Hash -ne (Get-FileHash "$evidence/data/server/$($file.Name)").Hash) { throw 'Fixture client/server hashes differ.' }
}
& dotnet build "$demoRoot/tests/InsectSpace.Foundation.Tests/InsectSpace.Foundation.Tests.csproj" --nologo *> "$evidence/build.log"
if ($LASTEXITCODE -ne 0) { throw "Test host build failed; see $evidence/build.log" }
$startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
$startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true; $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
$startInfo.WorkingDirectory = $demoRoot
$dll = Join-Path $demoRoot '.artifacts/dotnet/bin/InsectSpace.Foundation.Tests/Debug/net10.0/InsectSpace.Foundation.Tests.dll'
@($dll,'--workshop-acceptance-host',"$evidence/data/server","$Port","$BattlePort") | ForEach-Object { $startInfo.ArgumentList.Add($_) }
$process = [Diagnostics.Process]::Start($startInfo)
$errors = $process.StandardError.ReadToEndAsync()
$serverLog = [Collections.Generic.List[string]]::new()
function Read-HostLine([string]$Prefix) {
    do {
        $lineTask = $process.StandardOutput.ReadLineAsync()
        if (!$lineTask.Wait(10000)) { throw 'Acceptance host response timed out.' }
        $line = $lineTask.Result
        if ($null -eq $line) { throw 'Acceptance host exited before acknowledgement.' }
        $serverLog.Add($line)
    } while (!$line.StartsWith($Prefix,[StringComparison]::Ordinal))
    return $line
}
function Send-Fixture([string]$Command) {
    $process.StandardInput.WriteLine($Command); $process.StandardInput.Flush()
    return Read-HostLine "FIXTURE_ACK $Command "
}
$read = @'
var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Tests.WorkshopAcceptanceProbe>();
return Newtonsoft.Json.JsonConvert.SerializeObject(new {
    ready = p != null && p.Ready, error = p?.Error, available = p?.Client?.Ready ?? false,
    state = p?.Client?.Snapshot, response = p?.Client?.LastResponse,
    retryable = p?.Client?.RetryableRequest != null, status = p?.Client?.Status,
    growth = p?.Transport?.Cultivation.Snapshot, growthReady = p?.Transport?.Cultivation.Ready ?? false,
    walletReady = p?.Transport?.Client.Ready ?? false, battleReady = p?.Transport?.Battle.Ready ?? false,
    battleFinished = p?.Transport?.Battle.Finished ?? false,
    activeFormation = p?.Client?.Snapshot == null ? -1 : (int)p.Catalog.ActiveProfile(p.Client.Snapshot).Formation
});
'@
function Read-Probe { (Invoke-DemoRead @('command','eval',$read)).result | ConvertFrom-Json }
function Invoke-Probe([string]$Code) {
    $null = Invoke-DemoCommand @('command','eval',('var p = UnityEngine.Object.FindObjectOfType<InsectSpace.Tests.WorkshopAcceptanceProbe>(); ' + $Code + ' return true;'))
}
$checks = 0
function Wait-Probe([string]$Checkpoint,[scriptblock]$Predicate) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw "Acceptance host exited: $($process.ExitCode)" }
        $state = Read-Probe
        if ($state.error) { throw "$Checkpoint failed: $($state.error)" }
        if (& $Predicate $state) {
            $state | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$evidence/$Checkpoint.json" -Encoding utf8
            $script:checks++; Write-Host "WORKSHOP_UNITY_TCP $Checkpoint passed"; return $state
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 50)
    $state | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath "$evidence/$Checkpoint-timeout.json" -Encoding utf8
    throw "$Checkpoint timed out: $($state.status)"
}
function Request([string]$Checkpoint,[string]$Code,[int]$Result = 0) {
    $previousId = (Read-Probe).response.RequestId
    Invoke-Probe $Code
    $state = Wait-Probe $Checkpoint { param($s) $s.available -and !$s.retryable -and $null -ne $s.response -and $s.response.RequestId -gt $previousId }
    if ($state.response.Result -ne $Result) { throw "$Checkpoint result $($state.response.Result), expected $Result" }
    return $state
}
function Assert-Value([bool]$Condition,[string]$Message) { if (!$Condition) { throw $Message } }
$enteredPlay = $false
try {
    $null = Read-HostLine 'FIXTURE_READY '
    $null = Invoke-DemoCommand @('command','editor_play'); $enteredPlay = $true
    $playTimer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 250
        $playing = (Invoke-DemoRead @('command','eval','return UnityEngine.Application.isPlaying;')).result
    } while ($playing -ne $true -and $playTimer.Elapsed.TotalSeconds -lt 30)
    if ($playing -ne $true) { throw 'Editor did not finish entering Play.' }
    $directoryLiteral = '"' + ("$evidence/data/client".Replace('\','/')) + '"'
    $launch = 'var o = new UnityEngine.GameObject("WORKSHOP ACCEPTANCE - TEST ONLY"); var p = o.AddComponent<InsectSpace.Tests.WorkshopAcceptanceProbe>(); p.FixtureDirectory = ' + $directoryLiteral + '; p.Port = ' + $Port + '; return true;'
    $null = Invoke-DemoCommand @('command','eval',$launch)
    $initial = Wait-Probe '01-connected' { param($s) $s.available -and $s.growthReady -and $s.walletReady }
    Assert-Value ($initial.state.Inventory.Count -eq 0 -and $initial.state.Wallet.YuanShi -eq 100) 'Expected empty purchased inventory and 100 fixture YuanShi.'
    $null = Request '02-unawakened-rejected' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 1);' 6
    Invoke-Probe 'p.Transport.Cultivation.Awaken();'
    $null = Wait-Probe '03-awakened' { param($s) $s.growthReady -and $s.growth.Rank -eq 1 }
    $null = Request '04-rank-refreshed' 'p.Client.Refresh();'
    $moon = (Request '05-buy-moon' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 1);').response.ProducedInstance
    $lightA = (Request '06-buy-light-a' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 2);').response.ProducedInstance
    $lightB = (Request '07-buy-light-b' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 2);').response.ProducedInstance
    Assert-Value ($lightA -ne $lightB) 'Same definition must produce distinct instances.'
    $null = Request '08-buy-food' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 3);'
    $null = Send-Fixture 'roll:6000'
    $s = Request '09-refine-preserved' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $moon });" 15
    Assert-Value ($s.state.Inventory.Count -eq 3 -and $s.state.Wallet.YuanShi -eq 91) 'Failed refinement must retain Gu and charge exactly once.'
    $null = Send-Fixture 'roll:0'
    $null = Request '10-refine-success' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $moon });"
    $null = Send-Fixture 'roll:9999'
    $s = Request '11-refine-destroyed' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $lightA });" 16
    Assert-Value ($s.response.RemovedInstances[0] -eq $lightA -and $s.state.Inventory.Count -eq 2) 'Single refinement removed wrong instance.'
    $null = Send-Fixture 'roll:0'
    $null = Request '12-refine-light' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $lightB });"
    $replacement = (Request '13-buy-replacement' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 2);').response.ProducedInstance
    $null = Request '14-refine-replacement' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $replacement });"
    $null = Request '15-duplicate-family-rejected' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Equip, instances: new long[] { $lightB, $replacement });" 13
    $s = Request '16-equip' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Equip, instances: new long[] { $moon, $lightB });"
    Assert-Value ($s.state.Loadout.Count -eq 2 -and $s.activeFormation -ne 0) 'Equipped active Gu should contribute a profile.'
    $null = Send-Fixture 'time:110'
    $s = Request '17-dormant' 'p.Client.Refresh();'
    Assert-Value ($s.activeFormation -eq 0 -and $s.state.Loadout.Count -eq 2 -and $s.state.Inventory.Count -eq 3) 'Dormancy must preserve instances and slots, but disable profile.'
    Invoke-Probe "p.Transport.Connect($Port);"
    $s = Wait-Probe '18-dormant-reconnect' { param($s) $s.available -and $s.growthReady -and $s.walletReady }
    Assert-Value ($s.state.Wallet.YuanShi -eq 85 -and $s.activeFormation -eq 0) 'Reconnect changed wallet or dormant state.'
    foreach ($id in @($moon,$lightB,$replacement)) { $s = Request "19-feed-$id" "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Feed, 1, new long[] { $id });" }
    Assert-Value ($s.state.Materials[0].Count -eq 7 -and $s.activeFormation -ne 0) 'Feeding did not restore active profile or consumed wrong amount.'
    $null = Request '20-unequip' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Equip);'
    $ids = "$moon, $lightB, $replacement"
    $null = Request '21-fusion-rank-rejected' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Fuse, 1, new long[] { $ids });" 6
    $null = Send-Fixture 'cultivation-xp'; Invoke-Probe 'p.Transport.Cultivation.Refresh();'
    $null = Wait-Probe '22-xp' { param($s) $s.growthReady -and $s.growth.Experience -ge 100000 }
    foreach ($expectedStage in 2..4) {
        Invoke-Probe 'p.Transport.Cultivation.Breakthrough();'
        $null = Wait-Probe "23-stage-$expectedStage" { param($s) $s.growthReady -and $s.growth.Stage -eq $expectedStage }
    }
    $null = Send-Fixture 'cultivation-proof'; Invoke-Probe 'p.Transport.Cultivation.Refresh();'
    $null = Wait-Probe '24-proof' { param($s) $s.growthReady -and ($s.growth.Proofs -band 1) -ne 0 }
    Invoke-Probe 'p.Transport.Cultivation.Breakthrough();'
    $null = Wait-Probe '25-rank-two' { param($s) $s.growthReady -and $s.growth.Rank -eq 2 }
    $null = Request '26-workshop-rank-two' 'p.Client.Refresh();'
    $null = Send-Fixture 'roll:6000'
    $s = Request '27-fuse-preserved' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Fuse, 1, new long[] { $ids });" 15
    Assert-Value ($s.state.Inventory.Count -eq 3 -and $s.state.Wallet.YuanShi -eq 81 -and $s.state.Materials[0].Count -eq 5) 'Preserved fusion charged wrong inputs.'
    $null = Send-Fixture 'roll:9999'
    $s = Request '28-fuse-destroyed' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Fuse, 1, new long[] { $ids });" 16
    Assert-Value ($s.response.RemovedInstances.Count -eq 1 -and $s.response.RemovedInstances[0] -eq $moon -and $s.state.Wallet.YuanShi -eq 77 -and $s.state.Materials[0].Count -eq 3) 'Destroyed fusion did not lose exactly the chosen instance and costs.'
    $newMoon = (Request '29-buy-new-moon' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 1);').response.ProducedInstance
    $null = Send-Fixture 'roll:0'
    $null = Request '30-refine-new-moon' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $newMoon });"
    $s = Request '31-fuse-success' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Fuse, 1, new long[] { $newMoon, $lightB, $replacement });"
    $output = $s.response.ProducedInstance
    Assert-Value ($s.response.RemovedInstances.Count -eq 3 -and $s.state.Inventory.Count -eq 1 -and $s.state.Inventory[0].DefinitionId -eq 2001 -and $s.state.Inventory[0].Refined -and $s.state.Wallet.YuanShi -eq 69 -and $s.state.Materials[0].Count -eq 1) 'Fusion did not atomically replace inputs and deduct exact costs.'
    $null = Request '32-equip-output' "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Equip, instances: new long[] { $output });"
    Invoke-Probe 'p.Transport.Client.Refresh();'
    $null = Wait-Probe '33-wallet-refresh' { param($s) $s.walletReady }
    Invoke-Probe 'p.Transport.BeginBattle();'
    $null = Wait-Probe '34-kcp-admitted' { param($s) $s.battleReady }
    $s = Request '35-battle-lock' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 1);' 14
    Assert-Value ($s.state.BattleActive -and $s.state.Wallet.YuanShi -eq 69) 'Battle lock altered workshop wallet.'
    Invoke-Probe 'p.Transport.Battle.Send(InsectSpace.BattleEconomy.ResourceAction.LeaveBattle);'
    $null = Wait-Probe '36-kcp-ended' { param($s) $s.battleFinished }
    Invoke-Probe "p.Transport.Connect($Port);"
    $s = Wait-Probe '37-reconnected' { param($s) $s.available -and $s.growthReady -and $s.walletReady }
    Assert-Value ($s.state.Loadout[0] -eq $output -and $s.state.Inventory.Count -eq 1 -and $s.state.Wallet.YuanShi -eq 69) 'Final reconnect did not preserve fused loadout.'
    $uncertain = (Request '38-buy-for-timeout' 'p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Buy, 2);').response.ProducedInstance
    $null = Send-Fixture 'roll:9999'; $beforeRoll = Send-Fixture 'state'
    # Expire the client request before its reply is dispatched. The server still executes the real TCP request.
    Invoke-Probe "p.Client.Request(InsectSpace.GuWorkshop.WorkshopCommand.Refine, instances: new long[] { $uncertain }); p.Client.Tick(9);"
    $null = Wait-Probe '39-uncertain-result' { param($s) $s.retryable }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { $serverState = Send-Fixture 'state'; if ($serverState -match 'inventory=1 wallet=66$') { break }; Start-Sleep -Milliseconds 100 } while ($timer.Elapsed.TotalSeconds -lt 10)
    Assert-Value ($serverState -match 'inventory=1 wallet=66$') 'Timed-out request was not observed committed at server.'
    Invoke-Probe "p.Transport.Connect($Port);"
    $null = Wait-Probe '40-uncertain-reconnect' { param($s) $s.available -and $s.retryable }
    $s = Request '41-replay-original' 'p.Client.Retry();' 16
    $afterRoll = Send-Fixture 'state'
    Assert-Value ($s.response.Replayed -and !$s.retryable -and $s.state.Wallet.YuanShi -eq 66 -and $s.state.Wallet.XianYuanShi -eq 10 -and $s.state.Inventory.Count -eq 1 -and $s.response.RemovedInstances[0] -eq $uncertain) 'Uncertain refinement was not replayed exactly once.'
    $beforeCalls = [int]([regex]::Match($beforeRoll,'calls=(\d+)').Groups[1].Value)
    $afterCalls = [int]([regex]::Match($afterRoll,'calls=(\d+)').Groups[1].Value)
    Assert-Value ($afterCalls -eq $beforeCalls + 1) 'Replay redrew random outcome.'
    @{status='passed';checkpoints=$checks;syntheticRules=$true;wallet=66;randomCallsBeforeTimeout=$beforeCalls;randomCallsAfterReplay=$afterCalls} | ConvertTo-Json | Set-Content "$evidence/summary.json" -Encoding utf8
    Write-Host "WORKSHOP_UNITY_TCP_ACCEPTANCE_PASSED checkpoints=$checks evidence=$evidence"
} catch {
    $_ | Out-String | Set-Content "$evidence/failure.log" -Encoding utf8
    throw
} finally {
    try {
        if ($enteredPlay) {
            $editor = Invoke-DemoRead @('command','editor_status')
            if ($editor.playMode -ne 'stopped') { $null = Invoke-DemoCommand @('command','editor_stop') }
        }
    }
    finally {
        try {
            if (!$process.HasExited) {
                try { $null = Send-Fixture 'quit' } finally { if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() } }
            }
        } finally {
            $serverLog | Set-Content "$evidence/server.log" -Encoding utf8
            $errors.GetAwaiter().GetResult() | Set-Content "$evidence/server-errors.log" -Encoding utf8
            $process.Dispose()
            foreach ($entry in $liveHashes.GetEnumerator()) {
                if ((Get-FileHash -LiteralPath $entry.Key).Hash -ne $entry.Value) { throw "Live table changed during fixture acceptance: $($entry.Key)" }
            }
            Write-Host 'Live workshop JSON and generated bytes hashes unchanged.'
        }
    }
}
