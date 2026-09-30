using System;
using System.IO;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
#if INSECTSPACE_WECHAT_SDK && TUANJIE_2022_3_OR_NEWER
using WeChatWASM;
#endif

namespace InsectSpace.Editor
{
    public static class WeChatBuild
    {
        internal static bool IsExporting { get; private set; }
        public static string OutputRoot => Path.Combine(SettingsUtil.HybridCLRDataDir, "MiniGameValidation", "WeChat");

        public static void ExportNative()
        {
#if INSECTSPACE_WECHAT_SDK && TUANJIE_2022_3_OR_NEWER
            if (!NativeValidationBuild.IsIsolatedProject() || !Application.isBatchMode ||
                Application.unityVersion != "2022.3.62t16" ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.MiniGame)
                throw new BuildFailedException("Use tools/Invoke-Unity.ps1 -Action WeChatNative with the pinned editor.");
            var sdk = WXConvertCore.config;
            string appId = Environment.GetEnvironmentVariable("INSECTSPACE_WECHAT_APPID") ?? "";
            string cdn = Environment.GetEnvironmentVariable("INSECTSPACE_WECHAT_CDN") ?? "";
            if (appId.Length != 0 && !Regex.IsMatch(appId, "^wx[0-9a-f]{16}$"))
                throw new BuildFailedException("Supply a real WeChat app ID or leave it unset for an unconfigured export.");
            if (cdn.Length != 0 && (!Uri.TryCreate(cdn, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0))
                throw new BuildFailedException("The SDK CDN must be an HTTPS deployment root without credentials, query or fragment.");
            sdk.ProjectConf.projectName = "InsectSpace.Validation";
            sdk.ProjectConf.Appid = appId;
            sdk.ProjectConf.DST = Path.GetFullPath(OutputRoot);
            sdk.ProjectConf.relativeDST = Path.GetRelativePath(SettingsUtil.ProjectDir, sdk.ProjectConf.DST);
            sdk.ProjectConf.CDN = cdn.TrimEnd('/');
            sdk.ProjectConf.assetLoadType = 1;
            sdk.CompileOptions.DevelopBuild = true;
            sdk.CompileOptions.AutoProfile = false;
            sdk.CompileOptions.ScriptOnly = false;
            sdk.CompileOptions.Webgl2 = true;
            sdk.CompileOptions.fbslim = false;
            sdk.CompileOptions.DeleteStreamingAssets = false;
            EditorUtility.SetDirty(sdk);
            AssetDatabase.SaveAssets();
            WXConvertCore.PreInit();
            MiniGameBuild.PrepareNative(true);
            IsExporting = true;
            WXConvertCore.WXExportError result;
            try { result = WXConvertCore.DoExport(); }
            finally { IsExporting = false; }
            if (result != WXConvertCore.WXExportError.SUCCEED)
                throw new BuildFailedException("WeChat SDK conversion failed: " + result);
            foreach (string file in new[] { "game.js", "game.json", "project.config.json", "webgl.wasm.framework.unityweb.js" })
                if (!File.Exists(Path.Combine(OutputRoot, "minigame", file)))
                    throw new BuildFailedException("Missing converted WeChat artifact: " + file);
            File.WriteAllText(Path.Combine(OutputRoot, "export-receipt.json"), JsonUtility.ToJson(new ExportReceipt
            {
                sdkCommit = "a09d4b29daa1dd8358b09b5b5639554ab08cfdc2",
                engine = Application.unityVersion,
                appIdConfigured = appId.Length != 0,
                resourceCdnConfigured = cdn.Length != 0
            }, true));
            Debug.Log("[InsectSpace] WECHAT_NATIVE_EXPORTED sdk=0.1.34 commit=a09d4b2 appIdConfigured=" + (appId.Length != 0));
            Debug.Log("[InsectSpace] Conversion only. Configure the app ID and resource deployment before SDK/device execution.");
#else
            throw new BuildFailedException("Enable the pinned WeChat SDK and use the supported Tuanjie editor.");
#endif
        }

        [Serializable]
        private sealed class ExportReceipt
        {
            public string sdkCommit;
            public string engine;
            public bool appIdConfigured;
            public bool resourceCdnConfigured;
        }

        public static bool IsValidationExport(string path, BuildTarget target, BuildOptions options,
            bool scoped, bool isolated, bool scriptsOnly)
        {
#if TUANJIE_2022_3_OR_NEWER
            if (!scoped || !isolated || scriptsOnly || target != BuildTarget.MiniGame ||
                (options & BuildOptions.Development) == 0 || string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(path).TrimEnd('\\', '/'),
                    Path.GetFullPath(Path.Combine(OutputRoot, "webgl")).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
#endif
            return false;
        }
    }
}
