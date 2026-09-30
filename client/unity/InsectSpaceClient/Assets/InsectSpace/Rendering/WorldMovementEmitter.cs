using System;
using InsectSpace.Contracts;
using UnityEngine;

namespace InsectSpace.Rendering
{
    // Touch, joystick, mouse and automatic exploration can share this intent boundary.
    public sealed class WorldMovementEmitter
    {
        private readonly IWorldMovementSink sink;
        private WorldRoute route;
        private long sequence;
        private long latestEpoch = -1;

        public WorldMovementEmitter(IWorldMovementSink sink) =>
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));

        public void Bind(WorldRoute serverRoute)
        {
            serverRoute.Validate();
            if (serverRoute.Epoch <= latestEpoch)
                throw new InvalidOperationException("Movement cannot rebind a stale route.");
            route = serverRoute.Copy();
            latestEpoch = serverRoute.Epoch;
            sequence = 0;
        }

        public void Suspend() => route = null;

        public bool TryMove(Vector3 destination)
        {
            if (route == null || sequence == long.MaxValue) return false;
            double x = Math.Round(destination.x * 1000d);
            double z = Math.Round(destination.z * 1000d);
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(z) || double.IsInfinity(z) ||
                x < int.MinValue || x > int.MaxValue || z < int.MinValue || z > int.MaxValue) return false;
            return sink.Submit(new WorldMoveIntent
            {
                InstanceId = route.InstanceId, RouteEpoch = route.Epoch, Sequence = ++sequence,
                XMillimeters = (int)x, ZMillimeters = (int)z
            });
        }
    }
}
