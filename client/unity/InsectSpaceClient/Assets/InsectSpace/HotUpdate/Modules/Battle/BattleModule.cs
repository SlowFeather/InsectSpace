using System.Collections.Generic;
using InsectSpace.Client;
using InsectSpace.Foundation;
using InsectSpace.Simulation;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class BattleModule : ModuleBase
    {
        public override string Id => "battle";
        public override IReadOnlyList<string> Dependencies => new[] { "world", "config" };
        public override void Start(ServiceRegistry services)
        {
            var context = services.Get<BootContext>();
            if (!context.Configuration.localSmokeMode) return;
            context.Connections.BeginLocalEncounter();
            using (var battle = BattleSession.CreateSmokeSession(new EmptyLocalInputSource()))
            {
                for (int i = 0; i < 20; i++) battle.TryAdvance();
                context.Log("Deterministic smoke frames=" + (battle.Frame + 1) + " hash=" + battle.StateHash);
            }
            context.Connections.CompleteLocalBattle();
        }
    }
}
