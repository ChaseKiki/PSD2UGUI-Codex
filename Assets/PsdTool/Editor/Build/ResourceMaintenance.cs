using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEditor.U2D;

namespace PsdTool
{
    internal sealed class ResourceUsage
    {
        public GeneratedResource record;
        public string path;
        public readonly List<string> references = new List<string>();
        public bool CanRecycle => string.IsNullOrEmpty(record.restorePath) && !string.IsNullOrEmpty(path) && references.Count == 0;
    }

    internal static class ResourceMaintenance
    {
        public static List<ResourceUsage> Scan(PsdToolSettings settings)
        {
            var catalog = ResourceCatalog.Load(settings);
            var result = catalog.resources.Select(r => new ResourceUsage { record = r, path = AssetDatabase.GUIDToAssetPath(r.guid) }).ToList();
            var byPath = result.Where(r => !string.IsNullOrEmpty(r.path)).ToDictionary(r => r.path, StringComparer.OrdinalIgnoreCase);
            var byGuid = result.ToDictionary(r => r.record.guid);
            var trees = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:PsdTree"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); trees.Add(path);
                var tree = AssetDatabase.LoadAssetAtPath<PsdTree>(path);
                catalog.SyncBindings(tree);
                foreach (var node in tree.nodes.Where(n => n.status != NodeStatus.PendingDelete))
                {
                    string used = node.resourceOverride != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(node.resourceOverride)) : node.image.assetGuid;
                    if (!string.IsNullOrEmpty(used) && byGuid.TryGetValue(used, out var row)) row.references.Add(path + " / " + node.goName);
                }
            }
            if (catalog.bindings.RemoveAll(b => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(b.sourceGuid))) > 0) EditorUtility.SetDirty(catalog);
            // 已管理图集的显式图片列表会在回收时同步移除；其他图集仍是普通引用者。
            var managedAtlases = new HashSet<string>(catalog.atlasGuids.Select(AssetDatabase.GUIDToAssetPath));
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path) || trees.Contains(path)) continue;
                foreach (string dependency in AssetDatabase.GetDependencies(path, false))
                {
                    if (dependency == path || !byPath.TryGetValue(dependency, out var row)) continue;
                    if (managedAtlases.Contains(path))
                    {
                        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                        if (atlas != null && !atlas.GetPackables().Any(p => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(p)))) continue;
                    }
                    row.references.Add(path);
                }
            }
            foreach (var row in result)
                if (row.path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 || row.path.IndexOf("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) >= 0)
                    row.references.Add("运行时按路径加载目录：需人工迁移，禁止自动回收");
            AssetDatabase.SaveAssetIfDirty(catalog);
            return result;
        }

        public static void Recycle(PsdToolSettings settings, string guid)
        {
            ResourceImportPolicy.Validate(settings);
            var row = Scan(settings).Find(r => r.record.guid == guid);
            if (row == null || !row.CanRecycle) throw new InvalidOperationException("资源仍被引用、已回收或已丢失；请重新扫描。");
            var catalog = ResourceCatalog.Load(settings);
            PathUtil.EnsureFolder(settings.resourceRecycleFolder);
            string target = AssetDatabase.GenerateUniqueAssetPath(settings.resourceRecycleFolder.TrimEnd('/') + "/" + Path.GetFileName(row.path));
            string error = AssetDatabase.MoveAsset(row.path, target);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            row.record.restorePath = row.path;
            var index = AssetDatabase.LoadAssetAtPath<SpriteIndex>(settings.IndexPath);
            if (index != null) { index.Remove(row.path); AssetDatabase.SaveAssetIfDirty(index); }
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            RemoveFromAtlases(catalog, guid);
        }

        internal static void RemoveFromAtlases(ResourceCatalog catalog, string guid)
        {
            foreach (string atlasGuid in catalog.atlasGuids)
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(atlasGuid));
                if (atlas == null) continue;
                var removed = atlas.GetPackables().Where(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)) == guid).ToArray();
                if (removed.Length == 0) continue;
                atlas.Remove(removed); EditorUtility.SetDirty(atlas); AssetDatabase.SaveAssetIfDirty(atlas);
            }
        }

        public static void Restore(PsdToolSettings settings, string guid)
        {
            var catalog = ResourceCatalog.Load(settings);
            var record = catalog.Find(guid);
            string source = AssetDatabase.GUIDToAssetPath(guid);
            if (record == null || string.IsNullOrEmpty(record.restorePath) || string.IsNullOrEmpty(source)) throw new InvalidOperationException("回收记录或资源不存在。");
            string dest = record.restorePath;
            if (File.Exists(PathUtil.ToFullPath(dest)) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(dest))) throw new InvalidOperationException("原路径已有文件，不会覆盖：" + dest);
            PathUtil.EnsureFolder(Path.GetDirectoryName(dest).Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(source, dest);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            record.restorePath = null; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
        }

        public static void ApplyProfiles(PsdToolSettings settings)
        {
            ResourceImportPolicy.Validate(settings);
            var catalog = ResourceCatalog.Load(settings);
            foreach (var record in catalog.resources.Where(r => string.IsNullOrEmpty(r.restorePath)))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(record.guid)) as TextureImporter;
                if (importer == null) continue;
                string before = EditorJsonUtility.ToJson(importer);
                ResourceImportPolicy.Apply(importer, settings);
                if (before != EditorJsonUtility.ToJson(importer)) importer.SaveAndReimport();
            }
            foreach (string guid in catalog.atlasGuids)
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(guid));
                if (atlas == null) continue;
                string before = EditorJsonUtility.ToJson(atlas);
                AtlasBuilder.ApplyProfile(atlas, settings);
                var packing = atlas.GetPackingSettings(); packing.padding = settings.atlasPadding; atlas.SetPackingSettings(packing);
                if (before != EditorJsonUtility.ToJson(atlas)) { EditorUtility.SetDirty(atlas); AssetDatabase.SaveAssetIfDirty(atlas); }
            }
        }
    }
}
