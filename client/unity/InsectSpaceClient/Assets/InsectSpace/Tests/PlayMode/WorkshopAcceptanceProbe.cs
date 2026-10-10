#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Framework;
using Framework.Network;
using InsectSpace.Client;
using InsectSpace.Gameplay.Economy;
using InsectSpace.GuPaths;
using InsectSpace.GuWorkshop;
using UnityEngine;

namespace InsectSpace.Tests
{
    // Test assembly only. Explicitly launched by Test-WorkshopDemo.ps1, never by gameplay.
    public sealed class WorkshopAcceptanceProbe : MonoBehaviour
    {
        public string FixtureDirectory;
        public int Port;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public WorkshopCatalog Catalog { get; private set; }
        public LocalEconomyTcpClient Transport { get; private set; }
        public InsectSpace.Gameplay.GuWorkshop.WorkshopClient Client => Transport?.Workshop;
        private InsectSpaceBootstrap boot;
        private bool ownsBoot;

        private IEnumerator Start()
        {
            try
            {
                var configuration = BootConfiguration.Load(); configuration.Validate();
                if (!configuration.editorSimulate || !configuration.localSmokeMode || configuration.useWeChatSdk)
                    throw new InvalidOperationException("Acceptance requires explicit LOCAL SMOKE.");
                if (File.ReadAllText(Path.Combine(FixtureDirectory, "TEST_ONLY.txt")).Trim() != "WORKSHOP_TEST_FIXTURE")
                    throw new InvalidDataException("Explicit fixture marker required.");
                byte[] GuBytes(string name)
                {
                    var asset = Resources.Load<TextAsset>("LocalGuPaths/" + name);
                    if (asset == null) throw new InvalidDataException("Missing Gu catalog.");
                    var bytes = asset.bytes; Resources.UnloadAsset(asset); return bytes;
                }
                Catalog = new WorkshopCatalog(new GuCatalog(GuBytes), name => File.ReadAllBytes(Path.Combine(FixtureDirectory, name + ".bytes")));
            }
            catch (Exception e) { Error = e.Message; }
            if (Error != null) yield break;
            boot = FindObjectOfType<InsectSpaceBootstrap>();
            if (boot == null) { ownsBoot = true; boot = new GameObject("Workshop acceptance bootstrap - TEST ONLY").AddComponent<InsectSpaceBootstrap>(); }
            float deadline = Time.realtimeSinceStartup + 45;
            while (!boot.Ready && boot.LastError == null && Time.realtimeSinceStartup < deadline) yield return null;
            if (!boot.Ready) { Error = boot.LastError ?? "Bootstrap timeout"; yield break; }
            try
            {
                Transport = new LocalEconomyTcpClient(GameFrameworkEntry.GetModule<INetworkManager>(), enableCultivation: true, workshopCatalog: Catalog);
                Transport.Connect(Port); Ready = true;
            }
            catch (Exception e) { Error = e.Message; }
        }
        private void Update() { if (Ready) Transport.Tick(Time.unscaledDeltaTime); }
        private void OnGUI()
        {
            GUI.depth = -100;
            GUI.Box(new Rect(0, 0, Screen.width, 40), "TEST ONLY / WORKSHOP TCP ACCEPTANCE / synthetic rules");
        }
        private void OnDestroy()
        {
            Ready = false; Transport?.Dispose();
            if (ownsBoot && boot != null) Destroy(boot.gameObject);
        }
    }
}
#endif
