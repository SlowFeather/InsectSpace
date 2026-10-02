using System;
using System.IO;
using InsectSpace.Client;
using InsectSpace.Editor;
using NUnit.Framework;
using UnityEditor;

namespace InsectSpace.Tests
{
    public sealed class WebDevelopmentTests
    {
        private static BootConfiguration LocalWeb() => new BootConfiguration
        {
            webDevelopment = true, editorSimulate = false, localSmokeMode = true,
            useWeChatSdk = false, resourceMode = ResourceMode.Offline, discoverRemoteVersion = false
        };

        [Test]
        public void WebDevelopmentNeedsItsOwnRuntimeBuild()
        {
            var config = LocalWeb();
            Assert.DoesNotThrow(config.ValidateWebDevelopment);
            Assert.Throws<InvalidOperationException>(config.Validate);
        }

        [TestCase("disabled")]
        [TestCase("editor")]
        [TestCase("online")]
        [TestCase("wechat")]
        [TestCase("resource-mode")]
        [TestCase("discovery")]
        [TestCase("primary")]
        [TestCase("fallback")]
        public void WebDevelopmentCannotHideAnOnlineOrPlatformConfiguration(string change)
        {
            var config = LocalWeb();
            switch (change)
            {
                case "disabled": config.webDevelopment = false; break;
                case "editor": config.editorSimulate = true; break;
                case "online": config.localSmokeMode = false; break;
                case "wechat": config.useWeChatSdk = true; break;
                case "resource-mode": config.resourceMode = ResourceMode.Web; break;
                case "discovery": config.discoverRemoteVersion = true; break;
                case "primary": config.remoteRoot = "https://example.invalid"; break;
                case "fallback": config.fallbackRoot = "https://example.invalid"; break;
            }
            Assert.Throws<InvalidOperationException>(config.ValidateWebDevelopment);
        }

        [Test]
        public void OnlyScopedIsolatedDevelopmentWebBuildGetsTheException()
        {
            Assert.IsTrue(WebDevelopmentBuild.IsDevelopmentBuild(WebDevelopmentBuild.OutputRoot,
                BuildTarget.WebGL, BuildOptions.Development, true, true, false));
            foreach (string path in new[] { null, "", WebDevelopmentBuild.OutputRoot + "-release",
                Path.Combine(WebDevelopmentBuild.OutputRoot, "child"),
                Path.Combine(WebDevelopmentBuild.OutputRoot, "..", "Release") })
                Assert.IsFalse(WebDevelopmentBuild.IsDevelopmentBuild(path, BuildTarget.WebGL,
                    BuildOptions.Development, true, true, false));
            Assert.IsFalse(WebDevelopmentBuild.IsDevelopmentBuild(WebDevelopmentBuild.OutputRoot,
                BuildTarget.StandaloneWindows64, BuildOptions.Development, true, true, false));
        }

        [TestCase(false, true, false, BuildOptions.Development)]
        [TestCase(true, false, false, BuildOptions.Development)]
        [TestCase(true, true, true, BuildOptions.Development)]
        [TestCase(true, true, false, BuildOptions.None)]
        public void WebDevelopmentCannotGrantProductionApproval(bool scoped, bool isolated, bool scriptsOnly, BuildOptions options)
        {
            Assert.IsFalse(WebDevelopmentBuild.IsDevelopmentBuild(WebDevelopmentBuild.OutputRoot,
                BuildTarget.WebGL, options, scoped, isolated, scriptsOnly));
        }
    }
}
