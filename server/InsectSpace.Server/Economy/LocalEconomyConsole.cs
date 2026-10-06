using InsectSpace.BattleEconomy;
using InsectSpace.Economy;
using InsectSpace.Cultivation;

namespace InsectSpace.Server.Economy;

// Explicit developer console on the server process, never a remotely callable economy opcode.
public sealed class LocalEconomyConsole
{
    private readonly LocalEconomyHost host;
    private long frame, sequence;
    private string room = "";
    public LocalEconomyConsole(LocalEconomyHost host) { this.host = host; }
    public string Execute(string command)
    {
        var service = host.Service; var p = host.Principal; var s = service.Snapshot(p);
        if (command?.Trim().StartsWith("cultivation-", StringComparison.Ordinal) == true)
            return CultivationCommand(command.Trim(), s.Phase == ReservePhase.InBattle);
        EconomyResult result;
        if (s.Phase == ReservePhase.InBattle && s.RoomId.StartsWith("local-kcp-", StringComparison.Ordinal) &&
            command?.Trim() != "status" && command?.Trim() != "help" && command?.Trim() != "activity" && command?.Trim() != "monster" && command?.Trim() != "recharge")
            return "Use the client's KCP battle buttons for this network room; console must not bypass its ordered inputs.";
        switch (command?.Trim())
        {
            case "help": return "LOCAL ONLY: status | activity | monster | recharge (SIMULATED +5) | battle-start | cast | use-yuan | use-xian | battle-end | cultivation-status | cultivation-xp (SIMULATED +100000) | cultivation-proof (SIMULATED challenge evidence). Refresh the client after commands.";
            case "activity": result = service.GrantReward(p, RewardSource.Activity, Guid.NewGuid().ToString("N"), 10); break;
            case "monster": result = service.GrantReward(p, RewardSource.Monster, Guid.NewGuid().ToString("N"), 5); break;
            case "recharge": result = service.ApplyVerifiedRecharge(p, "local-simulated-" + Guid.NewGuid().ToString("N"), 5); break;
            case "battle-start":
                if (s.Phase == ReservePhase.InBattle) return "LOCAL: battle already active.";
                room = "local-room-" + Guid.NewGuid().ToString("N");
                result = service.BeginBattle(p, s.ReserveId, room, new BattleResourceRules(30, 10, 50));
                if (result == EconomyResult.Ok) { frame = 0; sequence = 0; }
                break;
            case "cast": case "use-yuan": case "use-xian":
                var action = command.Trim() == "cast" ? ResourceAction.CastSkill : command.Trim() == "use-yuan" ? ResourceAction.UseYuanShiReserve : ResourceAction.UseXianYuanShiReserve;
                result = service.ApplyBattleFrame(p, room, frame, new[] { new ResourceCommand(p.PlayerId, sequence, action) });
                if (result == EconomyResult.Ok) { frame++; sequence++; }
                break;
            case "battle-end": result = service.SettleBattle(p, s.ReserveId, room); break;
            case "status": result = EconomyResult.Ok; break;
            default: return "Unknown LOCAL command. Type help.";
        }
        s = service.Snapshot(p);
        return $"LOCAL {result}: wallet=({s.Wallet.YuanShi},{s.Wallet.XianYuanShi}) held=({s.Reserve.YuanShi},{s.Reserve.XianYuanShi}) phase={s.Phase} essence={s.ImmortalEssence}/{s.EssenceCapacity} revision={s.Revision}. Recharge is SIMULATED.";
    }
    private string CultivationCommand(string command, bool battleActive)
    {
        var service = host.Cultivation; var p = host.Principal; var s = service.Snapshot(p);
        if (battleActive && command != "cultivation-status") return "LOCAL: leave the battle before granting cultivation fixtures.";
        var result = CultivationResult.Ok;
        if (command == "cultivation-xp") result = service.GrantExperience(p, "local-xp-" + Guid.NewGuid().ToString("N"), 100000);
        else if (command == "cultivation-proof")
        {
            if (!s.Awakened || s.Rank == 9 || s.Rank <= 5 && s.Stage != MortalStage.Peak) return "LOCAL: no breakthrough evidence applies at the current stage.";
            var kind = s.Rank < 5 ? CultivationEvidence.MortalTrial : s.Rank == 5 ? CultivationEvidence.Ascension :
                s.Rank == 6 ? CultivationEvidence.HeavenlyTribulation : s.Rank == 7 ? CultivationEvidence.GrandTribulation : CultivationEvidence.MyriadTribulation;
            result = Record(kind);
            if (result == CultivationResult.Ok && s.Rank == 8)
            {
                result = Record(CultivationEvidence.MainPathDaoMarks, 100000);
                if (result == CultivationResult.Ok) result = Record(CultivationEvidence.SupremeGrandmaster);
                if (result == CultivationResult.Ok) result = Record(CultivationEvidence.HeavenlySealBroken);
            }
        }
        else if (command != "cultivation-status") return "Unknown LOCAL cultivation command. Type help.";
        s = service.Snapshot(p);
        return $"LOCAL CULTIVATION / SIMULATED XP AND PROOFS: {result}; rank={s.Rank}, stage={s.Stage}, aptitude={s.Aptitude.Grade}, sea={s.Aptitude.SeaPercent}%, step={s.Aptitude.StopStep}, xp={s.Experience}, tribulations=({s.HeavenlyTribulations},{s.GrandTribulations},{s.MyriadTribulations}), marks={s.MainPathDaoMarks}, revision={s.Revision}.";
        CultivationResult Record(CultivationEvidence kind, long amount = 1) => service.RecordEvidence(p, "local-proof-" + Guid.NewGuid().ToString("N"), s.Rank, s.Stage, kind, amount);
    }
}
