using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace InsectSpace.Editor
{
    public static class PortableAssetIds
    {
        [Serializable] private sealed class Entry { public string path; public string storedGuid; public string guid; }
        [Serializable] private sealed class Report { public Entry[] entries; }

        public static void Export()
        {
            var entries = new List<Entry>();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !path.StartsWith("Packages/com.insectspace.", StringComparison.Ordinal)) continue;
                string physical = path;
                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                    if (package == null) continue;
                    physical = package.resolvedPath + path.Substring(package.assetPath.Length);
                }
                if (!File.Exists(physical + ".meta")) continue;
                var match = Regex.Match(File.ReadAllText(physical + ".meta"), @"(?m)^guid:\s*(\S+)");
                if (!match.Success) continue;
                entries.Add(new Entry { path = path, storedGuid = match.Groups[1].Value,
                    guid = AssetDatabase.AssetPathToGUID(path) });
            }
            string output = Path.GetFullPath("../../../.artifacts/validation/portable-asset-guids.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(new Report { entries = entries.ToArray() }, true));
            Debug.Log("[InsectSpace] PORTABLE_ASSET_IDS_EXPORTED count=" + entries.Count);
        }
    }
}
