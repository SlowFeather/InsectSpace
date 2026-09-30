using System.Collections.Generic;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Modules
{
    internal sealed class ConfigModule : ModuleBase
    {
        public override string Id => "config";
        public override void Start(ServiceRegistry services)
        {
            if (services.Get<Config.Tables>().TbWorldScene.DataList.Count == 0)
                throw new System.InvalidOperationException("World scene table is empty.");
        }
    }

    internal sealed class PlatformModule : ModuleBase
    {
        public override string Id => "platform";
        public override IReadOnlyList<string> Dependencies => new[] { "config" };
    }
}
