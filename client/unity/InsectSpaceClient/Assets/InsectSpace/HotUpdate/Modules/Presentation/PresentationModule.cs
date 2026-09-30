using System.Collections.Generic;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class PresentationModule : ModuleBase
    {
        public override string Id => "presentation";
        public override IReadOnlyList<string> Dependencies => new[] { "world", "battle", "content" };
    }
}
