using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace PsdTool
{
    public static class AtlasBuilder
    {
        public static void Update(PsdToolSettings settings, string module, string panel, bool applyProfile = false)
        {
            ResourceImportPolicy.Validate(settings);
            var pairs = new[]
            {
                new[] { settings.Expand(settings.panelAtlas, module, panel), settings.Expand(settings.panelSprites, module, panel) },
                new[] { settings.Expand(settings.moduleAtlas, module, panel), settings.Expand(settings.moduleSprites, module, panel) },
                new[] { settings.commonAtlas, settings.commonSprites }
            };
            if (pairs.GroupBy(p => p[0]).Any(g => g.Select(p => p[1]).Distinct().Count() > 1))
                throw new InvalidOperationException("不同收集目录不能配置成同一个图集路径。");
            if (pairs.GroupBy(p => p[1]).Any(g => g.Select(p => p[0]).Distinct().Count() > 1))
                throw new InvalidOperationException("同一收集目录不能同时输出到多个图集。");
            for (int i = 0; i < pairs.Length; i++)
                for (int j = i + 1; j < pairs.Length; j++)
                    if (pairs[i][1] != pairs[j][1] && (Under(pairs[i][1], pairs[j][1]) || Under(pairs[j][1], pairs[i][1])))
                        throw new InvalidOperationException("图集收集目录不能嵌套，否则图片会被重复打包：" + pairs[i][1] + " / " + pairs[j][1]);
            foreach (var pair in pairs.GroupBy(p => p[0]).Select(g => g.First())) Pack(pair[0], pair[1], settings, applyProfile);
        }

        static bool Under(string path, string folder) => path.Replace('\\', '/').StartsWith(folder.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

        static void Pack(string atlasPath, string folderPath, PsdToolSettings settings, bool applyProfile)
        {
            if (!AssetDatabase.IsValidFolder(folderPath)) return;
            var textures = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Under(path, settings.resourceRecycleFolder)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.textureType != TextureImporterType.Sprite) continue;
                importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                if (Math.Max(w, h) > settings.largeImageThreshold) continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) textures.Add(texture);
            }
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null && textures.Count == 0) return;
            var catalog = ResourceCatalog.Load(settings);
            bool isNew = atlas == null;
            if (isNew)
            {
                PathUtil.EnsureFolder(Path.GetDirectoryName(atlasPath).Replace('\\', '/'));
                atlas = new SpriteAtlas(); AssetDatabase.CreateAsset(atlas, atlasPath);
            }
            string atlasGuid = AssetDatabase.AssetPathToGUID(atlasPath);
            if (!catalog.atlasGuids.Contains(atlasGuid))
            {
                if (!isNew && atlas.GetPackables().Any(p => p != null && AssetDatabase.GetAssetPath(p) != folderPath && !Under(AssetDatabase.GetAssetPath(p), folderPath)))
                    throw new InvalidOperationException("该图集含本工具收集范围外的内容，不会接管，请更换输出路径：" + atlasPath);
                catalog.atlasGuids.Add(atlasGuid); EditorUtility.SetDirty(catalog);
            }
            string before = EditorJsonUtility.ToJson(atlas);
            var existing = atlas.GetPackables();
            if (!existing.SequenceEqual(textures))
            {
                if (existing.Length != 0) atlas.Remove(existing);
                if (textures.Count != 0) atlas.Add(textures.ToArray());
            }
            var packing = atlas.GetPackingSettings();
            packing.enableRotation = false; packing.enableTightPacking = false;
            if (isNew || applyProfile) packing.padding = settings.atlasPadding;
            atlas.SetPackingSettings(packing);
            if (isNew || applyProfile)
            {
                var texture = atlas.GetTextureSettings();
                texture.readable = false; texture.generateMipMaps = false; texture.sRGB = true;
                texture.filterMode = settings.textureFilter; atlas.SetTextureSettings(texture);
                ApplyProfile(atlas, settings);
            }
            if (before != EditorJsonUtility.ToJson(atlas)) { EditorUtility.SetDirty(atlas); AssetDatabase.SaveAssetIfDirty(atlas); }
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        internal static void ApplyProfile(SpriteAtlas atlas, PsdToolSettings settings)
        {
            var defaults = atlas.GetPlatformSettings("DefaultTexturePlatform");
            defaults.maxTextureSize = settings.atlasMaxSize; defaults.crunchedCompression = false;
            defaults.textureCompression = settings.textureCompression; atlas.SetPlatformSettings(defaults);
            foreach (var rule in settings.texturePlatforms)
            {
                var platform = atlas.GetPlatformSettings(rule.platform);
                platform.name = rule.platform; platform.overridden = rule.overridden;
                platform.maxTextureSize = Math.Min(rule.maxTextureSize, settings.atlasMaxSize);
                platform.format = rule.format; platform.compressionQuality = rule.compressionQuality;
                atlas.SetPlatformSettings(platform);
            }
            atlas.SetIncludeInBuild(settings.atlasIncludeInBuild);
        }
    }
}
