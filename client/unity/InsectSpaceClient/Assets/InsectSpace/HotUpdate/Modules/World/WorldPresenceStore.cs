using System;
using System.Collections.Generic;
using System.Linq;
using InsectSpace.Contracts;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Modules
{
    // Full interest-set snapshots for now; delta replication is a later protocol change.
    public sealed class WorldPresenceStore : IWorldPresence
    {
        private readonly Dictionary<long, WorldActorSnapshot> actors = new Dictionary<long, WorldActorSnapshot>();
        private string instance;
        private long epoch = -1;
        private long tick = -1;
        public IReadOnlyCollection<WorldActorSnapshot> Actors => actors.Values.Select(Copy).ToArray();
        public event Action Changed;

        public void Clear()
        {
            instance = null;
            epoch = -1;
            tick = -1;
            actors.Clear();
            Changed?.Invoke();
        }

        public void Bind(WorldRoute route)
        {
            route.Validate();
            if (instance == route.InstanceId && epoch == route.Epoch) return;
            if (instance != null && route.Epoch <= epoch)
                throw new InvalidOperationException("Cannot bind a stale world route.");
            instance = route.InstanceId;
            epoch = route.Epoch;
            tick = -1;
            actors.Clear();
            Changed?.Invoke();
        }

        public bool ApplyForSession(SessionCoordinator session, string instanceId, long routeEpoch,
            long serverTick, IReadOnlyList<WorldActorSnapshot> snapshot)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.Phase != SessionPhase.World && session.Phase != SessionPhase.Battle &&
                session.Phase != SessionPhase.ConnectingBattle) return false;
            var route = session.Route;
            if (route == null || route.InstanceId != instanceId || route.Epoch != routeEpoch) return false;
            Bind(route);
            return Apply(instanceId, routeEpoch, serverTick, snapshot);
        }

        public bool Apply(string instanceId, long routeEpoch, long serverTick, IReadOnlyList<WorldActorSnapshot> snapshot)
        {
            if (instance == null || instanceId != instance || routeEpoch != epoch || serverTick <= tick) return false;
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var next = new Dictionary<long, WorldActorSnapshot>();
            foreach (var actor in snapshot)
            {
                if (actor == null || actor.ActorId <= 0 || next.ContainsKey(actor.ActorId))
                    throw new ArgumentException("Invalid or duplicate world actor.");
                next.Add(actor.ActorId, Copy(actor));
            }
            actors.Clear();
            foreach (var pair in next) actors.Add(pair.Key, pair.Value);
            tick = serverTick;
            Changed?.Invoke();
            return true;
        }

        private static WorldActorSnapshot Copy(WorldActorSnapshot actor) => new WorldActorSnapshot
        {
            ActorId = actor.ActorId, AppearanceId = actor.AppearanceId,
            XMillimeters = actor.XMillimeters, ZMillimeters = actor.ZMillimeters,
            FacingMilliDegrees = actor.FacingMilliDegrees, ServerTick = actor.ServerTick
        };
    }
}
