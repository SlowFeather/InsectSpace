using Framework;
using Framework.Network;
using InsectSpace.Foundation;
using InsectSpace.Network;
using System;
using System.Collections;
using YooAsset;

namespace InsectSpace.Client
{
    // Install from an AOT platform assembly before the Bootstrap scene starts.
    public interface IClientPlatformAdapter
    {
        IPlatformChannelFactory CreateChannels(INetworkManager manager);
        IChannelAddressResolver CreateAddressResolver();
        InitializePackageOptions CreateResourceOptions(BootConfiguration configuration, string packageName, bool rawFiles);
    }

    public interface IClientPlatformInitialization
    {
        IEnumerator Initialize();
    }

    public static class PlatformServices
    {
        private static IClientPlatformAdapter adapter;
        private static bool started;

        public static IEnumerator Initialize(bool requireWeChat)
        {
            started = true;
            if (requireWeChat && adapter == null)
                throw new InvalidOperationException("WeChat startup requires its AOT platform adapter.");
            if (adapter is IClientPlatformInitialization initialization)
                yield return initialization.Initialize();
        }

        public static void Install(IClientPlatformAdapter platformAdapter)
        {
            if (platformAdapter == null) throw new ArgumentNullException(nameof(platformAdapter));
            if (started || adapter != null)
                throw new InvalidOperationException("Install one platform adapter before framework startup.");
            adapter = platformAdapter;
        }

        public static YooResourceService CreateResources()
        {
            started = true;
            return adapter == null ? new YooResourceService() : new YooResourceService(adapter.CreateResourceOptions);
        }

        public static SessionConnections CreateSessionConnections(SessionCoordinator session)
        {
            started = true;
            var manager = GameFrameworkEntry.GetModule<INetworkManager>();
            if (adapter != null)
                return new SessionConnections(session, manager, adapter.CreateChannels(manager),
                    adapter.CreateAddressResolver(), () => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
#if UNITY_EDITOR || UNITY_STANDALONE
            IChannelAddressResolver resolver = new DesktopAddressResolver();
#else
            IChannelAddressResolver resolver = new UnconfiguredMiniGameAddressResolver();
#endif
            return new SessionConnections(session, GameFrameworkEntry.GetModule<INetworkManager>(),
                CreateChannelFactory(), resolver, () => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        public static IPlatformChannelFactory CreateChannelFactory()
        {
            started = true;
            if (adapter != null) return adapter.CreateChannels(GameFrameworkEntry.GetModule<INetworkManager>());
#if UNITY_EDITOR || UNITY_STANDALONE
            return new DesktopChannelFactory(GameFrameworkEntry.GetModule<INetworkManager>());
#else
            return new UnconfiguredMiniGameChannelFactory();
#endif
        }
    }
}
