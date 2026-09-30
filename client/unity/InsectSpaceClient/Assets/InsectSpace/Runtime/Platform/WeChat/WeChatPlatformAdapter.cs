#if INSECTSPACE_WECHAT_SDK && (UNITY_WEBGL || UNITY_EDITOR)
using System;
using System.Collections;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Network;
using UnityEngine;
using WeChatWASM;
using YooAsset;

namespace InsectSpace.Platform.WeChat
{
    public sealed class WeChatPlatformAdapter : IClientPlatformAdapter, IClientPlatformInitialization
    {
        private WeChatChannelFactory channels;
        private INetworkManager owner;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if !UNITY_EDITOR
            if (BootConfiguration.Load().useWeChatSdk)
                PlatformServices.Install(new WeChatPlatformAdapter());
#endif
        }

        public IEnumerator Initialize()
        {
            bool done = false;
            int result = -1;
            WX.InitSDK(code => { result = code; done = true; });
            float deadline = Time.realtimeSinceStartup + 30;
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;
            if (!done) throw new TimeoutException("WeChat SDK initialization timed out.");
            if (result != 0) throw new InvalidOperationException("WeChat SDK initialization failed: " + result);
            Debug.Log("[InsectSpace] WECHAT_SDK_READY transport=IPv4-TCP-KCP deviceValidation=pending");
        }

        public IPlatformChannelFactory CreateChannels(INetworkManager manager)
        {
            if (owner != null && !ReferenceEquals(owner, manager))
                throw new InvalidOperationException("The WeChat adapter already belongs to another network manager.");
            if (channels == null) { channels = new WeChatChannelFactory(manager); owner = manager; }
            return channels;
        }

        public IChannelAddressResolver CreateAddressResolver() => new WeChatAddressResolver();

        public InitializePackageOptions CreateResourceOptions(BootConfiguration configuration, string packageName, bool rawFiles) =>
            YooResourceService.CreateWebOptions(configuration, packageName, true);
    }
}
#endif
