using System;
using System.Collections.Generic;
using InsectSpace.Client;
using InsectSpace.Foundation;
using InsectSpace.Rendering;
using InsectSpace.Gameplay.Modules;
using Luban;
using UnityEngine.Scripting;
using InsectSpace.Contracts;

namespace InsectSpace.Gameplay
{
    [Preserve]
    public static class HotUpdateEntry
    {
        [Preserve]
        public static IHotUpdateApplication Create(BootContext context)
        {
#if DEVELOPMENT_BUILD && ENABLE_IL2CPP && !UNITY_EDITOR && UNITY_STANDALONE_WIN
            BackendNativeCacheValidation.RunWhenRequested();
#endif
            return new GameplayApplication(context);
        }
    }

    internal sealed class GameplayApplication : IHotUpdateApplication
    {
        private readonly ModuleHost host = new ModuleHost();
        private readonly BootContext context;
        public string Status => context.Configuration.localSmokeMode ? "LOCAL FOUNDATION / " + context.Session.Phase : context.Session.Phase.ToString();
        public IReadOnlyList<string> Modules => host.ActiveModuleIds;

        public GameplayApplication(BootContext context)
        {
            this.context = context;
#if INSECTSPACE_NATIVE_PATCH
            context.Log("GAMEPLAY_REVISION native-patch-002");
#else
            context.Log("GAMEPLAY_REVISION foundation-001");
#endif
            var tables = new Config.Tables(name =>
            {
                if (!context.TableData.TryGetValue(name, out var bytes))
                    throw new InvalidOperationException("Missing generated table: " + name);
                return new ByteBuf(bytes);
            });
            var services = new ServiceRegistry();
            services.Add(context);
            services.Add(context.Session);
            services.Add(context.Connections);
            var presence = new WorldPresenceStore();
            services.Add(presence);
            services.Add<IWorldPresence>(presence);
            services.Add(tables);
            var quality = tables.TbQualityProfile.Get((int)context.Configuration.quality);
            QualityController.Apply(context.Configuration.quality, quality.TargetFps);
            host.Start(new IGameModule[]
            {
                new ConfigModule(), new PlatformModule(), new LobbyModule(), new PlayerModule(),
                new WorldModule(), new BattleModule(), new ContentModule(), new PresentationModule()
            }, services);
        }

        public void Tick(float elapsedSeconds) => host.Tick(elapsedSeconds);
        public void Dispose() => host.Dispose();
    }
}
