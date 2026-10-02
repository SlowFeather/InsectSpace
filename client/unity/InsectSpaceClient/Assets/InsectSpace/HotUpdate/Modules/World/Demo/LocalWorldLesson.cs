using System.Collections.Generic;
using InsectSpace.Contracts;
using InsectSpace.Foundation;
using InsectSpace.Gameplay.Modules;
using InsectSpace.Rendering;
using UnityEngine;

namespace InsectSpace.Gameplay.Demo
{
    // An isolated local authority fixture exercises the actual AOI consumer and input boundary.
    public sealed class LocalWorldLesson : IWorldMovementSink
    {
        public SessionCoordinator Session { get; } = new SessionCoordinator();
        public WorldPresenceStore Presence { get; } = new WorldPresenceStore();
        public WorldMovementEmitter Movement { get; }
        public WorldMoveIntent Pending { get; private set; }
        public long Tick { get; private set; }
        public int MoveCount { get; private set; }
        private readonly List<WorldActorSnapshot> actors = new List<WorldActorSnapshot>();

        public LocalWorldLesson()
        {
            Movement = new WorldMovementEmitter(this);
            Session.Authenticated(1001);
            BindRoute(1);
        }

        private void BindRoute(long epoch)
        {
            var route = new WorldRoute { HomeRealmId = "demo-home", WorldClusterId = "demo-cluster",
                InstanceId = epoch % 2 == 1 ? "demo-garden-A" : "demo-garden-B", SceneId = "foundation_world", Epoch = epoch };
            Session.EnterWorld(route);
            Presence.Bind(route);
            Movement.Bind(route);
            Pending = null; Tick = 0; actors.Clear();
        }

        public void SwitchRoute() => BindRoute(Session.Route.Epoch + 1);
        public void Spawn()
        {
            actors.Clear();
            actors.Add(new WorldActorSnapshot { ActorId = 1001, AppearanceId = 1, XMillimeters = -1800 });
            actors.Add(new WorldActorSnapshot { ActorId = 1002, AppearanceId = 2, XMillimeters = 1800, ZMillimeters = 1000 });
            Apply();
        }
        public void RemovePeer() { actors.RemoveAll(a => a.ActorId == 1002); Apply(); }
        public void ChangeAppearance()
        {
            if (actors.Count == 0) Spawn();
            actors[0].AppearanceId = actors[0].AppearanceId == 1 ? 2 : 1;
            Apply();
        }
        public bool RequestMove() => Movement.TryMove(new Vector3(++MoveCount % 3 - 1, 0, MoveCount % 2 == 0 ? -2 : 2));
        public bool Submit(WorldMoveIntent intent) { Pending = intent; return true; }
        public bool ConfirmMove()
        {
            if (Pending == null || actors.Count == 0) return false;
            actors[0].XMillimeters = Pending.XMillimeters; actors[0].ZMillimeters = Pending.ZMillimeters;
            actors[0].FacingMilliDegrees = 90000;
            Pending = null; Apply(); return true;
        }
        private void Apply()
        {
            Tick++;
            foreach (var actor in actors) actor.ServerTick = Tick;
            var route = Session.Route;
            Presence.ApplyForSession(Session, route.InstanceId, route.Epoch, Tick, actors);
        }
        public bool TryStaleSnapshot()
        {
            var route = Session.Route;
            return Presence.ApplyForSession(Session, route.InstanceId, route.Epoch - 1, Tick + 100, actors);
        }
        public void SuspendMovement() => Movement.Suspend();
        public void Clear() { Presence.Clear(); Movement.Suspend(); Pending = null; actors.Clear(); }
    }
}
