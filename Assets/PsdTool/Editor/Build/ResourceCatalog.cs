using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    [Serializable]
    public sealed class GeneratedResource
    {
        public string guid, sourceGuid, nodeKey;
        public long writtenHash;
        public Vector4 generatedBorder;
        public bool generatedTiled;
        public bool sprite;
        public bool manualBorder, manualTiled;
        public string restorePath;
    }

    [Serializable]
    public sealed class ResourceBinding
    {
        public string sourceGuid, nodeKey, guid;
    }

    /// <summary>只登记工具实际创建或验证接管的文件；公共引用和手动指定资源不归生成器写入。</summary>
    public sealed class ResourceCatalog : ScriptableObject
    {
        public List<GeneratedResource> resources = new List<GeneratedResource>();
        public List<ResourceBinding> bindings = new List<ResourceBinding>();
        public List<string> atlasGuids = new List<string>();

        public static string Source(PsdTree tree)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tree));
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("资源导出需要已保存的节点树，请先解析 PSD。");
            return guid;
        }
        public static string Key(PsdNode node) => string.IsNullOrEmpty(node.stableKey) ? "layer:" + node.layerId : node.stableKey;
        public static ResourceCatalog Load(PsdToolSettings settings, bool create = true)
        {
            string path = settings.toolDataRoot + "/ResourceState/ResourceCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<ResourceCatalog>(path);
            if (catalog != null) return catalog;
            // 旧目录继续读取，不原地新建另一个登记表；实际迁移需用户审核。
            catalog = AssetDatabase.LoadAssetAtPath<ResourceCatalog>(settings.toolDataRoot + "/Resources/ResourceCatalog.asset");
            if (catalog != null || !create) return catalog;
            PathUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            catalog = CreateInstance<ResourceCatalog>(); AssetDatabase.CreateAsset(catalog, path);
            return catalog;
        }
        public GeneratedResource Find(string guid) => string.IsNullOrEmpty(guid) ? null : resources.Find(r => r.guid == guid);
        public bool IsOwner(string guid, string source, string key)
        {
            var entry = Find(guid);
            return entry != null && entry.sourceGuid == source && entry.nodeKey == key && string.IsNullOrEmpty(entry.restorePath);
        }
        public bool IsShared(string guid, string source, string key) => bindings.Any(b => b.guid == guid && (b.sourceGuid != source || b.nodeKey != key));
        public void Record(string guid, string source, string key, long hash, Vector4 border, bool tiled, bool sprite)
        {
            var record = Find(guid);
            if (record == null) { record = new GeneratedResource { guid = guid, sourceGuid = source, nodeKey = key }; resources.Add(record); }
            else if (record.writtenHash == hash && record.generatedBorder == border && record.generatedTiled == tiled && record.sprite == sprite) return;
            record.writtenHash = hash; record.generatedBorder = border; record.generatedTiled = tiled; record.sprite = sprite;
            EditorUtility.SetDirty(this);
        }
        public void SyncBindings(PsdTree tree)
        {
            string source = Source(tree);
            var next = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete && !n.image.isEmpty && !string.IsNullOrEmpty(n.image.assetGuid))
                .Select(n => new ResourceBinding { sourceGuid = source, nodeKey = Key(n), guid = n.image.assetGuid }).ToList();
            var previous = bindings.Where(b => b.sourceGuid == source).ToList();
            if (previous.Count == next.Count && previous.Zip(next, (a, b) => a.nodeKey == b.nodeKey && a.guid == b.guid).All(v => v)) return;
            bindings.RemoveAll(b => b.sourceGuid == source); bindings.AddRange(next);
            EditorUtility.SetDirty(this);
        }
    }
}
