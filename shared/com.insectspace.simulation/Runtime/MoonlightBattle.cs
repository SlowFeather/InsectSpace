using System;
using System.Collections.Generic;

namespace InsectSpace.Simulation
{
    public enum MoonlightBattleCommandKind : byte
    {
        CastMoonBlade = 1,
        SetAutoCast = 2,
        Retreat = 3
    }

    public readonly struct MoonlightBattleCommand
    {
        public long ActorId { get; }
        public long Sequence { get; }
        public MoonlightBattleCommandKind Kind { get; }
        public bool Enabled { get; }
        public MoonlightBattleCommand(long actorId, long sequence, MoonlightBattleCommandKind kind, bool enabled = false)
        {
            ActorId = actorId;
            Sequence = sequence;
            Kind = kind;
            Enabled = enabled;
        }
    }

    public enum MoonlightBattlePhase : byte { Active = 1, Victory = 2, Defeat = 3, Retreated = 4 }

    // All values are integer frame rules. The server owns the authoritative instance.
    public sealed class MoonlightBattleRules
    {
        public const int Version = 1;
        public int PlayerMaxHp { get; }
        public int MonsterMaxHp { get; }
        public int AutoAttackDamage { get; }
        public int AutoAttackIntervalFrames { get; }
        public int MonsterAttackDamage { get; }
        public int MonsterAttackIntervalFrames { get; }
        public int MaxResource { get; }
        public int ResourceRegenPerFrame { get; }
        public int MoonBladeDamage { get; }
        public int MoonBladeCooldownFrames { get; }
        public int MoonBladeResourceCost { get; }
        public int MoonlightGuId { get; }
        public string SkillId { get; }
        public string SkillName { get; }

        public MoonlightBattleRules(int playerMaxHp, int monsterMaxHp, int autoAttackDamage, int autoAttackIntervalFrames,
            int monsterAttackDamage, int monsterAttackIntervalFrames, int maxResource, int resourceRegenPerFrame,
            int moonBladeDamage, int moonBladeCooldownFrames, int moonBladeResourceCost, int moonlightGuId,
            string skillId, string skillName)
        {
            if (playerMaxHp <= 0 || monsterMaxHp <= 0 || autoAttackDamage <= 0 || autoAttackIntervalFrames <= 0 ||
                monsterAttackDamage <= 0 || monsterAttackIntervalFrames <= 0 || maxResource <= 0 || resourceRegenPerFrame < 0 ||
                moonBladeDamage <= 0 || moonBladeCooldownFrames <= 0 || moonBladeResourceCost <= 0 || moonlightGuId <= 0 ||
                string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(skillName)) throw new ArgumentException("Invalid moonlight battle rules.");
            PlayerMaxHp = playerMaxHp; MonsterMaxHp = monsterMaxHp; AutoAttackDamage = autoAttackDamage;
            AutoAttackIntervalFrames = autoAttackIntervalFrames; MonsterAttackDamage = monsterAttackDamage;
            MonsterAttackIntervalFrames = monsterAttackIntervalFrames; MaxResource = maxResource;
            ResourceRegenPerFrame = resourceRegenPerFrame; MoonBladeDamage = moonBladeDamage;
            MoonBladeCooldownFrames = moonBladeCooldownFrames; MoonBladeResourceCost = moonBladeResourceCost;
            MoonlightGuId = moonlightGuId; SkillId = skillId; SkillName = skillName;
        }

        public static MoonlightBattleRules Default => new MoonlightBattleRules(
            100, 100, 5, 20, 4, 30, 100, 1, 30, 60, 20, 1001, "moon-blade", "月刃");
    }

    public sealed class MoonlightBattleSnapshot
    {
        public long ActorId { get; }
        public long Frame { get; }
        public long LastSequence { get; }
        public int PlayerHp { get; }
        public int MonsterHp { get; }
        public int Resource { get; }
        public int MoonBladeCooldown { get; }
        public bool AutoCast { get; }
        public MoonlightBattlePhase Phase { get; }
        public MoonlightBattleRules Rules { get; }
        public ulong Hash { get; }

        public MoonlightBattleSnapshot(long actorId, long frame, long lastSequence, int playerHp, int monsterHp,
            int resource, int moonBladeCooldown, bool autoCast, MoonlightBattlePhase phase, MoonlightBattleRules rules, ulong hash)
        {
            if (actorId <= 0 || frame < -1 || lastSequence < -1 || playerHp < 0 || monsterHp < 0 || resource < 0 ||
                rules == null || moonBladeCooldown < 0 || moonBladeCooldown > rules.MoonBladeCooldownFrames) throw new ArgumentException("Invalid moonlight battle snapshot.");
            ActorId = actorId; Frame = frame; LastSequence = lastSequence; PlayerHp = playerHp; MonsterHp = monsterHp;
            Resource = resource; MoonBladeCooldown = moonBladeCooldown; AutoCast = autoCast; Phase = phase; Rules = rules; Hash = hash;
        }
    }

    public sealed class MoonlightBattleState
    {
        private readonly MoonlightBattleRules rules;
        public long ActorId { get; }
        public long Frame { get; private set; } = -1;
        public long LastSequence { get; private set; } = -1;
        public int PlayerHp { get; private set; }
        public int MonsterHp { get; private set; }
        public int Resource { get; private set; }
        public int MoonBladeCooldown { get; private set; }
        public bool AutoCast { get; private set; }
        public MoonlightBattlePhase Phase { get; private set; } = MoonlightBattlePhase.Active;
        public MoonlightBattleRules Rules => rules;
        public bool TryCastMoonBladeForValidation() => TryCastMoonBlade();

        public MoonlightBattleState(long actorId, MoonlightBattleRules rules = null)
        {
            if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId));
            ActorId = actorId; this.rules = rules ?? MoonlightBattleRules.Default;
            PlayerHp = this.rules.PlayerMaxHp; MonsterHp = this.rules.MonsterMaxHp; Resource = this.rules.MaxResource;
        }

        public MoonlightBattleSnapshot Capture() => new MoonlightBattleSnapshot(ActorId, Frame, LastSequence, PlayerHp, MonsterHp,
            Resource, MoonBladeCooldown, AutoCast, Phase, rules, StateHash);

        public static MoonlightBattleState Restore(MoonlightBattleSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var state = new MoonlightBattleState(snapshot.ActorId, snapshot.Rules)
            {
                Frame = snapshot.Frame, LastSequence = snapshot.LastSequence, PlayerHp = snapshot.PlayerHp,
                MonsterHp = snapshot.MonsterHp, Resource = snapshot.Resource, MoonBladeCooldown = snapshot.MoonBladeCooldown,
                AutoCast = snapshot.AutoCast, Phase = snapshot.Phase
            };
            if (state.StateHash != snapshot.Hash) throw new ArgumentException("Moonlight battle snapshot hash mismatch.");
            return state;
        }

        public bool Step(long frame, IReadOnlyList<MoonlightBattleCommand> commands)
        {
            if (Phase != MoonlightBattlePhase.Active || frame < 0 || Frame == long.MaxValue || frame != Frame + 1 || commands == null || commands.Count > 8) return false;
            var ordered = new List<MoonlightBattleCommand>(commands);
            ordered.Sort((a, b) => a.ActorId != b.ActorId ? a.ActorId.CompareTo(b.ActorId) : a.Sequence.CompareTo(b.Sequence));
            long previous = LastSequence;
            for (int i = 0; i < ordered.Count; i++)
            {
                var command = ordered[i];
                if (command.ActorId != ActorId || command.Sequence <= previous || command.Kind < MoonlightBattleCommandKind.CastMoonBlade || command.Kind > MoonlightBattleCommandKind.Retreat) return false;
                previous = command.Sequence;
            }
            if (Phase == MoonlightBattlePhase.Active)
            {
                if (Resource < rules.MaxResource) Resource = Math.Min(rules.MaxResource, Resource + rules.ResourceRegenPerFrame);
                if (MoonBladeCooldown > 0) MoonBladeCooldown--;
                foreach (var command in ordered)
                {
                    if (command.Kind == MoonlightBattleCommandKind.SetAutoCast) AutoCast = command.Enabled;
                    else if (command.Kind == MoonlightBattleCommandKind.Retreat) Phase = MoonlightBattlePhase.Retreated;
                    else if (command.Kind == MoonlightBattleCommandKind.CastMoonBlade) TryCastMoonBlade();
                }
                if (Phase == MoonlightBattlePhase.Active && AutoCast) TryCastMoonBlade();
                if (Phase == MoonlightBattlePhase.Active && frame % rules.AutoAttackIntervalFrames == 0) MonsterHp = Math.Max(0, MonsterHp - rules.AutoAttackDamage);
                if (Phase == MoonlightBattlePhase.Active && frame % rules.MonsterAttackIntervalFrames == 0) PlayerHp = Math.Max(0, PlayerHp - rules.MonsterAttackDamage);
                if (MonsterHp == 0) Phase = MoonlightBattlePhase.Victory;
                else if (PlayerHp == 0) Phase = MoonlightBattlePhase.Defeat;
            }
            LastSequence = previous; Frame = frame; return true;
        }

        private bool TryCastMoonBlade()
        {
            if (MoonBladeCooldown != 0 || Resource < rules.MoonBladeResourceCost || Phase != MoonlightBattlePhase.Active) return false;
            Resource -= rules.MoonBladeResourceCost; MoonBladeCooldown = rules.MoonBladeCooldownFrames;
            MonsterHp = Math.Max(0, MonsterHp - rules.MoonBladeDamage); return true;
        }

        public ulong StateHash
        {
            get
            {
                ulong hash = 14695981039346656037UL;
                Add(ref hash, ActorId); Add(ref hash, Frame); Add(ref hash, LastSequence); Add(ref hash, PlayerHp);
                Add(ref hash, MonsterHp); Add(ref hash, Resource); Add(ref hash, MoonBladeCooldown); Add(ref hash, AutoCast ? 1 : 0); Add(ref hash, (int)Phase);
                Add(ref hash, rules.PlayerMaxHp); Add(ref hash, rules.MonsterMaxHp); Add(ref hash, rules.MoonBladeDamage); Add(ref hash, rules.MoonBladeCooldownFrames); Add(ref hash, rules.MoonBladeResourceCost);
                return hash;
            }
        }
        private static void Add(ref ulong hash, long value)
        { unchecked { ulong bits = (ulong)value; for (int i = 0; i < 8; i++) { hash ^= (byte)bits; hash *= 1099511628211UL; bits >>= 8; } } }
    }
}
