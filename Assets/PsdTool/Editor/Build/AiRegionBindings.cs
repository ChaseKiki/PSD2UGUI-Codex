using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PsdTool
{
    /// <summary>模型只判断区域语义；引用由来源层级和几何推导，不采信模型生成的 LayerID。</summary>
    internal sealed class AiRegionBindings
    {
        readonly PsdTree tree;
        readonly AiControlProposal plan;
        readonly Dictionary<int, AiNodeEdit> edits = new Dictionary<int, AiNodeEdit>();
        AiRegionBindings(PsdTree tree, AiRegion region)
        {
            this.tree = tree;
            plan = new AiControlProposal { title = region.type + " · 区域 " + region.rootId, confidence = region.confidence,
                reason = "视觉判型；来源层级、包含关系与长短尺寸定位" };
        }
        internal static AiControlProposal Build(PsdTree tree, AiRegion region)
        {
            var builder = new AiRegionBindings(tree, region);
            var root = tree.Find(region.rootId) ?? throw new InvalidOperationException("候选根已丢失。");
            builder.Clear(root); builder.Control(root, region.type);
            switch (region.type)
            {
                case "Toggle": builder.Toggle(root); break;
                case "ScrollView": builder.Scroll(root); break;
                case "Dropdown": builder.Dropdown(root); break;
                case "InputField": builder.Simple(root, "placeholder"); break;
                case "Button": builder.Simple(root, "label"); break;
                case "Slider": builder.Linear(root, true); break;
                case "Progress": if (root.progressFill >= ProgressFillMode.Radial360) builder.Radial(root); else builder.Linear(root, false); break;
                default: throw new InvalidOperationException("本轮区域绑定尚未支持该类型，保留原节点。");
            }
            builder.plan.nodes = builder.edits.Values.OrderBy(e => e.layerId).ToList(); return builder.plan;
        }
        AiNodeEdit Edit(PsdNode node)
        {
            if (!edits.TryGetValue(node.layerId, out var edit)) edits[node.layerId] = edit = new AiNodeEdit { layerId = node.layerId };
            return edit;
        }
        void Clear(PsdNode root)
        {
            if (!AiProtocol.Protected(root))
            {
                var e = Edit(root); e.setTemplate = e.setSlot = true;
                e.templateId = root.kind == LayerKind.Group ? "Group" : ""; e.slot = "";
            }
            foreach (var child in Kids(root)) Clear(child);
        }
        void Control(PsdNode node, string type) { var e = Edit(node); e.setTemplate = true; e.templateId = type; }
        void Slot(PsdNode node, string role) { var e = Edit(node); e.setSlot = true; e.slot = role; }
        List<PsdNode> Parts(PsdNode root)
        {
            var result = new List<PsdNode>();
            foreach (var child in Kids(root))
                if (child.kind == LayerKind.Group && (string.IsNullOrEmpty(child.templateId) || child.templateId == "Group")) result.AddRange(Parts(child));
                else result.Add(child);
            return result;
        }
        void DirectSlot(PsdNode root, PsdNode node, string role)
        {
            Slot(node, role);
            if (node.parentId != root.layerId) { var edit = Edit(node); edit.setParent = true; edit.parentId = root.layerId; }
        }
        List<PsdNode> Kids(PsdNode n) => AiRegions.Children(tree, n.layerId);
        static bool Image(PsdNode n) => n.kind != LayerKind.Text && n.kind != LayerKind.Group;
        Rect R(PsdNode n) => AiRegions.Bounds(tree, n);
        float Area(PsdNode n) { var r = R(n); return r.width * r.height; }
        bool Contains(PsdNode outer, PsdNode inner, float tolerance = 3)
        {
            var a = R(outer); var b = R(inner);
            return b.xMin >= a.xMin - tolerance && b.yMin >= a.yMin - tolerance && b.xMax <= a.xMax + tolerance && b.yMax <= a.yMax + tolerance;
        }
        PsdNode Background(PsdNode root)
        {
            var images = Kids(root).Where(Image).OrderByDescending(Area).ToList();
            if (images.Count == 0 || Area(images[0]) <= 0) throw new InvalidOperationException("没有可定位的独立背景图层。");
            if (images.Count > 1 && Area(images[0]) < Area(images[1]) * 1.4f)
                throw new InvalidOperationException("背景存在近似等大的候选，需要人工确认，未自动猜测。");
            return images[0];
        }
        void Simple(PsdNode root, string textRole)
        {
            var parts = Parts(root);
            var images = parts.Where(Image).OrderByDescending(Area).ToList();
            // 多层同尺寸按钮外观无需猜哪张是底图，保留所有图片，以根节点热区交互。
            if (images.Count > 0 && (images.Count == 1 || Area(images[0]) >= Area(images[1]) * 1.4f)) DirectSlot(root, images[0], "bg");
            else if (textRole != "label") throw new InvalidOperationException("输入框背景存在多层候选，请在部件归属中指定 bg。");
            var texts = parts.Where(n => n.kind == LayerKind.Text).OrderByDescending(n => n.text?.fontSize ?? 0).ToList();
            if (texts.Count == 1) DirectSlot(root, texts[0], textRole);
            else if (textRole == "label")
            { if (texts.Count > 1 && (texts[0].text?.fontSize ?? 0) > (texts[1].text?.fontSize ?? 0) * 1.2f) DirectSlot(root, texts[0], "label"); }
            else if (texts.Count > 1) throw new InvalidOperationException("输入文字/提示文字需明确部件归属；保留全部文字等待确认。");
        }
        void Radial(PsdNode root)
        {
            var images = Parts(root).Where(Image).ToList();
            var fill = images.Find(n => n.slot == "fill");
            var track = images.Find(n => n.slot == "track" || n.slot == "bg");
            if (fill == null)
            {
                var candidates = images.Where(n => n != track).ToList();
                if (candidates.Count != 1) throw new InvalidOperationException("环形填充候选不唯一，请先为填充图片指定 fill 插槽，再重校验。");
                fill = candidates[0];
            }
            DirectSlot(root, fill, "fill");
            if (track != null && track != fill) DirectSlot(root, track, "track");
        }
        void Linear(PsdNode root, bool interactive)
        {
            var images = Parts(root).Where(Image).OrderByDescending(Area).ToList();
            var tracks = images.Where(n => { var r = R(n); return Mathf.Max(r.width, r.height) >= Mathf.Min(r.width, r.height) * 2; }).ToList();
            if (!interactive && root.progressFill != ProgressFillMode.Auto) tracks = images;
            if (tracks.Count == 0) throw new InvalidOperationException("暂不支持：环形/不明确方向的进度条，保留视觉节点。");
            var track = tracks[0]; bool horizontal = R(track).width >= R(track).height;
            var fills = images.Where(n => n != track && Contains(track, n, 4)).Where(n => {
                var r = R(n); var t = R(track); return horizontal ? r.height >= t.height * 0.4f && r.width > r.height * 1.5f : r.width >= t.width * 0.4f && r.height > r.width * 1.5f;
            }).ToList();
            if (fills.Count != 1) throw new InvalidOperationException("轨道/填充候选不唯一，需要确认 track/fill。");
            var fill = fills[0]; DirectSlot(root, track, "track"); DirectSlot(root, fill, "fill");
            if (!interactive) return;
            var handles = images.Where(n => n != track && n != fill).Where(n => {
                var r = R(n); var t = R(track);
                return horizontal ? r.center.x >= t.xMin - 4 && r.center.x <= t.xMax + 4 && Mathf.Abs(r.center.y - t.center.y) <= Mathf.Max(t.height, r.height) * 0.5f :
                    r.center.y >= t.yMin - 4 && r.center.y <= t.yMax + 4 && Mathf.Abs(r.center.x - t.center.x) <= Mathf.Max(t.width, r.width) * 0.5f;
            }).ToList();
            if (handles.Count != 1) throw new InvalidOperationException("Slider 必须有唯一可交互 handle，未降级冒充完整控件。");
            DirectSlot(root, handles[0], "handle");
        }
        void Toggle(PsdNode root)
        {
            var bg = Background(root); var others = Kids(root).Where(n => Image(n) && n != bg).ToList();
            var marks = others.Where(n => Contains(bg, n) && Area(n) < Area(bg) * 0.8f).ToList();
            if (marks.Count != 1) throw new InvalidOperationException("勾选图候选不唯一或不在底框内，未自动绑定。");
            Slot(bg, "bg"); Slot(marks[0], "checkmark");
            var texts = Kids(root).Where(n => n.kind == LayerKind.Text).ToList();
            if (texts.Count > 1) throw new InvalidOperationException("选项文字候选不唯一。");
            if (texts.Count == 1) Slot(texts[0], "label");
        }
        void Scroll(PsdNode root)
        {
            var bg = Background(root); Slot(bg, "bg"); var viewport = R(bg);
            var pieces = Kids(root).Where(n => Image(n) && n != bg).ToList();
            var used = new HashSet<int>();
            foreach (bool horizontal in new[] { false, true })
            {
                var pairs = new List<Tuple<PsdNode, PsdNode>>();
                foreach (var track in pieces)
                {
                    var t = R(track); float length = horizontal ? t.width : t.height, thickness = horizontal ? t.height : t.width;
                    float distance = horizontal ? Mathf.Min(Mathf.Abs(t.center.y - viewport.yMin), Mathf.Abs(t.center.y - viewport.yMax)) :
                        Mathf.Min(Mathf.Abs(t.center.x - viewport.xMin), Mathf.Abs(t.center.x - viewport.xMax));
                    if (thickness <= 0 || length < thickness * 3 || distance > Mathf.Max(18, thickness * 1.5f)) continue;
                    foreach (var handle in pieces.Where(n => n != track))
                    {
                        var h = R(handle); float hl = horizontal ? h.width : h.height, ht = horizontal ? h.height : h.width;
                        if (hl > 0 && hl < length * 0.8f && ht >= thickness * 0.5f && ht <= thickness * 1.5f && Contains(track, handle, 4)) pairs.Add(Tuple.Create(track, handle));
                    }
                }
                if (pairs.Count > 1) throw new InvalidOperationException("同方向轨道/滑块存在多套候选，请人工确认。");
                if (pairs.Count == 1)
                {
                    var pair = pairs[0];
                    if (!used.Add(pair.Item1.layerId) || !used.Add(pair.Item2.layerId)) throw new InvalidOperationException("横纵滚动条候选重叠。");
                    Slot(pair.Item1, horizontal ? "hbarbg" : "vbarbg"); Slot(pair.Item2, horizontal ? "hbar" : "vbar");
                }
            }
            if (pieces.Any(n => !used.Contains(n.layerId))) throw new InvalidOperationException("滚动区还有未能区分的图片，保留待确认，避免漏绑滚动条。");
            bool horizontalScroll = edits.Values.Any(e => e.slot == "hbar"), verticalScroll = edits.Values.Any(e => e.slot == "vbar");
            if (!horizontalScroll && !verticalScroll) verticalScroll = true;
            foreach (var item in Kids(root).Where(n => n.kind == LayerKind.Group))
            {
                var r = R(item);
                if (r.width <= 0 || r.height <= 0) continue;
                // 滚动方向允许内容延伸到视口外，保留全部 PSD 条目；非滚动方向不能串入旁边的控件。
                if (r.xMin < viewport.xMin - 4 || r.yMin < viewport.yMin - 4 ||
                    !horizontalScroll && r.xMax > viewport.xMax + 4 || !verticalScroll && r.yMax > viewport.yMax + 4)
                    throw new InvalidOperationException("列表项跨到非滚动方向或视口之前，需确认 content 归属。");
                Slot(item, "item");
            }
        }
        void Dropdown(PsdNode root)
        {
            var bg = Background(root); Slot(bg, "bg");
            var texts = Kids(root).Where(n => n.kind == LayerKind.Text).ToList();
            var arrows = Kids(root).Where(n => Image(n) && n != bg && Contains(bg, n)).ToList();
            if (texts.Count != 1 || arrows.Count != 1) throw new InvalidOperationException("顶条标题或箭头候选不唯一。");
            Slot(texts[0], "caption"); Slot(arrows[0], "arrow");
            var b = R(bg);
            var lists = Kids(root).Where(n => n.kind == LayerKind.Group).Where(n =>
            { var r = R(n); return r.yMin >= b.center.y && Mathf.Min(r.xMax, b.xMax) - Mathf.Max(r.xMin, b.xMin) >= b.width * 0.6f; }).ToList();
            if (lists.Count != 1) throw new InvalidOperationException("展开列表候选不唯一；当前区域流程不跨来源分组猜测。");
            var list = lists[0]; Control(list, "ScrollView"); Slot(list, "template"); Scroll(list);
            var items = Kids(list).Where(n => n.kind == LayerKind.Group).ToList();
            if (items.Count == 0) throw new InvalidOperationException("展开列表没有独立选项组。");
            foreach (var item in items) { Control(item, "Toggle"); Slot(item, "item"); Toggle(item); }
        }
    }
}
