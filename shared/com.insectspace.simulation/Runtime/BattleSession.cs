using System;
using System.Collections.Generic;
using Framework.Deterministic;
using InsectSpace.Contracts;

namespace InsectSpace.Simulation
{
    public interface IFrameInputSource : IDisposable
    {
        bool TryRead(long frameId, out IReadOnlyList<ISimulationCommand> commands);
    }

    // This empty local source is for framework validation, not gameplay AI.
    public sealed class EmptyLocalInputSource : IFrameInputSource
    {
        public bool TryRead(long frameId, out IReadOnlyList<ISimulationCommand> commands)
        {
            commands = Array.Empty<ISimulationCommand>();
            return true;
        }
        public void Dispose() { }
    }

    public sealed class OrderedFrameInbox : IFrameInputSource
    {
        private readonly SortedDictionary<long, IReadOnlyList<ISimulationCommand>> frames =
            new SortedDictionary<long, IReadOnlyList<ISimulationCommand>>();
        private readonly int capacity;
        private long nextFrame;
        private bool disposed;

        public OrderedFrameInbox(int capacity = 128)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public bool Push(long frameId, IReadOnlyList<ISimulationCommand> commands)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OrderedFrameInbox));
            if (frameId < nextFrame || frameId - nextFrame >= capacity || frames.ContainsKey(frameId)) return false;
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            var copy = new ISimulationCommand[commands.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                if (commands[i] == null || commands[i].FrameId != frameId)
                    throw new ArgumentException("A command is null or belongs to a different frame.");
                copy[i] = commands[i];
            }
            frames.Add(frameId, copy);
            return true;
        }

        public bool TryRead(long frameId, out IReadOnlyList<ISimulationCommand> commands)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OrderedFrameInbox));
            if (frameId != nextFrame) throw new InvalidOperationException("Frames must be consumed in order.");
            if (!frames.TryGetValue(frameId, out commands)) return false;
            frames.Remove(frameId);
            nextFrame++;
            return true;
        }

        public void Dispose() { disposed = true; frames.Clear(); }
    }

    public sealed class BattleSession : IDisposable
    {
        private readonly ISimulationWorld world;
        private readonly IFrameInputSource input;
        private bool disposed;
        private bool faulted;
        public long Frame => world.FrameId;
        public ulong StateHash => world.ComputeHash();

        public BattleSession(ISimulationWorld world, IFrameInputSource input)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
        }

        // Rendering decides when to pump. Simulation only sees integer frame IDs.
        public bool TryAdvance()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleSession));
            if (faulted) throw new InvalidOperationException("Faulted simulation requires a new authoritative session.");
            long nextFrame = world.FrameId + 1;
            if (!input.TryRead(nextFrame, out var commands)) return false;
            try { world.Step(nextFrame, commands); }
            catch { faulted = true; throw; }
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            input.Dispose();
        }

        public static BattleSession CreateSmokeSession(IFrameInputSource input)
        {
            var world = new FrameSimulationWorld(FP64.FromInt(1) / FP64.FromInt(ProtocolVersion.BattleFramesPerSecond),
                Array.Empty<IFrameSimulationSystem>(), new EmptyState());
            return new BattleSession(world, input);
        }

        private sealed class EmptyState : ISimulationStateStore
        {
            public ulong ComputeHash() => StableHash64.Offset;
            public ISimulationSnapshot Capture(long frameId) => new EmptySnapshot(frameId);
            public void Restore(ISimulationSnapshot snapshot)
            {
                if (!(snapshot is EmptySnapshot) || snapshot.FormatVersion != 1)
                    throw new ArgumentException("Incompatible smoke snapshot.");
            }
        }

        private sealed class EmptySnapshot : ISimulationSnapshot
        {
            public long FrameId { get; }
            public int FormatVersion => 1;
            public ulong StateHash => StableHash64.Offset;
            public EmptySnapshot(long frameId) { FrameId = frameId; }
        }
    }
}
