using System;
using System.Collections.Generic;
using InsectSpace.Foundation;
using InsectSpace.Network;

namespace InsectSpace.Client
{
    public interface IHotUpdateApplication : IDisposable
    {
        string Status { get; }
        IReadOnlyList<string> Modules { get; }
        void Tick(float elapsedSeconds);
    }

    public sealed class BootContext : IDisposable
    {
        public BootConfiguration Configuration { get; }
        public IReadOnlyDictionary<string, byte[]> TableData { get; }
        public SessionCoordinator Session { get; } = new SessionCoordinator();
        public SessionConnections Connections { get; }
        public YooResourceService Resources { get; }
        public Action<string> Log { get; }

        public BootContext(BootConfiguration configuration, IReadOnlyDictionary<string, byte[]> tableData,
            Action<string> log, YooResourceService resources)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            TableData = tableData ?? throw new ArgumentNullException(nameof(tableData));
            Log = log ?? throw new ArgumentNullException(nameof(log));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Connections = PlatformServices.CreateSessionConnections(Session);
        }

        public void Dispose() => Connections.Dispose();
    }
}
