using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    public enum AiBackend { Ollama, CompatibleChat, CodexTask }
    [Serializable]
    public class FontMapping
    {
        [Tooltip("PS 中的字体 PostScript 名称，例如 SourceHanSansCN-Bold")]
        public string psFontName;
        public TMP_FontAsset fontAsset;
    }

    public class PsdToolSettings : ScriptableObject
    {
        public const string DefaultPath = "Assets/PsdTool/PsdToolSettings.asset";
        public const string LegacyRoot = "Assets/UI/_PsdTool";
        public const string DefaultDataRoot = "Assets/PsdTool/Editor/Data";
        public const string DefaultRuntimeMaterials = "Assets/PsdTool/Runtime/Generated/MaterialPresets";

        [Header("持久编辑数据（不随 Player 构建）")]
        [Tooltip("节点树、预览图、图片索引。换项目时不用改。")]
        public string toolDataRoot = DefaultDataRoot;

        [Header("界面输出（{module} 模块名，{panel} 界面名）")]
        public string panelSprites = "Assets/UI/Modules/{module}/{panel}/Sprites";
        public string panelTextures = "Assets/UI/Modules/{module}/{panel}/Textures";
        public string panelPrefabs = "Assets/UI/Modules/{module}/{panel}/Prefabs";
        public string panelAtlas = "Assets/UI/Modules/{module}/{panel}/Atlas/{panel}.spriteatlas";

        [Header("模块内共用")]
        public string moduleSprites = "Assets/UI/Modules/{module}/Common/Sprites";
        public string moduleAtlas = "Assets/UI/Modules/{module}/Common/Atlas/Common.spriteatlas";

        [Header("全局共用")]
        public string commonSprites = "Assets/UI/Common/Sprites";
        public string commonTextures = "Assets/UI/Common/Textures";
        public string commonAtlas = "Assets/UI/Common/Atlas/Common.spriteatlas";

        [Header("适配")]
        public Vector2Int designResolution = new Vector2Int(750, 1334);
        [Range(0f, 1f)] public float canvasMatch;

        [Header("资源")]
        [Tooltip("任一边超过该值的图放进 Textures，用 RawImage，不进图集")]
        public int largeImageThreshold = 512;
        [Header("新建贴图与图集（已有手工设置默认保留）")]
        public int textureMaxSize = 2048;
        public FilterMode textureFilter = FilterMode.Bilinear;
        public TextureImporterCompression textureCompression = TextureImporterCompression.Compressed;
        public List<TexturePlatformRule> texturePlatforms = new List<TexturePlatformRule>
        {
            new TexturePlatformRule { platform = "Android" },
            new TexturePlatformRule { platform = "iPhone" }
        };
        public int atlasMaxSize = 2048;
        public int atlasPadding = 4;
        public bool atlasIncludeInBuild = true;
        [Tooltip("疑似无用资源移入该目录，保留 GUID，可在资源管理窗口恢复")]
        public string resourceRecycleFolder = DefaultDataRoot + "/ResourceRecycle";
        [Tooltip("非文字图片有图层样式时，通过本机 Photoshop 临时副本烘焙。失败时停止导出，避免静默丢失 FX。")]
        public bool bakeImageEffects = true;
        public int photoshopTimeoutSeconds = 180;

        [Header("控件")]
        public int minHitSize = 88;
        [Tooltip("可选附加表现；默认不生成。单个按钮可在节点面板单独启用缩放。")]
        public bool addButtonPressScale;
        public bool addButtonGray;
        [Range(0.5f, 1f)] public float buttonPressScale = 0.95f;
        public float buttonPressDuration = 0.1f;
        [Tooltip("距父节点边缘 ≤ 该值时推断锚到该边")]
        public float anchorSnap = 40f;
        [Range(0f, 1f)]
        [Tooltip("规则识别置信度低于此值时只进建议列表，不自动应用")]
        public float ruleAutoApplyMin = 0.8f;

        // 旧配置序列化兼容字段，不再提供本地模型/接口入口。
        [HideInInspector] public bool aiEnabled;
        [HideInInspector] public AiBackend aiBackend = AiBackend.CodexTask;
        [HideInInspector] public int aiTimeoutSeconds = 300;
        [Header("Codex 识别")]
        [InspectorName("识别图片最大边长"), Range(512, 1536)] public int aiImageMaxSide = 1024;
        [HideInInspector] public int aiLayerPreviews = 64;
        [HideInInspector] public bool aiAutoApplyHighConfidence = false;
        [InspectorName("允许控件结构调整")] public bool aiAllowStructure = true;
        [HideInInspector] public int aiContextSize = 4096;
        [HideInInspector] public int aiOutputTokens = 4096;
        [HideInInspector] public string aiEndpoint = "http://127.0.0.1:11434/v1/chat/completions";
        [HideInInspector] public string aiModel = "qwen3-vl:4b-instruct";
        [InspectorName("默认勾选置信度"), Range(0f, 1f)]
        [Tooltip("达到此置信度的有效建议默认勾选，仍需人工确认后应用。0 表示使用 0.85")]
        public float aiAutoApplyMin = 0.85f;

        /// <summary>API Key 只留在本机 EditorPrefs，不写进设置资产。</summary>
        public const string AiApiKeyPref = "PsdTool.AiApiKey";

        [Header("字体")]
        [Tooltip("没有映射时使用；为空则使用 TMP Settings 的默认字体")]
        public TMP_FontAsset defaultFont;
        public List<FontMapping> fontMappings = new List<FontMapping>();
        [Tooltip("TMP 材质预设输出目录")]
        public string materialPresetFolder = DefaultRuntimeMaterials;

        public string TreesFolder => $"{toolDataRoot}/Trees";
        public string PreviewsFolder => $"{toolDataRoot}/Cache/Previews";
        public string IndexPath => $"{toolDataRoot}/Index/SpriteIndex.asset";

        public string Expand(string template, string module, string panel)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return template.Replace("{module}", module ?? string.Empty).Replace("{panel}", panel ?? string.Empty)
                .Replace('\\', '/').TrimEnd('/');
        }

        public string PrefabPath(string module, string panel) =>
            $"{Expand(panelPrefabs, module, panel)}/{panel}.prefab";

        /// <summary>索引扫描的根目录：路径模板里第一个占位符之前的部分。</summary>
        public List<string> ScanRoots()
        {
            var roots = new List<string>();
            foreach (var template in new[] { panelSprites, panelTextures, moduleSprites, commonSprites, commonTextures })
                AddRoot(roots, template);
            return roots;
        }

        static void AddRoot(List<string> roots, string template)
        {
            if (string.IsNullOrEmpty(template)) return;
            int brace = template.IndexOf('{');
            string root = (brace >= 0 ? template.Substring(0, brace) : template).Replace('\\', '/').TrimEnd('/');
            if (root.Length == 0 || roots.Contains(root)) return;
            roots.Add(root);
        }

        public SpriteScope Classify(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (Under(assetPath, commonSprites) || Under(assetPath, commonTextures)) return SpriteScope.Common;
            if (Match(assetPath, moduleSprites)) return SpriteScope.Module;
            if (Match(assetPath, panelSprites) || Match(assetPath, panelTextures)) return SpriteScope.Panel;
            return SpriteScope.Other;
        }

        static bool Under(string path, string folder)
        {
            folder = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            return folder.Length > 0 && (path == folder || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
        }

        static bool Match(string path, string template)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return Under(path, template);
            string pattern = "^" + Regex.Escape(template.Replace('\\', '/')).Replace(@"\{module\}", "[^/]+").Replace(@"\{panel\}", "[^/]+") + "(/|$)";
            return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase);
        }

        public TMP_FontAsset FindFont(string psFontName, out bool mapped)
        {
            foreach (var m in fontMappings)
            {
                if (m.fontAsset != null && string.Equals(m.psFontName, psFontName, StringComparison.OrdinalIgnoreCase))
                {
                    mapped = true;
                    return m.fontAsset;
                }
            }

            mapped = false;
            if (defaultFont != null) return defaultFont;
            return TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
        }

        public static PsdToolSettings TryGet()
        {
            var atDefault = AssetDatabase.LoadAssetAtPath<PsdToolSettings>(DefaultPath);
            if (atDefault != null) return atDefault;
            var guids = AssetDatabase.FindAssets("t:" + nameof(PsdToolSettings));
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<PsdToolSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        public static PsdToolSettings Load()
        {
            // 已有工程只通过迁移清单确认后搬迁，Load 不再隐式移动数据。
            var settings = TryGet();
            if (settings != null) return settings;

            settings = CreateInstance<PsdToolSettings>();
            PathUtil.EnsureFolder(Path.GetDirectoryName(DefaultPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(settings, DefaultPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>把第一阶段放在 UI 目录里的工具资产挪到工具目录，GUID 保持不变。</summary>
        public static void MigrateLegacy()
        {
            if (!AssetDatabase.IsValidFolder(LegacyRoot)) return;
            PathUtil.EnsureFolder("Assets/PsdTool");
            PathUtil.EnsureFolder(DefaultDataRoot);
            Move($"{LegacyRoot}/PsdToolSettings.asset", DefaultPath);
            Move($"{LegacyRoot}/Trees", $"{DefaultDataRoot}/Trees");
            Move($"{LegacyRoot}/Previews", $"{DefaultDataRoot}/Previews");
            if (AssetDatabase.IsValidFolder(LegacyRoot)
                && AssetDatabase.GetSubFolders(LegacyRoot).Length == 0
                && AssetDatabase.FindAssets(string.Empty, new[] { LegacyRoot }).Length == 0)
                AssetDatabase.DeleteAsset(LegacyRoot);
        }

        static void Move(string from, string to)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(from)) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(to))) return;
            PathUtil.EnsureFolder(Path.GetDirectoryName(to)?.Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"移动 {from} 失败：{error}");
        }
    }
}
