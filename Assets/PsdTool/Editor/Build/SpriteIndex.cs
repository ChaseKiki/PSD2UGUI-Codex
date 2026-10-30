using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    [Serializable]
    public class SpriteIndexEntry
    {
        public long pixelHash;
        public long dHash;
        public int width;
        public int height;
        public string guid;
        public string path;
        public SpriteScope scope;
        public Vector4 border;
        public bool tiled;
    }

    /// <summary>按像素哈希索引 UI 目录里的 Sprite 和贴图，由导入管线增量维护。</summary>
    public class SpriteIndex : ScriptableObject
    {
        public const int SimilarHamming = 5;
        public const int SimilarSizeSlack = 2;
        public const float SimilarAvgError = 1.5f;
        public const int SimilarMaxError = 16;
        public const string TiledTag = "psdtool:tiled";
        public const string TiledLabel = "PsdToolTiled";
        public static bool IsTiled(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return (asset != null && AssetDatabase.GetLabels(asset).Contains(TiledLabel)) ||
                (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.userData == TiledTag);
        }
        internal static void SetTiled(string path, bool tiled)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset == null) return;
            var labels = AssetDatabase.GetLabels(asset).ToList();
            bool changed = tiled ? !labels.Contains(TiledLabel) : labels.Contains(TiledLabel);
            if (!changed) return;
            if (tiled) labels.Add(TiledLabel); else labels.Remove(TiledLabel);
            AssetDatabase.SetLabels(asset, labels.ToArray());
        }

        internal static bool Compatible(string path, bool sprite, Vector4? border, bool tiled, FilterMode filter)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !importer.sRGBTexture || importer.filterMode != filter) return false;
            if (sprite) return importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single &&
                Mathf.Approximately(importer.spritePixelsPerUnit, 100) && (!border.HasValue || importer.spriteBorder == border.Value) && IsTiled(path) == tiled;
            return importer.textureType == TextureImporterType.Default;
        }

        public List<SpriteIndexEntry> entries = new List<SpriteIndexEntry>();

        public static SpriteIndex GetOrCreate(PsdToolSettings settings)
        {
            var index = AssetDatabase.LoadAssetAtPath<SpriteIndex>(settings.IndexPath);
            if (index != null) return index;
            PathUtil.EnsureFolder(Path.GetDirectoryName(settings.IndexPath)?.Replace('\\', '/'));
            index = CreateInstance<SpriteIndex>();
            AssetDatabase.CreateAsset(index, settings.IndexPath);
            return index;
        }

        public bool Upsert(SpriteIndexEntry entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(entries[i].path, entry.path, StringComparison.OrdinalIgnoreCase)) continue;
                var old = entries[i];
                if (old.pixelHash == entry.pixelHash && old.dHash == entry.dHash && old.width == entry.width
                    && old.height == entry.height && old.guid == entry.guid && old.scope == entry.scope
                    && old.border == entry.border && old.tiled == entry.tiled)
                    return false;
                entries[i] = entry;
                EditorUtility.SetDirty(this);
                return true;
            }
            entries.Add(entry);
            EditorUtility.SetDirty(this);
            return true;
        }

        public bool Remove(string path)
        {
            int removed = entries.RemoveAll(e => string.Equals(e.path, path, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) EditorUtility.SetDirty(this);
            return removed > 0;
        }

        public void Retarget(string from, string to)
        {
            foreach (var e in entries)
            {
                if (!string.Equals(e.path, from, StringComparison.OrdinalIgnoreCase)) continue;
                e.path = to;
                EditorUtility.SetDirty(this);
                return;
            }
        }

        /// <summary>精确哈希。优先当前界面，其次当前模块，最后 Common。其他界面只返回建议，不自动复用。</summary>
        public bool TryFindExact(long hash, string ignorePath, string panelDir, string moduleDir, string commonDir, out SpriteIndexEntry match, out bool suggestMoveToCommon,
            int width = 0, int height = 0, bool sprite = true, Vector4? border = null, bool tiled = false, FilterMode filter = FilterMode.Bilinear)
        {
            match = null;
            suggestMoveToCommon = false;
            int bestRank = int.MaxValue;
            foreach (var e in entries)
            {
                if (e.pixelHash != hash) continue;
                string actualPath = AssetDatabase.GUIDToAssetPath(e.guid);
                if (string.IsNullOrEmpty(actualPath)) continue;
                e.path = actualPath;
                if (string.Equals(e.path, ignorePath, System.StringComparison.OrdinalIgnoreCase)) continue;
                if ((width > 0 && e.width != width) || (height > 0 && e.height != height) || !Compatible(e.path, sprite, border, tiled, filter)) continue;
                if (!GeneratedResourceStore.PixelHash(e.path, out long actual) || actual != hash) continue;
                int rank = Rank(e.path, panelDir, moduleDir, commonDir);
                if (rank < bestRank || (rank == bestRank && match != null && string.CompareOrdinal(e.path, match.path) < 0))
                {
                    bestRank = rank;
                    match = e;
                }
            }
            if (match == null) return false;
            suggestMoveToCommon = bestRank > 2;
            return true;
        }

        /// <summary>容差匹配：尺寸接近且差值哈希接近，再逐像素比较。需要人工确认。</summary>
        public bool TryFindSimilar(Color32[] px, int w, int h, long hash, string panelDir, string moduleDir, string commonDir, out SpriteIndexEntry match, out float avgError,
            bool sprite = true, Vector4? border = null, bool tiled = false, FilterMode filter = FilterMode.Bilinear)
        {
            match = null;
            avgError = 0;
            long dHash = PixelUtil.DHash(px, w, h);
            float best = float.MaxValue;
            foreach (var e in entries)
            {
                if (e.pixelHash == hash) continue;
                string currentPath = AssetDatabase.GUIDToAssetPath(e.guid);
                if (string.IsNullOrEmpty(currentPath)) continue;
                e.path = currentPath;
                if (!Compatible(e.path, sprite, border, tiled, filter)) continue;
                if (Math.Abs(e.width - w) > SimilarSizeSlack || Math.Abs(e.height - h) > SimilarSizeSlack) continue;
                if (e.width != w || e.height != h) continue;
                if (PixelUtil.Hamming(e.dHash, dHash) > SimilarHamming) continue;
                if (Rank(e.path, panelDir, moduleDir, commonDir) > 2) continue;
                if (!TryReadPixels(e.path, out var other, out int ow, out int oh) || ow != w || oh != h) continue;
                float avg = AverageError(px, other, out int max);
                if (avg > SimilarAvgError || max > SimilarMaxError || avg >= best) continue;
                best = avg;
                match = e;
                avgError = avg;
            }
            return match != null;
        }

        static int Rank(string path, string panelDir, string moduleDir, string commonDir)
        {
            if (In(path, panelDir)) return 0;
            if (In(path, moduleDir)) return 1;
            if (In(path, commonDir)) return 2;
            return 3;
        }

        static bool In(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            return path.Equals(folder, StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static void Rebuild(PsdToolSettings settings)
        {
            var index = GetOrCreate(settings);
            var replacement = CreateInstance<SpriteIndex>();
            var roots = settings.ScanRoots().Where(AssetDatabase.IsValidFolder).ToList();
            var guids = roots.Count == 0 ? new string[0] : AssetDatabase.FindAssets("t:Texture2D", roots.ToArray());
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar("重建图片索引", path, (float)i / guids.Length)) throw new OperationCanceledException();
                    replacement.Refresh(settings, path);
                }
                index.entries = replacement.entries.OrderBy(e => e.path, StringComparer.Ordinal).ToList();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DestroyImmediate(replacement);
            }
            EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
        }

        public bool Refresh(PsdToolSettings settings, string path)
        {
            if (!IsIndexedTexture(path) || !InAnyRoot(settings, path)) return Remove(path);
            if (!TryReadPixels(path, out var px, out int w, out int h)) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return Upsert(new SpriteIndexEntry
            {
                pixelHash = PixelUtil.Hash(px, w, h),
                dHash = PixelUtil.DHash(px, w, h),
                width = w,
                height = h,
                guid = AssetDatabase.AssetPathToGUID(path),
                path = path,
                scope = settings.Classify(path),
                border = importer != null ? importer.spriteBorder : Vector4.zero,
                tiled = IsTiled(path),
            });
        }

        public static bool IsIndexedTexture(string path)
        {
            return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
        }

        static bool InAnyRoot(PsdToolSettings settings, string path)
        {
            if (In(path, settings.resourceRecycleFolder)) return false;
            foreach (var root in settings.ScanRoots())
                if (In(path, root)) return true;
            return false;
        }

        /// <summary>从图片文件解码，转成从上到下的行顺序，和导出时的哈希一致。</summary>
        public static bool TryReadPixels(string assetPath, out Color32[] pixels, out int width, out int height)
        {
            pixels = null;
            width = height = 0;
            string full = PathUtil.ToFullPath(assetPath);
            if (!File.Exists(full)) return false;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(full))) return false;
                width = tex.width;
                height = tex.height;
                var bottomUp = tex.GetPixels32();
                pixels = new Color32[bottomUp.Length];
                for (int y = 0; y < height; y++)
                    Array.Copy(bottomUp, y * width, pixels, (height - 1 - y) * width, width);
                return true;
            }
            finally
            {
                DestroyImmediate(tex);
            }
        }

        static float AverageError(Color32[] a, Color32[] b, out int max)
        {
            long sum = 0;
            max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int dr = Math.Abs(a[i].r - b[i].r);
                int dg = Math.Abs(a[i].g - b[i].g);
                int db = Math.Abs(a[i].b - b[i].b);
                int da = Math.Abs(a[i].a - b[i].a);
                max = Math.Max(max, Math.Max(Math.Max(dr, dg), Math.Max(db, da)));
                sum += dr + dg + db + da;
            }
            return (float)sum / (a.Length * 4f);
        }
    }

    public static class ResourceActions
    {
        public static void Apply(ResourceSuggestion suggestion, PsdTree tree, PsdToolSettings settings)
        {
            if (suggestion.done) return;
            switch (suggestion.kind)
            {
                case SuggestionKind.MoveToCommon:
                    MoveToCommon(suggestion, tree, settings);
                    suggestion.done = true;
                    break;
                case SuggestionKind.UseSimilar:
                    UseSimilar(suggestion, tree);
                    suggestion.done = true;
                    break;
                case SuggestionKind.ApplyRule:
                    RuleRecognizer.Accept(suggestion, tree);
                    break;
                case SuggestionKind.ApplyAi:
                    AiMarker.Accept(suggestion, tree);
                    break;
            }
            EditorUtility.SetDirty(tree);
            var catalog = ResourceCatalog.Load(settings);
            catalog.SyncBindings(tree);
            AssetDatabase.SaveAssets();
        }

        static void MoveToCommon(ResourceSuggestion suggestion, PsdTree tree, PsdToolSettings settings)
        {
            string source = !string.IsNullOrEmpty(suggestion.assetGuid) ? AssetDatabase.GUIDToAssetPath(suggestion.assetGuid) : suggestion.assetPath;
            string guid = AssetDatabase.AssetPathToGUID(source);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("建议中的图片已经不存在，请重新解析/生成建议。");
            var catalog = ResourceCatalog.Load(settings);
            if (catalog.Find(guid) == null) throw new InvalidOperationException("该图片不属于本工具，不会自动移动。请用节点上的“指定图片”直接引用。");
            bool sprite = AssetImporter.GetAtPath(source) is TextureImporter sourceImporter && sourceImporter.textureType == TextureImporterType.Sprite;
            string folder = sprite ? settings.commonSprites : settings.commonTextures;
            string file = Path.GetFileName(source);
            PathUtil.EnsureFolder(folder);
            string dest = $"{folder.TrimEnd('/')}/{file}";
            if (!string.Equals(dest, source, StringComparison.OrdinalIgnoreCase)) dest = AssetDatabase.GenerateUniqueAssetPath(dest);

            if (!string.Equals(dest, source, StringComparison.OrdinalIgnoreCase))
            {
                string error = AssetDatabase.MoveAsset(source, dest);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }

            var index = AssetDatabase.LoadAssetAtPath<SpriteIndex>(settings.IndexPath);
            if (index != null)
            {
                index.Retarget(source, dest);
                var entry = index.entries.Find(e => e.path == dest);
                if (entry != null) entry.scope = SpriteScope.Common;
                EditorUtility.SetDirty(index);
            }

            foreach (var treeGuid in AssetDatabase.FindAssets("t:PsdTree"))
            {
                var affected = AssetDatabase.LoadAssetAtPath<PsdTree>(AssetDatabase.GUIDToAssetPath(treeGuid));
                bool changed = false;
                foreach (var node in affected.nodes)
                {
                    if (node.image.assetGuid != guid && node.image.assetPath != source) continue;
                    node.image.assetPath = dest; node.image.assetGuid = guid; node.image.reusedFromPath = dest; changed = true;
                }
                if (changed) EditorUtility.SetDirty(affected);
            }
            suggestion.assetPath = dest; suggestion.assetGuid = guid;
            ResourceMaintenance.RemoveFromAtlases(catalog, guid);
            AtlasBuilder.Update(settings, tree.moduleName, tree.panelName);
        }

        static void UseSimilar(ResourceSuggestion suggestion, PsdTree tree)
        {
            var node = tree.Find(suggestion.layerId);
            if (node == null) throw new InvalidOperationException("建议中的图层已经不存在。");
            string path = !string.IsNullOrEmpty(suggestion.assetGuid) ? AssetDatabase.GUIDToAssetPath(suggestion.assetGuid) : suggestion.assetPath;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            node.resourceOverride = sprite != null ? (UnityEngine.Object)sprite : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (node.resourceOverride == null) throw new InvalidOperationException("建议中的图片已经不存在。");
            GeneratedResourceStore.BindOverride(node, node.image.logicalRect.width > 0 ? node.image.logicalRect : node.psdRect);
            node.locks &= ~PropGroup.Sprite; node.forceUpdateGroups |= PropGroup.Sprite;
        }
    }
}
