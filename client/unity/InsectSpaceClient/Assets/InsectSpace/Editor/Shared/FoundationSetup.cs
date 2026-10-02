using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor.Settings;
using InsectSpace.Client;
using InsectSpace.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset.Editor;

namespace InsectSpace.Editor
{
    [InitializeOnLoad]
    public static class FoundationSetup
    {
#if UNITY_6000_0_OR_NEWER && !TUANJIE_2022_3_OR_NEWER
        public const string SceneExtension = ".unity";
#else
        public const string SceneExtension = ".scene";
#endif
        public const string ScenePath = "Assets/InsectSpace/Scenes/Bootstrap" + SceneExtension;
        static FoundationSetup()
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += PrepareIfMissing;
        }

        private static void PrepareIfMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += PrepareIfMissing;
                return;
            }
            if (!File.Exists(ScenePath) && !string.IsNullOrEmpty(SceneManager.GetActiveScene().path)) Prepare();
        }

        [MenuItem("InsectSpace/Foundation/Prepare Framework")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing framework settings.");
            Directory.CreateDirectory("Assets/InsectSpace/Content/Code");
            Directory.CreateDirectory("Assets/InsectSpace/Scenes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureCollectors();
            ConfigureHybridClr();
            CreateSceneIfMissing();
            ContentBuild.Prepare();
            var existing = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(existing).ToArray();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[InsectSpace] FOUNDATION_PREPARED");
        }

        [MenuItem("InsectSpace/Foundation/Open Bootstrap Scene")]
        public static void OpenBootstrap()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Prepare();
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static void ConfigureCollectors()
        {
            var settings = BundleCollectorSettingData.Setting;
            var package = settings.Packages.FirstOrDefault(p => p.PackageName == "Core");
            if (package == null)
            {
                package = new BundleCollectorPackage
                {
                    PackageName = "Core", PackageDesc = "Version-matched code and Luban data",
                    EnableAddressable = true, AutoCollectShaders = false,
                    IgnoreRuleName = nameof(RawFileIgnoreRule)
                };
                settings.Packages.Add(package);
            }
            AddGroup(package, "Tables", "Assets/InsectSpace/Content/Data", "tables");
            AddGroup(package, "Code", "Assets/InsectSpace/Content/Code", "code");
            BundleCollectorSettingData.SaveFile();
        }

        private static void AddGroup(BundleCollectorPackage package, string name, string path, string tag)
        {
            if (package.Groups.Any(g => g.GroupName == name)) return;
            var group = new BundleCollectorGroup { GroupName = name, AssetTags = tag };
            group.Collectors.Add(new BundleCollector
            {
                CollectPath = path, CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                PackRuleName = nameof(PackRawFile), AddressRuleName = nameof(AddressByFileName),
                FilterRuleName = nameof(CollectAll), AssetTags = tag
            });
            package.Groups.Add(group);
        }

        private static void ConfigureHybridClr()
        {
            var settings = HybridCLRSettings.Instance;
            // Configure only the new project, never install or replace native editor files.
            if (settings.hotUpdateAssemblies == null || settings.hotUpdateAssemblies.Length == 0)
                settings.hotUpdateAssemblies = new[] { HotUpdateLoader.AssemblyName };
            if (settings.patchAOTAssemblies == null || settings.patchAOTAssemblies.Length == 0)
                settings.patchAOTAssemblies = new[]
                {
                    "mscorlib", "System", "System.Core", "Framework.Core", "Framework.Deterministic",
                    "InsectSpace.Contracts", "InsectSpace.Foundation", "InsectSpace.Simulation", "InsectSpace.Network",
                    "InsectSpace.Client.Runtime", "InsectSpace.Luban.Runtime", "InsectSpace.Rendering",
                    "UnityEngine.CoreModule", "YooAsset"
                };
            if (!settings.patchAOTAssemblies.Contains("YooAsset"))
                settings.patchAOTAssemblies = settings.patchAOTAssemblies.Concat(new[] { "YooAsset" }).ToArray();
            settings.outputLinkFile = "InsectSpace/Generated/HybridCLR/link.xml";
            settings.outputAOTGenericReferenceFile = "InsectSpace/Generated/HybridCLR/AOTGenericReferences.cs";
            HybridCLRSettings.Save();
        }

        private static void CreateSceneIfMissing()
        {
            if (File.Exists(ScenePath)) return;
            Scene previous = SceneManager.GetActiveScene();
            bool single = Application.isBatchMode || !previous.IsValid() ||
                (string.IsNullOrEmpty(previous.path) && !previous.isDirty);
            if (!single && string.IsNullOrEmpty(previous.path))
                throw new InvalidOperationException("Save the untitled scene before preparing the framework.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, single ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                new GameObject("InsectSpace - Framework").AddComponent<InsectSpaceBootstrap>();
                var cameraObject = new GameObject("Isometric Camera");
                var camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(10, 13, -10);
                cameraObject.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 8;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.13f, 0.17f, 0.18f);
                cameraObject.AddComponent<AudioListener>();
                cameraObject.AddComponent<IsometricCameraRig>();
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(45, -30, 0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "World Blockout (not gameplay)";
                floor.transform.localScale = new Vector3(2, 1, 2);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = "Foundation Ground";
                material.color = new Color(0.40f, 0.53f, 0.43f);
                AssetDatabase.CreateAsset(material, "Assets/InsectSpace/Scenes/FoundationGround.mat");
                floor.GetComponent<Renderer>().sharedMaterial = material;
                for (int i = 0; i < 3; i++)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "Scale Marker " + i;
                    marker.transform.position = new Vector3(i * 3 - 3, 0.5f, 2);
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (!single)
                {
                    if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
