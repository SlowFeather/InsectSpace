using System.Collections.Generic;
using InsectSpace.Client;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class LobbyModule : ModuleBase
    {
        public override string Id => "lobby";
        public override IReadOnlyList<string> Dependencies => new[] { "platform" };
        public override void Start(ServiceRegistry services)
        {
            var context = services.Get<BootContext>();
            if (context.Configuration.localSmokeMode)
            {
                context.Log("LOCAL_SMOKE: no account authentication or backend connection.");
                context.Session.Authenticated(1);
            }
            // Online mode deliberately stays SignedOut until a real backend validates login.
        }
        public override void Stop() { }
    }
}
