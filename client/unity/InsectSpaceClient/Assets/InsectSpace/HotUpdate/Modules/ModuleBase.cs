using System.Collections.Generic;
using InsectSpace.Foundation;

namespace InsectSpace.Gameplay.Modules
{
    internal abstract class ModuleBase : IGameModule
    {
        public abstract string Id { get; }
        public virtual IReadOnlyList<string> Dependencies => System.Array.Empty<string>();
        public virtual void Start(ServiceRegistry services) { }
        public virtual void Tick(float elapsedSeconds) { }
        public virtual void Stop() { }
    }
}
