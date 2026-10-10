using System;
using System.Collections.Generic;
using InsectSpace.Economy;

namespace InsectSpace.BattleEconomy
{
    // Bounded ordered inbox: missing authority frames wait; corrupt frames never partially mutate state.
    public sealed class BattleResourceReplica
    {
        private BattleResources state;
        private readonly SortedDictionary<long, BattleResourceMessage> inbox = new SortedDictionary<long, BattleResourceMessage>();
        public string RoomId { get; }
        public BattleResourceSnapshot Snapshot => state.Capture();
        public bool Finished { get; private set; }
        public int BufferedFrames => inbox.Count;
        public BattleResourceReplica(string roomId, BattleResourceSnapshot checkpoint)
        {
            if (!EconomyPacketCodec.ValidId(roomId)) throw new ArgumentException("Invalid room.");
            RoomId = roomId; state = BattleResources.Restore(checkpoint);
        }
        public bool Receive(BattleResourceMessage frame)
        {
            if (frame == null || frame.RoomId != RoomId || frame.State == null || frame.State.ActorId != state.ActorId ||
                (frame.Kind != BattleMessageKind.Frame && frame.Kind != BattleMessageKind.Finished) || !BattleResourceCodec.Valid(frame)) return false;
            if (frame.State.Frame <= state.Frame) return true; // Old delivery cannot mutate or consume again.
            if (Finished || frame.State.Frame > state.Frame + 256) return false;
            if (inbox.TryGetValue(frame.State.Frame, out var previous))
                return previous.State.Hash == frame.State.Hash && previous.Sequence == frame.Sequence && previous.Action == frame.Action && previous.Kind == frame.Kind;
            if (inbox.Count >= 256) return false;
            inbox.Add(frame.State.Frame, frame);
            while (inbox.TryGetValue(state.Frame + 1, out var next))
            {
                var candidate = BattleResources.Restore(state.Capture());
                var commands = next.Action == 0 ? Array.Empty<ResourceCommand>() :
                    new[] { new ResourceCommand(state.ActorId, next.Sequence, next.Action) };
                if (!candidate.ApplyFrame(next.State.Frame, commands) || candidate.StateHash != next.State.Hash ||
                    candidate.Essence != next.State.Essence || candidate.Capacity != next.State.Capacity ||
                    !candidate.Remaining.Equals(next.State.Remaining) || candidate.LastSequence != next.State.LastSequence ||
                    candidate.Capture().Rules.SkillCost != next.State.Rules.SkillCost ||
                    candidate.Capture().Rules.YuanShiRecovery != next.State.Rules.YuanShiRecovery ||
                    candidate.Capture().Rules.XianYuanShiRecovery != next.State.Rules.XianYuanShiRecovery) return false;
                state = candidate; inbox.Remove(state.Frame);
                if (next.Kind == BattleMessageKind.Finished) { Finished = true; inbox.Clear(); break; }
            }
            return true;
        }
    }
}
