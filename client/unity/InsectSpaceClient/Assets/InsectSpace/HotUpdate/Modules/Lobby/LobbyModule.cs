using System.Collections.Generic;
using InsectSpace.Client;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class LobbyModule : ModuleBase
    {
        private BackendLobbyPanel panel;
        public override string Id => "lobby";
        public override IReadOnlyList<string> Dependencies => new[] { "platform" };
        public override void Start(ServiceRegistry services)
        {
            var context = services.Get<BootContext>();
            if (context.Configuration.localSmokeMode)
            {
                context.Log("LOCAL_SMOKE: no account authentication or backend connection.");
                context.Session.Authenticated(1);
                return;
            }
            panel = new UnityEngine.GameObject("InsectSpace Backend Lobby UI").AddComponent<BackendLobbyPanel>();
            UnityEngine.Object.DontDestroyOnLoad(panel.gameObject);
            panel.Initialize(context);
        }
        public override void Stop()
        {
            if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
            panel = null;
        }
    }
}
