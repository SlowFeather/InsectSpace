using System.Collections.Generic;

namespace InsectSpace.Gameplay.Modules
{
    // Owns character/equipment/Gu read models. No authoritative stat or reward logic here.
    internal sealed class PlayerModule : ModuleBase
    {
        public override string Id => "player";
        public override IReadOnlyList<string> Dependencies => new[] { "lobby", "config" };
    }
}
