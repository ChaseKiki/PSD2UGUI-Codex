using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using PsdTool.Parser;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    /// <summary>切图导出：裁透明边、纯色识别、九宫格、像素去重、大图分流、导入设置。</summary>
    public static class SpriteExporter
    {
        enum ImportKind { Sprite, Texture, Preview }

        struct ImportRequest
        {
            public string path;
            public ImportKind kind;
            public Vector4 border;
            public bool tiled;
            public bool isNew;
            public PsdNode node;
            public long pixelHash;
        }

        public static void Export(PsdDocument doc, PsdTree tree, PsdToolSettings settings)
        {
            ResourceImportPolicy.Validate(settings);
            string spriteDir = settings.Expand(settings.panelSprites, tree.moduleName, tree.panelName);
            string textureDir = settings.Expand(settings.panelTextures, tree.moduleName, tree.panelName);
            string commonDir = settings.commonSprites.TrimEnd('/');
            string moduleDir = settings.Expand(settings.moduleSprites, tree.moduleName, tree.panelName);
            string previewDir = settings.PreviewsFolder;
            foreach (var d in new[] { spriteDir, textureDir, commonDir, previewDir }) PathUtil.EnsureFolder(d);

            if (tree.suggestions == null) tree.suggestions = new List<ResourceSuggestion>();
            else tree.suggestions.RemoveAll(s => s.kind == SuggestionKind.MoveToCommon || s.kind == SuggestionKind.UseSimilar);

            var targets = new List<PsdNode>();
            foreach (var n in tree.nodes)
                if (n.status != NodeStatus.PendingDelete && n.kind != LayerKind.Group && n.kind != LayerKind.Text)
                    targets.Add(n);

            // 所有依赖 PS 的步骤先完成；失败时不开始覆盖项目中的切图。
            var baked = PhotoshopFxBaker.Prepare(doc, targets.Where(n => n.resourceOverride == null).ToList(), settings);

            var written = new Dictionary<string, PsdNode>();
            var imports = new List<ImportRequest>();
            var index = SpriteIndex.GetOrCreate(settings);
            var catalog = ResourceCatalog.Load(settings);

            using (var reader = new PsdPixelReader(doc))
            {
                try
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var node = targets[i];
                        if (EditorUtility.DisplayCancelableProgressBar("导出切图", node.goName, (float)i / targets.Count))
                            throw new OperationCanceledException("已取消导出");

                        var layer = doc.FindLayer(node.layerId);
                        if (layer == null) continue;
                        if (node.resourceOverride != null) { GeneratedResourceStore.BindOverride(node, LogicalBounds(layer)); continue; }
                        ExportNode(reader, layer, node, tree, settings, index, catalog, baked, spriteDir, textureDir, moduleDir, commonDir, written, imports);
                    }

                    EditorUtility.DisplayProgressBar("导出切图", "合成预览图", 1f);
                    ExportPreview(reader, doc, tree, previewDir, imports);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            AssetDatabase.Refresh();
            ApplyImportSettings(imports, settings);
            Remember(index, settings, imports);

            foreach (var request in imports)
            {
                if (request.node == null) continue;
                string guid = AssetDatabase.AssetPathToGUID(request.path);
                request.node.generatedAssetGuid = guid;
                catalog.Record(guid, ResourceCatalog.Source(tree), ResourceCatalog.Key(request.node), request.pixelHash,
                    request.border, request.tiled, request.kind == ImportKind.Sprite);
            }

            foreach (var node in targets)
                node.image.assetGuid = string.IsNullOrEmpty(node.image.assetPath) ? null : AssetDatabase.AssetPathToGUID(node.image.assetPath);
            catalog.SyncBindings(tree);
            AssetDatabase.SaveAssetIfDirty(catalog);
            EditorUtility.SetDirty(index);
        }

        static void ExportNode(PsdPixelReader reader, PsdLayer layer, PsdNode node, PsdTree tree, PsdToolSettings settings, SpriteIndex index, ResourceCatalog catalog,
            Dictionary<int, PhotoshopFxBaker.Entry> baked,
            string spriteDir, string textureDir, string moduleDir, string commonDir, Dictionary<string, PsdNode> written, List<ImportRequest> imports)
        {
            var info = node.image;
            info.fxBaked = false;
            info.logicalRect = LogicalBounds(layer);
            info.renderOpacity = node.opacity;
            RectInt bounds;
            Color32[] pixels;
            if (baked.TryGetValue(layer.Id, out var fx))
            {
                pixels = ReadBakedPixels(fx);
                bounds = new RectInt(fx.x, fx.y, fx.width, fx.height);
                info.fxBaked = true;
                info.renderOpacity = layer.Opacity / 255f;
            }
            else if (Markers.Has(node.markers, "merge"))
            {
                pixels = MergeGroup(reader, layer, out bounds);
            }
            else
            {
                pixels = reader.DecodeLayer(layer);
                bounds = layer.Bounds;
            }

            var opaque = pixels == null ? new RectInt() : PixelUtil.OpaqueBounds(pixels, bounds.width, bounds.height);
            if (opaque.width == 0 || opaque.height == 0)
            {
                ClearImage(info, true);
                return;
            }

            var cropped = PixelUtil.Crop(pixels, bounds.width, opaque);
            info.isEmpty = false;
            info.trimmedRect = new RectInt(bounds.x + opaque.x, bounds.y + opaque.y, opaque.width, opaque.height);

            bool isSolid = PixelUtil.IsSolid(cropped, out var solid);
            if (!info.fxBaked && (Markers.Has(node.markers, "color") || (isSolid && !Markers.Has(node.markers, "raw"))))
            {
                ClearImage(info, false);
                info.isSolid = true;
                info.solidColor = solid;
                info.solidColor.a = 255;
                return;
            }

            bool raw = Markers.Has(node.markers, "raw")
                       || opaque.width > settings.largeImageThreshold
                       || opaque.height > settings.largeImageThreshold;
            string ownedPath = AssetDatabase.GUIDToAssetPath(node.generatedAssetGuid ?? "");
            if (catalog.IsOwner(node.generatedAssetGuid, ResourceCatalog.Source(tree), ResourceCatalog.Key(node)) &&
                AssetImporter.GetAtPath(ownedPath) is TextureImporter ownedImporter && !Markers.Has(node.markers, "raw"))
                raw = ownedImporter.textureType != TextureImporterType.Sprite;
            var spritePixels = cropped;
            int sw = opaque.width, sh = opaque.height;
            Vector4 border = Vector4.zero;
            bool sliced = false, tiled = Markers.Has(node.markers, "tile");
            bool forbidNine = node.nineOverride == NineSliceOverride.Forbid || Markers.Has(node.markers, "no9s");
            bool forceNine = node.nineOverride == NineSliceOverride.Force || Markers.Has(node.markers, "9s");
            bool stretchFx = info.fxBaked && StretchImagePolicy.NeedsSliceCheck(tree, node);
            if (!raw && !forbidNine && (!info.fxBaked || forceNine || stretchFx))
            {
                string nineArg = null;
                if (node.nineOverride == NineSliceOverride.Force && node.nineBorder != Vector4.zero)
                    nineArg = $"{node.nineBorder.x},{node.nineBorder.w},{node.nineBorder.z},{node.nineBorder.y}";
                else if (Markers.TryGetArg(node.markers, "9s", out var markerNine))
                    nineArg = markerNine ?? string.Empty;
                else if (forceNine)
                    nineArg = string.Empty;

                if (NineSlice.Try(cropped, sw, sh, forceNine ? nineArg ?? string.Empty : null, out var slice))
                {
                    spritePixels = slice.pixels;
                    sw = slice.width;
                    sh = slice.height;
                    border = slice.border;
                    tiled = slice.tiled || tiled;
                    sliced = !tiled;
                    if (!string.IsNullOrEmpty(slice.warning)) tree.warnings.Add($"“{node.goName}”{slice.warning}");
                }
                else if (forceNine)
                {
                    tree.warnings.Add(string.IsNullOrEmpty(slice.warning)
                        ? $"“{node.goName}”要求九宫，但没有识别出可切的边"
                        : $"“{node.goName}”{slice.warning}");
                }
                else if (stretchFx)
                    tree.warnings.Add($"动态拉伸图片“{node.goName}”的 FX 九宫回验未通过，保留整图；请指定强制九宫边框或引用已设置九宫的 Sprite 后检查拉伸效果。");
            }

            long hash = PixelUtil.Hash(spritePixels, sw, sh);
            string folder = Markers.Has(node.markers, "common") ? (raw ? settings.commonTextures : commonDir) : raw ? textureDir : spriteDir;
            string ownPath = $"{folder}/{PathUtil.SanitizeFileName(tree.panelName)}_{node.layerId}.png";
            string localPath = $"{(raw ? textureDir : spriteDir)}/{PathUtil.SanitizeFileName(tree.panelName)}_{node.layerId}.png";
            GeneratedResourceStore.AdoptLegacy(catalog, tree, node, ownPath);

            if (GeneratedResourceStore.RetainBinding(node, hash, Markers.Has(node.markers, "raw"), forceNine || forbidNine, border, Markers.Has(node.markers, "tile")))
            {
                written[ResourceKey(hash, !node.image.useRawImage, node.image.border, node.image.tiled)] = node;
                return;
            }
            string resourceKey = ResourceKey(hash, !raw, border, tiled);

            // 已生成的独占资源优先原位更新，不自动切换成另一个 GUID。
            bool ownsFile = catalog.IsOwner(node.generatedAssetGuid, ResourceCatalog.Source(tree), ResourceCatalog.Key(node));
            if (!ownsFile && written.TryGetValue(resourceKey, out var owner))
            {
                CopyFrom(info, owner);
                return;
            }

            if (!ownsFile && index.TryFindExact(hash, ownPath, raw ? textureDir : spriteDir, moduleDir, raw ? settings.commonTextures : commonDir, out var exact, out bool suggestMove,
                sw, sh, !raw, border, tiled, settings.textureFilter))
            {
                if (suggestMove)
                {
                    tree.suggestions.Add(new ResourceSuggestion
                    {
                        kind = SuggestionKind.MoveToCommon,
                        layerId = node.layerId,
                        assetPath = exact.path,
                        assetGuid = exact.guid,
                        message = $"“{node.goName}”与其他界面的 {exact.path} 相同，建议移到 Common",
                    });
                }
                else
                {
                    ApplyExternal(info, exact, hash);
                    written[resourceKey] = node;
                    return;
                }
            }
            else if (!raw && index.TryFindSimilar(spritePixels, sw, sh, hash, spriteDir, moduleDir, commonDir, out var similar, out float error,
                true, border, tiled, settings.textureFilter))
            {
                tree.suggestions.Add(new ResourceSuggestion
                {
                    kind = SuggestionKind.UseSimilar,
                    layerId = node.layerId,
                    assetPath = similar.path,
                    assetGuid = similar.guid,
                    message = $"“{node.goName}”与 {similar.path} 近似（平均误差 {error:0.##}），确认后可复用",
                });
            }

            ownPath = GeneratedResourceStore.WritablePath(catalog, tree, node, settings, ownPath, localPath, !raw);
            string fullPath = PathUtil.ToFullPath(ownPath);
            bool exists = File.Exists(fullPath);
            var record = catalog.Find(AssetDatabase.AssetPathToGUID(ownPath));
            if (record != null && AssetImporter.GetAtPath(ownPath) is TextureImporter existingImporter)
            {
                if (forceNine || forbidNine) record.manualBorder = false;
                if (node.nineOverride == NineSliceOverride.Auto && !forceNine && !forbidNine && (record.manualBorder || existingImporter.spriteBorder != record.generatedBorder))
                {
                    record.manualBorder = true;
                    border = existingImporter.spriteBorder;
                    tree.warnings.Add("“" + node.goName + "”保留了在 Sprite 导入设置中手调的九宫边框。");
                }
                if (Markers.Has(node.markers, "tile")) record.manualTiled = false;
                else if (record.manualTiled || SpriteIndex.IsTiled(ownPath) != record.generatedTiled)
                { record.manualTiled = true; tiled = SpriteIndex.IsTiled(ownPath); }
                EditorUtility.SetDirty(catalog);
            }
            if (border.x + border.z > sw || border.y + border.w > sh)
                throw new InvalidOperationException("图片“" + node.goName + "”的新尺寸无法容纳保留的九宫边框，请在节点面板重新指定边框。");
            sliced = !tiled && border != Vector4.zero;
            if (!exists || !GeneratedResourceStore.PixelHash(ownPath, out long existingHash) || existingHash != hash)
                File.WriteAllBytes(fullPath, PixelUtil.EncodePng(spritePixels, sw, sh));

            info.pixelHash = hash;
            info.assetPath = ownPath;
            info.useRawImage = raw;
            info.sliced = sliced;
            info.tiled = tiled;
            info.border = border;
            info.isSolid = false;
            info.reusedFromLayerId = -1;
            info.reusedFromPath = null;
            written[ResourceKey(hash, !raw, border, tiled)] = node;
            imports.Add(new ImportRequest
            {
                path = ownPath,
                kind = raw ? ImportKind.Texture : ImportKind.Sprite,
                border = border,
                tiled = tiled,
                isNew = !exists,
                node = node,
                pixelHash = hash,
            });
        }

        static string ResourceKey(long hash, bool sprite, Vector4 border, bool tiled) =>
            string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2},{3},{4},{5}:{6}", hash, sprite, border.x, border.y, border.z, border.w, tiled);

        static Color32[] ReadBakedPixels(PhotoshopFxBaker.Entry entry)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(entry.path)) || texture.width != entry.width || texture.height != entry.height)
                    throw new InvalidOperationException("Photoshop 返回的 PNG 尺寸与清单不符：" + entry.path);
                var input = texture.GetPixels32();
                var output = new Color32[input.Length];
                for (int y = 0; y < entry.height; y++)
                    Array.Copy(input, (entry.height - 1 - y) * entry.width, output, y * entry.width, entry.width);
                return output;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        internal static RectInt LogicalBounds(PsdLayer layer)
        {
            if (layer.Kind != PsdLayerKind.Group) return layer.Bounds;
            RectInt? bounds = null;
            foreach (var child in layer.Children)
            {
                if (!child.Visible) continue;
                var rect = LogicalBounds(child);
                if (rect.width <= 0 || rect.height <= 0) continue;
                if (!bounds.HasValue) bounds = rect;
                else bounds = new RectInt(Math.Min(bounds.Value.xMin, rect.xMin), Math.Min(bounds.Value.yMin, rect.yMin),
                    Math.Max(bounds.Value.xMax, rect.xMax) - Math.Min(bounds.Value.xMin, rect.xMin),
                    Math.Max(bounds.Value.yMax, rect.yMax) - Math.Min(bounds.Value.yMin, rect.yMin));
            }
            return bounds ?? layer.Bounds;
        }

        static void ClearImage(ImageInfo info, bool empty)
        {
            info.isEmpty = empty;
            info.isSolid = false;
            info.assetPath = null;
            info.assetGuid = null;
            info.useRawImage = false;
            info.sliced = false;
            info.tiled = false;
            info.border = Vector4.zero;
            info.reusedFromLayerId = -1;
            info.reusedFromPath = null;
        }

        static void CopyFrom(ImageInfo info, PsdNode owner)
        {
            var src = owner.image;
            info.pixelHash = src.pixelHash;
            info.assetPath = src.assetPath;
            info.useRawImage = src.useRawImage;
            info.sliced = src.sliced;
            info.tiled = src.tiled;
            info.border = src.border;
            info.isSolid = false;
            info.reusedFromLayerId = owner.layerId;
            info.reusedFromPath = null;
        }

        static void ApplyExternal(ImageInfo info, SpriteIndexEntry entry, long hash)
        {
            GeneratedResourceStore.ApplyReference(info, entry.path);
            info.pixelHash = hash;
            info.assetPath = entry.path;
            info.assetGuid = entry.guid;
            info.isSolid = false;
            info.reusedFromLayerId = -1;
            info.reusedFromPath = entry.path;
            bool sprite = AssetImporter.GetAtPath(entry.path) is TextureImporter importer
                          && importer.textureType == TextureImporterType.Sprite;
            info.useRawImage = !sprite;
        }

        /// <summary>@merge：按 Normal 模式把组内可见图层合成一张图（忽略混合模式和图层样式）。</summary>
        static Color32[] MergeGroup(PsdPixelReader reader, PsdLayer group, out RectInt bounds)
        {
            var leaves = new List<(PsdLayer layer, float opacity)>();
            Collect(group, 1f, leaves);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var (l, _) in leaves)
            {
                if (l.Width == 0 || l.Height == 0) continue;
                minX = Math.Min(minX, l.Left);
                minY = Math.Min(minY, l.Top);
                maxX = Math.Max(maxX, l.Right);
                maxY = Math.Max(maxY, l.Bottom);
            }

            if (maxX <= minX || maxY <= minY)
            {
                bounds = new RectInt();
                return null;
            }

            bounds = new RectInt(minX, minY, maxX - minX, maxY - minY);
            var canvas = new Color32[bounds.width * bounds.height];
            foreach (var (l, opacity) in leaves)
            {
                var px = reader.DecodeLayer(l);
                if (px == null) continue;
                float alpha = opacity * l.Opacity / 255f * l.FillOpacity / 255f;
                PixelUtil.AlphaOver(canvas, bounds.width, bounds.height, px, l.Width, l.Height, l.Left - minX, l.Top - minY, alpha);
            }
            return canvas;
        }

        static void Collect(PsdLayer group, float opacity, List<(PsdLayer, float)> leaves)
        {
            foreach (var child in group.Children)
            {
                if (!child.Visible || child.Kind == PsdLayerKind.Adjustment) continue;
                if (child.Kind == PsdLayerKind.Group) Collect(child, opacity * child.Opacity / 255f, leaves);
                else leaves.Add((child, opacity));
            }
        }

        static void ExportPreview(PsdPixelReader reader, PsdDocument doc, PsdTree tree, string previewDir, List<ImportRequest> imports)
        {
            Color32[] composite;
            try
            {
                composite = reader.DecodeComposite();
            }
            catch (Exception e)
            {
                tree.warnings.Add($"合成预览图读取失败：{e.Message}");
                composite = null;
            }

            if (composite == null)
            {
                tree.previewPath = null;
                tree.warnings.Add("PSD 中没有可用的合成图，无法做对比预览。请在 PS 保存时勾选“最大兼容”。");
                return;
            }

            string path = $"{previewDir}/{PathUtil.SanitizeFileName(tree.moduleName)}_{PathUtil.SanitizeFileName(tree.panelName)}.png";
            string full = PathUtil.ToFullPath(path);
            bool exists = File.Exists(full);
            byte[] encoded = PixelUtil.EncodePng(composite, doc.Width, doc.Height);
            if (!exists || !File.ReadAllBytes(full).SequenceEqual(encoded)) File.WriteAllBytes(full, encoded);
            if (!exists)
            {
                imports.Add(new ImportRequest { path = path, kind = ImportKind.Preview, isNew = true });
            }
            tree.previewPath = path;
        }

        /// <summary>新文件设置完整导入参数；已有切图只更新九宫格边框和网格类型。</summary>
        static void ApplyImportSettings(List<ImportRequest> files, PsdToolSettings settings)
        {
            if (files.Count == 0) return;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var file in files)
                {
                    if (!(AssetImporter.GetAtPath(file.path) is TextureImporter importer)) continue;
                    if (file.isNew)
                    {
                        importer.mipmapEnabled = false;
                        importer.alphaIsTransparency = true;
                        importer.wrapMode = TextureWrapMode.Clamp;
                        importer.npotScale = TextureImporterNPOTScale.None;
                        switch (file.kind)
                        {
                            case ImportKind.Sprite:
                                importer.textureType = TextureImporterType.Sprite;
                                importer.spriteImportMode = SpriteImportMode.Single;
                                importer.spritePixelsPerUnit = 100;
                                ResourceImportPolicy.Apply(importer, settings);
                                break;
                            case ImportKind.Texture:
                                importer.textureType = TextureImporterType.Default;
                                ResourceImportPolicy.Apply(importer, settings);
                                break;
                            case ImportKind.Preview:
                                importer.textureType = TextureImporterType.Default;
                                importer.maxTextureSize = 4096;
                                importer.textureCompression = TextureImporterCompression.Uncompressed;
                                break;
                        }
                    }

                    if (file.kind == ImportKind.Sprite)
                    {
                        if (importer.spriteBorder != file.border) importer.spriteBorder = file.border;
                        if (importer.userData == SpriteIndex.TiledTag && !file.tiled) importer.userData = string.Empty;
                        SpriteIndex.SetTiled(file.path, file.tiled);
                        if (file.isNew)
                        {
                            var textureSettings = new TextureImporterSettings();
                            importer.ReadTextureSettings(textureSettings);
                            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                            importer.SetTextureSettings(textureSettings);
                        }
                    }
                    if (file.isNew || EditorUtility.IsDirty(importer)) importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static void Remember(SpriteIndex index, PsdToolSettings settings, List<ImportRequest> files)
        {
            foreach (var file in files)
            {
                if (file.kind == ImportKind.Preview || !SpriteIndex.TryReadPixels(file.path, out var px, out int w, out int h)) continue;
                index.Upsert(new SpriteIndexEntry
                {
                    pixelHash = PixelUtil.Hash(px, w, h),
                    dHash = PixelUtil.DHash(px, w, h),
                    width = w,
                    height = h,
                    guid = AssetDatabase.AssetPathToGUID(file.path),
                    path = file.path,
                    scope = settings.Classify(file.path),
                    border = file.border,
                    tiled = file.tiled,
                });
            }
        }
    }
}
