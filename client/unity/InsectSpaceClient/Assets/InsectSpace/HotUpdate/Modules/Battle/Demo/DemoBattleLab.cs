using System;
using System.Collections.Generic;
using Framework.Deterministic;
using InsectSpace.Simulation;

namespace InsectSpace.Gameplay.Demo
{
    // LOCAL TEACHING FIXTURE. Integer commands, GF ordering, FP64 clock and real BattleSession.
    // No Unity, physics, platform RNG or presentation clock enters this state machine.
    public sealed class DemoBattleLab : IDisposable
    {
        private sealed class Strike : ISimulationCommand
        {
            public long FrameId { get; }
            public long ActorId { get; }
            public long Sequence => FrameId;
            public Strike(long frame, long actor) { FrameId = frame; ActorId = actor; }
        }

        private sealed class DuelState : ISimulationStateStore
        {
            public int PlayerHp = 100;
            public int OpponentHp = 100;
            public int Combo;
            public ulong ComputeHash()
            {
                var hash = StableHash64.Add(StableHash64.Offset, FP64.FromInt(PlayerHp));
                hash = StableHash64.Add(hash, FP64.FromInt(OpponentHp));
                return StableHash64.Add(hash, FP64.FromInt(Combo));
            }
            public ISimulationSnapshot Capture(long frameId) => new Snapshot(frameId, this);
            public void Restore(ISimulationSnapshot value)
            {
                if (!(value is Snapshot snapshot) || snapshot.FormatVersion != 1)
                    throw new ArgumentException("Incompatible demo snapshot.");
                PlayerHp = snapshot.PlayerHp;
                OpponentHp = snapshot.OpponentHp;
                Combo = snapshot.Combo;
            }
        }

        private sealed class Snapshot : ISimulationSnapshot
        {
            public long FrameId { get; }
            public int FormatVersion => 1;
            public ulong StateHash { get; }
            public readonly int PlayerHp, OpponentHp, Combo;
            public Snapshot(long frame, DuelState state)
            {
                FrameId = frame; StateHash = state.ComputeHash();
                PlayerHp = state.PlayerHp; OpponentHp = state.OpponentHp; Combo = state.Combo;
            }
        }

        private sealed class DuelSystem : IFrameSimulationSystem
        {
            private readonly DuelState state;
            public SimulationPhase Phase => SimulationPhase.ApplyCommands;
            public int Order => 0;
            public DuelSystem(DuelState state) { this.state = state; }
            public void Execute(FrameSimulationContext context)
            {
                foreach (var command in context.Commands)
                {
                    if (state.PlayerHp == 0 || state.OpponentHp == 0) break;
                    // Deliberately order-sensitive: proves GF's canonical command order matters.
                    state.Combo = (state.Combo * 3 + (int)command.ActorId) % 7;
                    if (command.ActorId == 1) state.OpponentHp = Math.Max(0, state.OpponentHp - 7 - state.Combo);
                    else state.PlayerHp = Math.Max(0, state.PlayerHp - 4 - state.Combo);
                }
            }
        }

        private sealed class LocalSource : IFrameInputSource
        {
            public bool TryRead(long frame, out IReadOnlyList<ISimulationCommand> commands)
            { commands = Commands(frame, false); return true; }
            public void Dispose() { }
        }

        private DuelState state;
        private BattleSession session;
        private OrderedFrameInbox inbox;
        private readonly List<ulong> hashes = new List<ulong>();
        public bool OrderedMode { get; private set; }
        public long Frame => session.Frame;
        public ulong Hash => session.StateHash;
        public int PlayerHp => state.PlayerHp;
        public int OpponentHp => state.OpponentHp;
        public int RecordedFrames => hashes.Count;
        public bool Finished => PlayerHp == 0 || OpponentHp == 0;

        public DemoBattleLab() { Reset(false); }

        public void Reset(bool ordered)
        {
            session?.Dispose();
            hashes.Clear();
            OrderedMode = ordered;
            state = new DuelState();
            inbox = ordered ? new OrderedFrameInbox(128) : null;
            session = Create(state, ordered ? (IFrameInputSource)inbox : new LocalSource());
        }

        public bool Push(long frame) => inbox != null && inbox.Push(frame, Commands(frame, true));

        public bool Step()
        {
            if (Finished || !session.TryAdvance()) return false;
            hashes.Add(session.StateHash);
            return true;
        }

        public bool VerifyReplay(out int verifiedFrames)
        {
            verifiedFrames = 0;
            var replayState = new DuelState();
            using (var replay = Create(replayState, new LocalSource()))
                foreach (var hash in hashes)
                {
                    if (!replay.TryAdvance() || replay.StateHash != hash) return false;
                    verifiedFrames++;
                }
            return true;
        }

        private static BattleSession Create(DuelState state, IFrameInputSource source) =>
            new BattleSession(new FrameSimulationWorld(FP64.FromInt(1) / FP64.FromInt(20),
                new IFrameSimulationSystem[] { new DuelSystem(state) }, state), source);

        private static ISimulationCommand[] Commands(long frame, bool reverse) => reverse
            ? new ISimulationCommand[] { new Strike(frame, 2), new Strike(frame, 1) }
            : new ISimulationCommand[] { new Strike(frame, 1), new Strike(frame, 2) };

        public void Dispose() => session?.Dispose();
    }
}
