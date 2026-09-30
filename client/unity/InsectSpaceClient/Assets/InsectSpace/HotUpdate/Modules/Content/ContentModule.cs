using System.Collections.Generic;

namespace InsectSpace.Gameplay.Modules
{
    // Scene/story/quest/reward presentation is data-driven. Rewards remain server-owned.
    internal sealed class ContentModule : ModuleBase
    {
        public override string Id => "content";
        public override IReadOnlyList<string> Dependencies => new[] { "world", "config" };
    }
}
