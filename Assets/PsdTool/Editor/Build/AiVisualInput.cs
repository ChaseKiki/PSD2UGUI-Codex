using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PsdTool.Parser;
using UnityEngine;

namespace PsdTool
{
    internal sealed class AiVisualInput
    {
        public string signature, treeJson, note, reportFolder;
        public readonly List<string> images = new List<string>();
        public readonly List<AiRegion> regions = new List<AiRegion>();

        public static string Signature(PsdTree tree, PsdToolSettings settings)
        {
            if (tree == null || !File.Exists(tree.psdPath)) throw new InvalidOperationException("请先选择并解析可访问的 PSD 源文件。");
            string fileHash;
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(tree.psdPath)) fileHash = BitConverter.ToString(hash.ComputeHash(file));
            return AiProtocol.Hash(AiProtocol.Version + "|" + fileHash + "|" + TreeJson(tree) + "|" + AiProtocol.Endpoint(settings) + "|" + settings.aiModel + "|" +
                settings.aiBackend + "|" + settings.aiImageMaxSide + "|" + settings.aiLayerPreviews + "|" + settings.aiAllowStructure + "|" + settings.aiContextSize + "|" + settings.aiOutputTokens);
        }
        internal static string TaskSignature(PsdTree tree, PsdToolSettings settings, string expectedFileHash = null)
        {
            if (tree == null || settings == null || !File.Exists(tree.psdPath)) throw new InvalidOperationException("请先解析可访问的 PSD。");
            string fileHash;
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(tree.psdPath)) fileHash = BitConverter.ToString(hash.ComputeHash(file));
            if (expectedFileHash != null && !string.Equals(fileHash.Replace("-", ""), expectedFileHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PSD 来源已改变，请重新识别，不能沿用旧建议。");
            return AiProtocol.Hash("codex-task-v1|" + fileHash + "|" + UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(tree)) +
                "|" + TreeJson(tree) + "|" + settings.aiImageMaxSide + "|" + settings.aiAllowStructure + "|" + settings.designResolution + "|" + settings.minHitSize);
        }
        public static AiVisualInput Prepare(PsdTree tree, PsdToolSettings settings, bool externalTask = false)
        {
            var input = new AiVisualInput { signature = externalTask ? TaskSignature(tree, settings) : Signature(tree, settings), treeJson = TreeJson(tree) };
            var doc = PsdParser.Parse(tree.psdPath);
            using (var reader = new PsdPixelReader(doc))
            {
                var composite = reader.DecodeComposite();
                if (composite == null) throw new InvalidOperationException("PSD 没有合成预览图。请在 Photoshop 保存时启用“最大兼容”后重新解析；AI 不会使用旧切图或无图猜测。");
                input.images.Add(EncodeFit(composite, doc.Width, doc.Height, Mathf.Clamp(settings.aiImageMaxSide, 512, 1536)));
                input.regions.AddRange(AiRegions.Prepare(tree, composite, doc.Width, doc.Height, settings.aiImageMaxSide));
                var layers = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete && n.kind != LayerKind.Group)
                    .OrderBy(n => n.layerId).Select(n => doc.FindLayer(n.layerId)).Where(l => l != null && l.Kind != PsdLayerKind.Text && l.Width > 0 && l.Height > 0).ToList();
                if (externalTask && layers.Count > 1024) throw new InvalidOperationException("图片图层超过 1024，请按界面拆分 PSD 后识别，不会静默省略图层定位图。");
                int count = externalTask ? layers.Count : Math.Min(layers.Count, Mathf.Clamp(settings.aiLayerPreviews, 0, 128));
                const int cell = 112, columns = 8, perPage = 64;
                for (int start = 0; start < count; start += perPage)
                {
                    int length = Math.Min(perPage, count - start), width = columns * cell, height = ((length + columns - 1) / columns) * cell;
                    var sheet = new Color32[width * height];
                    for (int p = 0; p < sheet.Length; p++) sheet[p] = new Color32(235, 235, 235, 255);
                    for (int i = 0; i < length; i++)
                    {
                        var layer = layers[start + i]; var pixels = reader.DecodeLayer(layer);
                        if (pixels == null) continue;
                        int ox = i % columns * cell + 6, oy = i / columns * cell;
                        Number(sheet, width, ox, oy + 2, layer.Id);
                        float scale = Math.Min(1f, Math.Min(98f / layer.Width, 88f / layer.Height));
                        int w = Math.Max(1, (int)(layer.Width * scale)), h = Math.Max(1, (int)(layer.Height * scale));
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                        {
                            Color32 pixel = pixels[Math.Min(layer.Height - 1, y * layer.Height / h) * layer.Width + Math.Min(layer.Width - 1, x * layer.Width / w)];
                            byte background = (byte)(((x / 8 + y / 8) % 2 == 0) ? 210 : 245);
                            int a = pixel.a;
                            sheet[(oy + 20 + y) * width + ox + x] = new Color32((byte)((pixel.r * a + background * (255 - a)) / 255),
                                (byte)((pixel.g * a + background * (255 - a)) / 255), (byte)((pixel.b * a + background * (255 - a)) / 255), 255);
                        }
                    }
                    input.images.Add(Convert.ToBase64String(PixelUtil.EncodePng(sheet, width, height)));
                }
                input.note = $"当前 PSD 整图 1 张，图层缩略图 {count}/{layers.Count} 张（包含隐藏图片）；全部活动节点 {tree.nodes.Count(n => n.status != NodeStatus.PendingDelete)} 个。";
                input.note += $" 分区域识别 {input.regions.Count} 个候选组，使用来源像素边界定位，不使用图层名称判断。";
            }
            return input;
        }
        internal static string EncodeFit(Color32[] pixels, int width, int height, int maxSide)
        {
            float scale = Math.Min(1f, maxSide / (float)Math.Max(width, height));
            int w = Math.Max(1, (int)(width * scale)), h = Math.Max(1, (int)(height * scale));
            if (w == width && h == height) return Convert.ToBase64String(PixelUtil.EncodePng(pixels, width, height));
            var output = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) output[y * w + x] = pixels[(y * height / h) * width + x * width / w];
            return Convert.ToBase64String(PixelUtil.EncodePng(output, w, h));
        }
        internal static string TreeJson(PsdTree tree)
        {
            var output = new StringBuilder("[");
            foreach (var node in tree.nodes.Where(n => n.status != NodeStatus.PendingDelete).OrderBy(n => n.layerId))
            {
                if (output.Length > 1) output.Append(',');
                var rect = Bounds(tree, node, new HashSet<int>());
                output.Append("{\"id\":").Append(node.layerId).Append(",\"p\":").Append(node.parentId).Append(",\"k\":").Append((int)node.kind)
                    .Append(",\"r\":[").Append(Num(rect.x)).Append(',').Append(Num(rect.y)).Append(',').Append(Num(rect.width)).Append(',').Append(Num(rect.height)).Append(']')
                    .Append(",\"n\":").Append(AiProtocol.Quote(Short(node.goName, 32))).Append(",\"v\":").Append(node.visible ? 1 : 0);
                if (!string.IsNullOrEmpty(node.templateId) && node.templateId != "Group") output.Append(",\"t\":").Append(AiProtocol.Quote(node.templateId));
                if (!string.IsNullOrEmpty(node.slot)) output.Append(",\"s\":").Append(AiProtocol.Quote(node.slot));
                if (node.progressFill != ProgressFillMode.Auto) output.Append(",\"progress\":").Append(AiProtocol.Quote(node.progressFill + ":" + node.progressReverse + ":" + node.progressClockwise + ":" + node.progressOrigin + ":" + Num(node.progressValue)));
                if (AiProtocol.Protected(node)) output.Append(",\"locked\":true");
                if (!Mathf.Approximately(node.opacity, 1)) output.Append(",\"a\":").Append(Num(node.opacity));
                if (node.locks != PropGroup.None) output.Append(",\"locks\":").Append(AiProtocol.Quote(node.locks.ToString()));
                if (node.hasEffects) output.Append(",\"fx\":true");
                if (node.kind == LayerKind.Text) output.Append(",\"text\":").Append(AiProtocol.Quote(Short(node.text?.content, 64)));
                output.Append('}');
            }
            return output.Append(']').ToString();
        }
        static string Short(string value, int length) => value != null && value.Length > length ? value.Substring(0, length) : value ?? "";
        static string Num(float n) => n.ToString("0.##", CultureInfo.InvariantCulture);
        static Rect Bounds(PsdTree tree, PsdNode node, HashSet<int> stack)
        {
            if (!stack.Add(node.layerId)) throw new InvalidOperationException("节点树存在循环，无法进行 AI 识别。");
            Rect result;
            if (node.kind == LayerKind.Text && node.text != null) result = node.text.rect;
            else if (node.kind != LayerKind.Group || node.aiBoundsOverride) result = new Rect(node.psdRect.x, node.psdRect.y, node.psdRect.width, node.psdRect.height);
            else
            {
                Rect? combined = null;
                foreach (var child in tree.nodes.Where(n => n.parentId == node.layerId && n.status != NodeStatus.PendingDelete))
                {
                    var rect = Bounds(tree, child, stack);
                    if (rect.width > 0 && rect.height > 0) combined = combined.HasValue ? PrefabGenerator.Union(combined.Value, rect) : rect;
                }
                result = combined ?? new Rect();
            }
            stack.Remove(node.layerId); return result;
        }
        static readonly string[] Digits = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111" };
        static void Number(Color32[] pixels, int width, int x, int y, int value)
        {
            foreach (char digit in value.ToString(CultureInfo.InvariantCulture))
            {
                string glyph = digit == '-' ? "000000111000000" : Digits[digit - '0'];
                int scale = value.ToString(CultureInfo.InvariantCulture).Length <= 7 ? 3 : 2;
                for (int row = 0; row < 5; row++) for (int col = 0; col < 3; col++) if (glyph[row * 3 + col] == '1')
                    for (int yy = 0; yy < scale; yy++) for (int xx = 0; xx < scale; xx++) pixels[(y + row * scale + yy) * width + x + col * scale + xx] = new Color32(15, 15, 15, 255);
                x += 4 * scale;
            }
        }
    }
}
