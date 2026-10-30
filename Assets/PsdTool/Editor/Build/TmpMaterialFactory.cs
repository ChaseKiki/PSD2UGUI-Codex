using System.Globalization;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    /// <summary>按（字体、描边、投影）参数复用 TMP 材质预设，参数量化避免材质爆炸。</summary>
    public static class TmpMaterialFactory
    {
        public static Material Get(TMP_FontAsset font, TextInfo text, PsdToolSettings settings)
        {
            if (font == null || font.material == null) return null;
            bool outline = text.outline != null && text.outline.enabled && text.outline.size > 0.01f;
            bool shadow = text.shadow != null && text.shadow.enabled && (text.shadow.distance > 0.01f || text.shadow.size > 0.01f);
            if (!outline && !shadow) return font.material;

            var outlineInfo = text.outline ?? new TextEffectInfo();
            var shadowInfo = text.shadow ?? new TextEffectInfo();
            float size = outline ? Quantize(outlineInfo.size, 0.5f) : 0;
            float dist = shadow ? Quantize(shadowInfo.distance, 0.5f) : 0;
            float soft = shadow ? Quantize(shadowInfo.size, 0.5f) : 0;
            float angle = shadow ? Quantize(shadowInfo.angle, 1f) : 0;
            var oc = outlineInfo.color;
            var sc = shadowInfo.color;
            oc.a = (byte)Mathf.Clamp(Mathf.RoundToInt(outlineInfo.opacity * 255f), 0, 255);
            sc.a = (byte)Mathf.Clamp(Mathf.RoundToInt(shadowInfo.opacity * 255f), 0, 255);

            string fontGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(font));
            string name = string.Format(CultureInfo.InvariantCulture, "{0}_{1}_fx2_o{2}_{3}_{4}_s{5}_{6}_{7}_{8}_a{9}",
                font.name, fontGuid, outline ? 1 : 0, size, ColorUtility.ToHtmlStringRGBA(oc), shadow ? 1 : 0,
                dist, soft, ColorUtility.ToHtmlStringRGBA(sc), angle);
            name = PathUtil.SanitizeFileName(name);
            string folder = string.IsNullOrEmpty(settings.materialPresetFolder)
                ? PsdToolSettings.DefaultRuntimeMaterials
                : settings.materialPresetFolder;
            PathUtil.EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(font.material) { name = name };
            if (outline)
            {
                mat.EnableKeyword("OUTLINE_ON");
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth(font, size));
                mat.SetColor(ShaderUtilities.ID_OutlineColor, oc);
                // 外描边补偿：略微膨胀字形，避免看起来变细
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, OutlineWidth(font, size) * 0.5f);
            }
            if (shadow)
            {
                mat.EnableKeyword("UNDERLAY_ON");
                float rad = angle * Mathf.Deg2Rad;
                float ox = Mathf.Cos(rad) * dist;
                float oy = Mathf.Sin(rad) * dist;
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, ox / Mathf.Max(1f, font.faceInfo.pointSize));
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -oy / Mathf.Max(1f, font.faceInfo.pointSize));
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, OutlineWidth(font, soft));
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(soft / 8f));
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, sc);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static float Quantize(float v, float step) => Mathf.Round(v / step) * step;

        /// <summary>像素描边宽度 → TMP 归一化 outline（与字体采样字号相关，近似）。</summary>
        static float OutlineWidth(TMP_FontAsset font, float pixels)
        {
            float face = font.faceInfo.pointSize > 0 ? font.faceInfo.pointSize : 72f;
            return Mathf.Clamp(pixels / face, 0f, 0.5f);
        }
    }
}
