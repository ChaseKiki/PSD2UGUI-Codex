using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    [Serializable]
    public sealed class TexturePlatformRule
    {
        public string platform = "Android";
        public bool overridden = true;
        public int maxTextureSize = 2048;
        public TextureImporterFormat format = TextureImporterFormat.ASTC_6x6;
        [Range(0, 100)] public int compressionQuality = 50;
    }

    internal static class ResourceImportPolicy
    {
        public static void Apply(TextureImporter importer, PsdToolSettings settings)
        {
            importer.maxTextureSize = settings.textureMaxSize;
            importer.filterMode = settings.textureFilter;
            importer.textureCompression = settings.textureCompression;
            foreach (var rule in settings.texturePlatforms)
            {
                var platform = importer.GetPlatformTextureSettings(rule.platform);
                platform.name = rule.platform; platform.overridden = rule.overridden;
                platform.maxTextureSize = rule.maxTextureSize; platform.format = rule.format;
                platform.compressionQuality = rule.compressionQuality;
                importer.SetPlatformTextureSettings(platform);
            }
        }
        public static void Validate(PsdToolSettings settings)
        {
            foreach (string path in new[] { settings.toolDataRoot, settings.panelSprites, settings.panelTextures, settings.panelPrefabs,
                settings.panelAtlas, settings.moduleSprites, settings.moduleAtlas, settings.commonSprites, settings.commonTextures,
                settings.commonAtlas, settings.resourceRecycleFolder, string.IsNullOrEmpty(settings.materialPresetFolder) ? PsdToolSettings.DefaultRuntimeMaterials : settings.materialPresetFolder })
            {
                if (string.IsNullOrWhiteSpace(path) || !path.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.Replace('\\', '/').Split('/').Any(p => p == ".." || p == ".") || Path.IsPathRooted(path))
                    throw new InvalidOperationException("资源路径必须在 Assets 内，且不能包含 ../：" + path);
            }
            foreach (string path in new[] { settings.panelSprites, settings.panelTextures, settings.panelPrefabs, settings.panelAtlas,
                settings.moduleSprites, settings.moduleAtlas, settings.commonSprites, settings.commonTextures, settings.commonAtlas, settings.materialPresetFolder })
                if (!string.IsNullOrEmpty(path) && path.Replace('\\', '/').Split('/').Contains("Editor"))
                    throw new InvalidOperationException("运行素材/Prefab 不能输出到 Editor 目录：" + path);
            if (!Size(settings.textureMaxSize) || !Size(settings.atlasMaxSize)) throw new InvalidOperationException("贴图/图集上限必须为 32～16384 的 2 次幂。");
            var names = new HashSet<string>();
            foreach (var rule in settings.texturePlatforms)
            {
                if (rule == null || (rule.platform != "Android" && rule.platform != "iPhone" && rule.platform != "Standalone"))
                    throw new InvalidOperationException("纹理平台名支持 Android、iPhone、Standalone（iOS 使用 iPhone）。");
                if (!names.Add(rule.platform) || !Size(rule.maxTextureSize)) throw new InvalidOperationException("平台配置重复或贴图上限无效：" + rule.platform);
            }
        }
        static bool Size(int value) => value >= 32 && value <= 16384 && (value & (value - 1)) == 0;
    }
}
