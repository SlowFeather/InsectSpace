using System;
using System.IO;
using System.Linq;
using InsectSpace.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using YooAsset.Editor;

namespace InsectSpace.Editor
{
    public static class ContentBuild
    {
        private const string Root = "Assets/InsectSpace/Content/WorldCommon";

        internal static void Prepare()
        {
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string prefabPath = Root + "/WorldActor.prefab";
            string scenePath = Root + "/WorldSandbox" + FoundationSetup.SceneExtension;
            if (!File.Exists(prefabPath) || !File.Exists(scenePath))
            {
                Scene previous = SceneManager.GetActiveScene();
                bool single = Application.isBatchMode || !previous.IsValid() ||
                    (string.IsNullOrEmpty(previous.path) && !previous.isDirty);
                if (!single && string.IsNullOrEmpty(previous.path))
                    throw new InvalidOperationException("Save the untitled scene before preparing content assets.");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, single ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                try
                {
                    if (!File.Exists(prefabPath))
                    {
                        var actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        try
                        {
                            actor.name = "World Actor Placeholder";
                            actor.transform.position = Vector3.up;
                            actor.GetComponent<Renderer>().sharedMaterial =
                                AssetDatabase.LoadAssetAtPath<Material>("Assets/InsectSpace/Scenes/FoundationGround.mat");
                            PrefabUtility.SaveAsPrefabAsset(actor, prefabPath);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(actor); }
                    }
                    if (!File.Exists(scenePath))
                    {
                        new GameObject("World Content Placeholder");
                        EditorSceneManager.SaveScene(scene, scenePath);
                    }
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
            var packages = BundleCollectorSettingData.Setting.Packages;
            if (packages.All(p => p.PackageName != "WorldCommon"))
            {
                var package = new BundleCollectorPackage
                {
                    PackageName = "WorldCommon", PackageDesc = "Replaceable world framework assets",
                    EnableAddressable = true
                };
                var group = new BundleCollectorGroup { GroupName = "Foundation", AssetTags = "foundation" };
                group.Collectors.Add(new BundleCollector
                {
                    CollectPath = Root, CollectorGUID = AssetDatabase.AssetPathToGUID(Root),
                    PackRuleName = nameof(PackSeparately), AddressRuleName = nameof(AddressByFileName),
                    FilterRuleName = nameof(CollectAll)
                });
                package.Groups.Add(group);
                packages.Add(package);
                BundleCollectorSettingData.SaveFile();
            }
        }

        [MenuItem("InsectSpace/Build/Build Content Packages")]
        public static void BuildPackages()
        {
            FoundationSetup.Prepare();
            var config = BootConfiguration.Load();
            foreach (var entry in config.contentPackages)
            {
                entry.Validate(config.packageName);
                var result = new LegacyBuildPipeline().Run(new LegacyBuildParameters
                {
                    BuildOutputRoot = Environment.GetEnvironmentVariable("INSECTSPACE_BUILD_OUTPUT_ROOT")
                        ?? Path.GetFullPath("../../../.artifacts/yoo"),
                    BundledFileRoot = Path.Combine(Application.streamingAssetsPath, "yoo"),
                    BuildPipeline = nameof(LegacyBuildPipeline),
                    BuildBundleType = (int)EBundleType.AssetBundle,
                    BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                    PackageName = entry.name,
                    PackageVersion = entry.version,
                    CompressOption = ECompressOption.LZ4,
                    VerifyBuildingResult = true,
                    BundledCopyOption = EBundledCopyOption.ClearAndCopyAll
                }, true);
                if (!result.Success) throw new InvalidOperationException(result.ErrorInfo);
                Debug.Log("[InsectSpace] CONTENT_PACKAGE_BUILT " + result.OutputPackageDirectory);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
