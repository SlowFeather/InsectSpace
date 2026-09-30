using System.Collections.Generic;
using InsectSpace.Client;
using InsectSpace.Contracts;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class WorldModule : ModuleBase
    {
        private BootContext context;
        private WorldPresenceStore presence;
        private WorldRoute currentRoute;
        public override string Id => "world";
        public override IReadOnlyList<string> Dependencies => new[] { "player" };
        public override void Start(ServiceRegistry services)
        {
            context = services.Get<BootContext>();
            presence = services.Get<WorldPresenceStore>();
            if (context.Configuration.localSmokeMode)
                context.Session.EnterWorld(new WorldRoute
                {
                    HomeRealmId = "local-realm", WorldClusterId = "local-cluster",
                    InstanceId = "local-smoke", SceneId = "foundation_world", Epoch = 1
                });
            Tick(0);
        }

        public override void Tick(float elapsedSeconds)
        {
            var route = context.Session.Route;
            if (route == null)
            {
                if (currentRoute != null) presence.Clear();
                currentRoute = null;
            }
            else if (currentRoute == null || route.Epoch != currentRoute.Epoch || route.InstanceId != currentRoute.InstanceId)
            {
                presence.Bind(route);
                currentRoute = route;
            }
        }

        public override void Stop()
        {
            presence?.Clear();
            presence = null;
            context = null;
            currentRoute = null;
        }
    }
}
