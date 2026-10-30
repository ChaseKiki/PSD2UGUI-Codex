using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class GeneratedResourceStore
    {
        public static void AdoptLegacy(ResourceCatalog catalog, PsdTree tree, PsdNode node, string expectedPath)
        {
            if (string.IsNullOrEmpty(node.image.assetGuid) || catalog.Find(node.image.assetGuid) != null || node.image.reusedFromLayerId >= 0 || !string.IsNullOrEmpty(node.image.reusedFromPath)) return;
            string current = AssetDatabase.GUIDToAssetPath(node.image.assetGuid);
            if (current != expectedPath || node.image.pixelHash == 0 || !PixelHash(current, out long actual) || actual != node.image.pixelHash) return;
            var importer = AssetImporter.GetAtPath(current) as TextureImporter;
            if (importer == null || (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode != SpriteImportMode.Single)) return;
            catalog.Record(node.image.assetGuid, ResourceCatalog.Source(tree), ResourceCatalog.Key(node), actual,
                importer.spriteBorder, SpriteIndex.IsTiled(current), importer.textureType == TextureImporterType.Sprite);
            node.generatedAssetGuid = node.image.assetGuid;
        }

        public static string WritablePath(ResourceCatalog catalog, PsdTree tree, PsdNode node, PsdToolSettings settings,
            string preferredPath, string localPath, bool sprite)
        {
            string source = ResourceCatalog.Source(tree), key = ResourceCatalog.Key(node);
            string previous = AssetDatabase.GUIDToAssetPath(node.generatedAssetGuid ?? "");
            if (string.IsNullOrEmpty(previous)) previous = AssetDatabase.GUIDToAssetPath(node.image.assetGuid ?? "");
            if (!string.IsNullOrEmpty(previous) && catalog.IsOwner(AssetDatabase.AssetPathToGUID(previous), source, key))
            {
                var record = catalog.Find(AssetDatabase.AssetPathToGUID(previous));
                var importer = AssetImporter.GetAtPath(previous) as TextureImporter;
                bool writable = settings.Classify(previous) != SpriteScope.Common && settings.Classify(previous) != SpriteScope.Module &&
                    !catalog.IsShared(record.guid, source, key) && importer != null &&
                    (importer.textureType == TextureImporterType.Sprite) == sprite &&
                    (!sprite || importer.spriteImportMode == SpriteImportMode.Single) &&
                    PixelHash(previous, out long actual) && actual == record.writtenHash;
                if (writable) return previous;
                preferredPath = localPath;
                tree.warnings.Add("“" + node.goName + "”原图片已共用、移入公共目录、被手改或更改类型；本次另存，原资源与引用保留。");
            }
            PathUtil.EnsureFolder(Path.GetDirectoryName(preferredPath).Replace('\\', '/'));
            if (File.Exists(PathUtil.ToFullPath(preferredPath)) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(preferredPath)))
                preferredPath = AssetDatabase.GenerateUniqueAssetPath(preferredPath);
            return preferredPath;
        }

        public static bool RetainBinding(PsdNode node, long incomingHash, bool rawRequested, bool forceBorder, Vector4 border, bool requireTiled = false)
        {
            string path = AssetDatabase.GUIDToAssetPath(node.image.assetGuid ?? "");
            if (string.IsNullOrEmpty(path) || !PixelHash(path, out long actual) || actual != incomingHash) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || (importer.textureType != TextureImporterType.Sprite && importer.textureType != TextureImporterType.Default)) return false;
            bool sprite = importer.textureType == TextureImporterType.Sprite;
            if ((sprite && importer.spriteImportMode != SpriteImportMode.Single) || (rawRequested && sprite) || (forceBorder && importer.spriteBorder != border) || (requireTiled && !SpriteIndex.IsTiled(path))) return false;
            ApplyReference(node.image, path);
            node.image.pixelHash = incomingHash;
            return true;
        }

        public static void ApplyReference(ImageInfo info, string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            info.assetPath = path; info.assetGuid = AssetDatabase.AssetPathToGUID(path);
            info.isEmpty = false; info.isSolid = false;
            info.useRawImage = importer == null || importer.textureType != TextureImporterType.Sprite;
            info.border = info.useRawImage || importer == null ? Vector4.zero : importer.spriteBorder;
            info.tiled = !info.useRawImage && SpriteIndex.IsTiled(path);
            info.sliced = !info.tiled && info.border != Vector4.zero;
        }

        public static bool PixelHash(string path, out long hash)
        {
            hash = 0;
            if (!SpriteIndex.TryReadPixels(path, out var pixels, out int w, out int h)) return false;
            hash = PixelUtil.Hash(pixels, w, h); return true;
        }

        public static void BindOverride(PsdNode node, RectInt? layout = null)
        {
            var asset = node.resourceOverride;
            if (!(asset is Sprite) && !(asset is Texture2D)) throw new InvalidOperationException("指定图片只支持项目内 Sprite 或 Texture2D：" + node.goName);
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("指定图片必须是已保存的项目资源。");
            ApplyReference(node.image, path);
            node.image.useRawImage = asset is Texture2D;
            node.image.fxBaked = false;
            node.image.renderOpacity = node.opacity;
            node.image.trimmedRect = layout ?? node.psdRect;
            node.image.logicalRect = layout ?? node.psdRect;
            node.image.reusedFromPath = path;
            if (asset is Sprite sprite)
            {
                node.image.border = sprite.border;
                node.image.sliced = !node.image.tiled && sprite.border != Vector4.zero;
            }
            else { node.image.border = Vector4.zero; node.image.sliced = node.image.tiled = false; }
        }
    }
}
