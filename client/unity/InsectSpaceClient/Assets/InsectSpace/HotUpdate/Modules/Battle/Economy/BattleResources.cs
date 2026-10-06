using System;
using System.Collections.Generic;
using System.Linq;
using InsectSpace.Economy;

namespace InsectSpace.BattleEconomy
{
    public enum ResourceAction : byte { CastSkill = 1, UseYuanShiReserve = 2, UseXianYuanShiReserve = 3, LeaveBattle = 4 }
    public readonly struct ResourceCommand
    {
        public long ActorId { get; }
        public long Sequence { get; }
        public ResourceAction Action { get; }
        public ResourceCommand(long actorId, long sequence, ResourceAction action) { ActorId = actorId; Sequence = sequence; Action = action; }
    }
    public sealed class BattleResourceRules
    {
        public long SkillCost { get; }
        public long YuanShiRecovery { get; }
        public long XianYuanShiRecovery { get; }
        public BattleResourceRules(long skillCost, long yuanShiRecovery, long xianYuanShiRecovery)
        {
            if (skillCost <= 0 || yuanShiRecovery <= 0 || xianYuanShiRecovery <= 0) throw new ArgumentOutOfRangeException(nameof(skillCost));
            SkillCost = skillCost; YuanShiRecovery = yuanShiRecovery; XianYuanShiRecovery = xianYuanShiRecovery;
        }
    }

    public sealed class BattleResourceSnapshot
    {
        public long ActorId { get; }
        public long Frame { get; }
        public long LastSequence { get; }
        public long Essence { get; }
        public long Capacity { get; }
        public StoneAmounts Remaining { get; }
        public BattleResourceRules Rules { get; }
        public ulong Hash { get; }
        public BattleResourceSnapshot(long actorId, long frame, long lastSequence, long essence, long capacity,
            StoneAmounts remaining, BattleResourceRules rules, ulong hash)
        {
            if (actorId <= 0 || frame < -1 || lastSequence < -1 || (frame == -1 && lastSequence != -1) ||
                essence < 0 || capacity < essence || !remaining.IsNonNegative || rules == null)
                throw new ArgumentException("Invalid battle checkpoint.");
            ActorId = actorId; Frame = frame; LastSequence = lastSequence; Essence = essence; Capacity = capacity;
            Remaining = remaining; Rules = rules; Hash = hash;
        }
    }

    // Pure integer state, advanced only by ordered authority frames. No wallet reference.
    public sealed class BattleResources
    {
        private readonly BattleResourceRules rules;
        public long ActorId { get; }
        public long Frame { get; private set; } = -1;
        public long LastSequence { get; private set; } = -1;
        public long Essence { get; private set; }
        public long Capacity { get; }
        public StoneAmounts Remaining { get; private set; }
        public BattleResources(long actorId, long essence, long capacity, StoneAmounts reserve, BattleResourceRules rules)
        {
            if (actorId <= 0 || essence < 0 || capacity < essence || !reserve.IsNonNegative) throw new ArgumentException("Invalid battle resources.");
            ActorId = actorId; Essence = essence; Capacity = capacity; Remaining = reserve; this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }
        public BattleResourceSnapshot Capture() => new BattleResourceSnapshot(ActorId, Frame, LastSequence, Essence, Capacity, Remaining, rules, StateHash);
        public static BattleResources Restore(BattleResourceSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var state = new BattleResources(snapshot.ActorId, snapshot.Essence, snapshot.Capacity, snapshot.Remaining, snapshot.Rules)
                { Frame = snapshot.Frame, LastSequence = snapshot.LastSequence };
            if (state.StateHash != snapshot.Hash) throw new ArgumentException("Battle checkpoint hash mismatch.");
            return state;
        }
        public bool ApplyFrame(long frame, IReadOnlyList<ResourceCommand> commands)
        {
            if (frame < 0 || Frame == long.MaxValue || frame != Frame + 1 || commands == null || commands.Count > 128) return false;
            var ordered = commands.OrderBy(c => c.ActorId).ThenBy(c => c.Sequence).ToArray();
            long previous = LastSequence;
            foreach (var command in ordered)
            {
                if (command.ActorId != ActorId || command.Sequence <= previous || command.Action < ResourceAction.CastSkill ||
                    command.Action > ResourceAction.LeaveBattle) return false;
                previous = command.Sequence;
            }
            foreach (var command in ordered)
            {
                if (command.Action == ResourceAction.CastSkill)
                { if (Essence >= rules.SkillCost) Essence -= rules.SkillCost; }
                else if (command.Action != ResourceAction.LeaveBattle && Essence < Capacity)
                {
                    var cost = command.Action == ResourceAction.UseYuanShiReserve ? new StoneAmounts(1, 0) : new StoneAmounts(0, 1);
                    if (Remaining.Covers(cost))
                    {
                        long recovery = command.Action == ResourceAction.UseYuanShiReserve ? rules.YuanShiRecovery : rules.XianYuanShiRecovery;
                        Essence += Math.Min(Capacity - Essence, recovery); Remaining = StoneAmounts.Subtract(Remaining, cost);
                    }
                }
            }
            LastSequence = previous; Frame = frame; return true;
        }
        public ulong StateHash
        {
            get
            {
                ulong hash = 14695981039346656037UL;
                foreach (long value in new[] { ActorId, Frame, LastSequence, Essence, Capacity, Remaining.YuanShi, Remaining.XianYuanShi, rules.SkillCost, rules.YuanShiRecovery, rules.XianYuanShiRecovery })
                { unchecked { ulong bits = (ulong)value; for (int i = 0; i < 8; i++) { hash ^= (byte)bits; hash *= 1099511628211UL; bits >>= 8; } } }
                return hash;
            }
        }
    }
}
