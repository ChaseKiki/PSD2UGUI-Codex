using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PsdTool.Parser;
using UnityEngine;

namespace PsdTool
{
    /// <summary>PSD 文档 → 中间节点树。重新解析时保留人工修改、锁定和上次生成的信息。</summary>
    public static class TreeBuilder
    {
        public static void Build(PsdDocument doc, PsdTree tree, PsdToolSettings settings)
        {
            var previous = new Dictionary<int, PsdNode>();
            foreach (var n in tree.nodes) previous[n.layerId] = n;

            var seen = new HashSet<int>();
            tree.nodes = new List<PsdNode>();
            tree.warnings = new List<string>(doc.Warnings);
            tree.docSize = new Vector2Int(doc.Width, doc.Height);
            tree.psdStamp = Stamp(doc.FilePath);

            var design = settings.designResolution;
            if (doc.Width != design.x || doc.Height != design.y)
                tree.warnings.Add($"PSD 尺寸为 {doc.Width}×{doc.Height}，与设计分辨率 {design.x}×{design.y} 不一致");

            AddChildren(doc.Roots, -1, tree, previous, seen);

            var groups = new HashSet<int>(tree.nodes.Where(n => n.aiHasParentOverride).Select(n => n.aiParentId));
            bool changed;
            do
            {
                changed = false;
                foreach (var old in previous.Values)
                    if (old.aiGeneratedGroup && groups.Contains(old.layerId) && old.parentId != -1 && groups.Add(old.parentId)) changed = true;
            } while (changed);
            foreach (var old in previous.Values)
                if (old.aiGeneratedGroup && groups.Contains(old.layerId))
                { old.status = old.status == NodeStatus.PendingDelete ? NodeStatus.Synced : old.status; tree.nodes.Add(old); seen.Add(old.layerId); }
            foreach (var node in tree.nodes)
                if (node.aiHasParentOverride)
                {
                    if (node.aiParentId == -1 || seen.Contains(node.aiParentId)) node.parentId = node.aiParentId;
                    else { node.aiHasParentOverride = false; tree.warnings.Add("AI 父组已丢失，恢复 PSD 原层级：" + node.goName); }
                }

            foreach (var old in previous.Values)
            {
                if (seen.Contains(old.layerId) || old.status == NodeStatus.New) continue;
                old.status = NodeStatus.PendingDelete;
                tree.nodes.Add(old);
            }
        }

        public static string Stamp(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.Length}-{info.LastWriteTimeUtc.Ticks}" : string.Empty;
        }

        static void AddChildren(List<PsdLayer> layers, int parentId, PsdTree tree, Dictionary<int, PsdNode> previous, HashSet<int> seen)
        {
            for (int i = 0; i < layers.Count; i++)
                AddNode(layers[i], parentId, i, tree, previous, seen);
        }

        static void AddNode(PsdLayer layer, int parentId, int order, PsdTree tree, Dictionary<int, PsdNode> previous, HashSet<int> seen)
        {
            var markers = new List<string>();
            string baseName = Markers.Parse(layer.Name, markers);
            if (Markers.Has(markers, "ignore")) return;

            if (layer.Kind == PsdLayerKind.Adjustment)
            {
                tree.warnings.Add($"调整图层“{layer.Name}”不支持，已跳过。请在 PS 中把效果合并到下方图层。");
                return;
            }

            bool merge = layer.Kind == PsdLayerKind.Group && Markers.Has(markers, "merge");
            var node = new PsdNode
            {
                layerId = layer.Id,
                stableKey = "layer:" + layer.Id,
                parentId = parentId,
                order = order,
                psdName = baseName,
                markers = markers,
                kind = merge ? LayerKind.Image : MapKind(layer.Kind),
                psdRect = layer.Bounds,
                visible = layer.Visible,
                opacity = layer.Opacity / 255f * (layer.Kind == PsdLayerKind.Group ? 1f : layer.FillOpacity / 255f),
                blendKey = layer.BlendKey,
                clipping = layer.Clipping,
                hasEffects = layer.HasEffects,
                templateId = layer.Kind == PsdLayerKind.Group && !merge ? "Group" : string.Empty,
            };

            ApplyLayerMarkers(node, markers);

            node.goName = Markers.TryGetArg(markers, "name", out var customName) && !string.IsNullOrWhiteSpace(customName)
                ? customName
                : string.IsNullOrWhiteSpace(baseName) ? $"{node.kind}_{node.layerId}" : baseName;

            if (layer.Kind == PsdLayerKind.Text && layer.Text != null)
            {
                node.text = ConvertText(layer.Text, layer.Bounds);
                EffectsReader.ApplyToText(layer, node.text, tree.warnings);
            }

            CollectWarnings(layer, node, tree.warnings);

            if (previous.TryGetValue(layer.Id, out var prev))
            {
                if (!string.IsNullOrEmpty(prev.stableKey)) node.stableKey = prev.stableKey;
                node.generatedAssetGuid = prev.generatedAssetGuid;
                node.resourceOverride = prev.resourceOverride;
                ScrollLayoutOptions.Copy(prev, node);
                node.aiHasParentOverride = prev.aiHasParentOverride;
                node.aiParentId = prev.aiParentId;
                node.image = prev.image ?? new ImageInfo();
                node.locks = prev.locks;
                node.forceUpdateGroups = prev.forceUpdateGroups;
                node.snapshotJson = prev.snapshotJson;
                node.goGlobalId = prev.goGlobalId;
                node.status = prev.status == NodeStatus.PendingDelete ? NodeStatus.Synced : prev.status;
                if (prev.source == DecisionSource.Manual || prev.source == DecisionSource.AI && !AiProtocol.Protected(node))
                    RestoreDecisions(node, prev);
                else if (prev.source == DecisionSource.AI) node.aiHasParentOverride = false;
            }
            else
            {
                node.status = NodeStatus.New;
            }

            seen.Add(layer.Id);
            tree.nodes.Add(node);

            if (layer.Kind == PsdLayerKind.Group && !merge)
                AddChildren(layer.Children, layer.Id, tree, previous, seen);
        }

        static void ApplyLayerMarkers(PsdNode node, List<string> markers)
        {
            foreach (var m in markers)
            {
                string key = Markers.KeyOf(m);
                if (Markers.TypeToTemplate.TryGetValue(key, out var template))
                {
                    node.templateId = template;
                    node.source = DecisionSource.Marker;
                }
                else if (Markers.Slots.Contains(key))
                {
                    node.slot = key;
                }
            }

            if (Markers.TryGetArg(markers, "anchor", out var anchor) && !string.IsNullOrEmpty(anchor))
                node.anchorPreset = anchor.ToLowerInvariant();
            if (Markers.Has(markers, "bind")) node.generateBind = true;
            if (Markers.Has(markers, "safe")) node.underSafeArea = true;
            if (Markers.Has(markers, "no9s")) node.nineOverride = NineSliceOverride.Forbid;
            else if (Markers.TryGetArg(markers, "9s", out var nine))
            {
                node.nineOverride = NineSliceOverride.Force;
                if (!string.IsNullOrEmpty(nine) && TryParseBorder(nine, out var border))
                    node.nineBorder = border;
            }
        }

        static void RestoreDecisions(PsdNode node, PsdNode prev)
        {
            node.templateId = prev.templateId;
            node.slot = prev.slot;
            node.goName = prev.goName;
            node.anchorPreset = prev.anchorPreset;
            node.generateBind = prev.generateBind;
            node.underSafeArea = prev.underSafeArea;
            node.nineOverride = prev.nineOverride;
            node.nineBorder = prev.nineBorder;
            node.pressScale = prev.pressScale;
            node.progressFill = prev.progressFill; node.progressReverse = prev.progressReverse; node.progressClockwise = prev.progressClockwise;
            node.progressOrigin = prev.progressOrigin; node.progressValue = prev.progressValue;
            node.source = prev.source;
            node.confidence = prev.confidence;
        }

        static bool TryParseBorder(string arg, out Vector4 border)
        {
            border = Vector4.zero;
            var parts = arg.Split(new[] { ',', '，' });
            if (parts.Length != 4) return false;
            if (!float.TryParse(parts[0], out float l)) return false;
            if (!float.TryParse(parts[1], out float t)) return false;
            if (!float.TryParse(parts[2], out float r)) return false;
            if (!float.TryParse(parts[3], out float b)) return false;
            // 标记是 左,上,右,下 → Unity spriteBorder 是 左,下,右,上
            border = new Vector4(l, b, r, t);
            return true;
        }

        static LayerKind MapKind(PsdLayerKind kind)
        {
            switch (kind)
            {
                case PsdLayerKind.Group: return LayerKind.Group;
                case PsdLayerKind.Text: return LayerKind.Text;
                case PsdLayerKind.Fill: return LayerKind.Fill;
                case PsdLayerKind.SmartObject: return LayerKind.SmartObject;
                default: return LayerKind.Image;
            }
        }

        static void CollectWarnings(PsdLayer layer, PsdNode node, List<string> warnings)
        {
            string name = layer.Name;
            if (layer.BlendKey != "norm" && layer.BlendKey != "pass")
                warnings.Add($"图层“{name}”使用了混合模式 {layer.BlendKey.Trim()}，UGUI 不支持，将按正常模式显示");
            if (layer.Clipping)
                warnings.Add($"图层“{name}”是剪贴蒙版，当前按普通图层导出，请在 PS 中合并或栅格化");

            if (layer.HasEffects && layer.Kind != PsdLayerKind.Text)
                warnings.Add(node.kind == LayerKind.Group
                    ? $"组“{name}”带 FX：需要保留子节点时不能直接整组烘焙；可显式标记 @merge 导成图片，或在 PS 调整样式归属。"
                    : $"图片“{name}”带 FX：导出时按设置通过 Photoshop 临时副本烘焙；关闭烘焙会丢失图片样式。");

            if (layer.Kind == PsdLayerKind.Text && layer.Text != null)
            {
                if (Math.Abs(layer.Text.Xy) > 1e-4 || Math.Abs(layer.Text.Yx) > 1e-4)
                    warnings.Add($"文字“{name}”有旋转或斜切变换，当前只还原缩放");
                if (layer.Width == 0 || layer.Height == 0)
                    warnings.Add($"文字“{name}”内容为空");
            }
            else if (layer.Kind != PsdLayerKind.Group && (layer.Width == 0 || layer.Height == 0))
            {
                warnings.Add($"图层“{name}”是空图层，已跳过");
            }
        }

        static TextInfo ConvertText(PsdText t, RectInt bounds)
        {
            double scaleX = Math.Sqrt(t.Xx * t.Xx + t.Xy * t.Xy);
            double scaleY = Math.Sqrt(t.Yx * t.Yx + t.Yy * t.Yy);
            if (scaleX <= 0) scaleX = 1;
            if (scaleY <= 0) scaleY = 1;

            var baseRun = t.Runs[0];
            foreach (var r in t.Runs)
                if (r.Length > baseRun.Length) baseRun = r;

            var info = new TextInfo
            {
                content = t.Content,
                font = baseRun.Font,
                fontSize = Round(baseRun.FontSize * scaleY),
                color = baseRun.Color,
                bold = baseRun.FauxBold,
                italic = baseRun.FauxItalic,
                tracking = (float)baseRun.Tracking,
                leading = Round(baseRun.Leading * scaleY),
                autoLeading = baseRun.AutoLeading,
                justification = t.Justification,
                isBox = t.IsBox,
                lineCount = t.Content.Split('\n').Length,
            };

            int start = 0;
            foreach (var r in t.Runs)
            {
                info.runs.Add(new TextRunInfo
                {
                    start = start,
                    length = r.Length,
                    font = r.Font,
                    fontSize = Round(r.FontSize * scaleY),
                    color = r.Color,
                    bold = r.FauxBold,
                    italic = r.FauxItalic,
                });
                start += r.Length;
            }

            if (t.IsBox)
            {
                info.rect = new Rect(
                    (float)(t.Tx + t.BoxLeft * scaleX),
                    (float)(t.Ty + t.BoxTop * scaleY),
                    (float)((t.BoxRight - t.BoxLeft) * scaleX),
                    (float)((t.BoxBottom - t.BoxTop) * scaleY));
            }
            else
            {
                float lineHeight = info.autoLeading || info.leading <= 0 ? info.fontSize * 1.2f : info.leading;
                float pad = Mathf.Ceil(info.fontSize * 0.25f);
                float w = bounds.width + pad * 2f;
                float h = Mathf.Max(bounds.height, info.lineCount * lineHeight);
                float x;
                switch (t.Justification)
                {
                    case 1: x = bounds.xMax + pad - w; break;
                    case 2: x = bounds.center.x - w / 2f; break;
                    default: x = bounds.x - pad; break;
                }
                info.rect = new Rect(Mathf.Round(x), Mathf.Round(bounds.center.y - h / 2f), Mathf.Ceil(w), Mathf.Ceil(h));
            }

            return info;
        }

        static float Round(double v) => (float)Math.Round(v, 1);
    }
}
