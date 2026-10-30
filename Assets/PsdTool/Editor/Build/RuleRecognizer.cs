using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PsdTool
{
    /// <summary>
    /// 无标记时的规则识别。高置信度直接写入（source=Rule），低置信度进建议列表。
    /// 不覆盖 Manual / Marker。
    /// </summary>
    public static class RuleRecognizer
    {
        public static void Recognize(PsdTree tree, PsdToolSettings settings)
        {
            var children = BuildChildren(tree);
            foreach (var node in tree.nodes)
            {
                if (node.status == NodeStatus.PendingDelete) continue;
                if (node.source == DecisionSource.Manual || node.source == DecisionSource.Marker || node.source == DecisionSource.AI)
                    continue;
                if (!string.IsNullOrEmpty(node.templateId) && node.templateId != "Group" && node.source != DecisionSource.None)
                    continue;

                if (!TryRecognize(node, children, out string template, out float confidence)) continue;

                if (confidence >= settings.ruleAutoApplyMin)
                {
                    node.templateId = template;
                    node.source = DecisionSource.Rule;
                    node.confidence = confidence;
                }
                else
                {
                    tree.suggestions.Add(new ResourceSuggestion
                    {
                        kind = SuggestionKind.ApplyRule,
                        layerId = node.layerId,
                        templateId = template,
                        message = $"建议将“{node.goName}”识别为 {template}（置信度 {confidence:0.00}）",
                    });
                }
            }

            DetectToggleGroups(tree, children);
            PromoteImageInputs(tree);
            FillSlots(tree);
        }

        /// <summary>给已有模板补空插槽。不覆盖已有插槽，也不覆盖手动修改。</summary>
        public static void FillSlots(PsdTree tree)
        {
            var children = BuildChildren(tree);
            foreach (var node in tree.nodes)
            {
                if (node.status == NodeStatus.PendingDelete) continue;
                if (string.IsNullOrEmpty(node.templateId) || node.templateId == "Group") continue;
                AssignDefaultSlots(node, children);
            }
            AssignSlotsByGeometry(tree, children);
        }

        public static void Accept(ResourceSuggestion s, PsdTree tree)
        {
            if (s.kind != SuggestionKind.ApplyRule || s.done) return;
            var node = tree.Find(s.layerId);
            if (node == null) return;
            node.templateId = s.templateId;
            node.source = DecisionSource.Manual;
            node.confidence = 1f;
            s.done = true;
            AssignDefaultSlots(node, BuildChildren(tree));
        }

        static Dictionary<int, List<PsdNode>> BuildChildren(PsdTree tree)
        {
            var map = new Dictionary<int, List<PsdNode>>();
            foreach (var n in tree.nodes)
            {
                if (n.status == NodeStatus.PendingDelete) continue;
                if (!map.TryGetValue(n.parentId, out var list)) map[n.parentId] = list = new List<PsdNode>();
                list.Add(n);
            }
            foreach (var list in map.Values) list.Sort((a, b) => a.order.CompareTo(b.order));
            return map;
        }

        static List<PsdNode> Kids(Dictionary<int, List<PsdNode>> map, int id) =>
            map.TryGetValue(id, out var list) ? list : new List<PsdNode>();

        static bool TryRecognize(PsdNode node, Dictionary<int, List<PsdNode>> children, out string template, out float confidence)
        {
            template = null;
            confidence = 0;
            string name = (node.psdName ?? string.Empty) + " " + (node.goName ?? string.Empty);

            if (Match(name, @"\b(btn|button|按钮)\b")) return Hit("Button", 0.95f, out template, out confidence);
            if (Match(name, @"\b(toggle|开关)\b")) return Hit("Toggle", 0.95f, out template, out confidence);
            if (Match(name, @"\b(tab|页签)\b")) return Hit("Tab", 0.95f, out template, out confidence);
            if (node.kind != LayerKind.Text && Match(name, @"scrollbar|滚动条"))
            {
                bool list = LooksLikeList(Kids(children, node.layerId));
                return Hit(list ? "ScrollView" : "Scrollbar", list ? 0.9f : 0.95f, out template, out confidence);
            }
            if (Match(name, @"\b(slider|滑动条)\b")) return Hit("Slider", 0.95f, out template, out confidence);
            if (Match(name, @"\b(progress|进度)\b")) return Hit("Progress", 0.9f, out template, out confidence);
            if (IsChromeSlot(node.slot)) return false;
            if (node.kind == LayerKind.Group && Match(name, @"\b(list|scroll|列表)\b") && !Match(name, @"title|标题"))
                return Hit("ScrollView", 0.9f, out template, out confidence);
            if (node.kind == LayerKind.Group && Match(name, @"inputfield|输入框|\binput\b|输入"))
                return Hit("InputField", 0.95f, out template, out confidence);
            if (Match(name, @"\b(dropdown|下拉)\b")) return Hit("Dropdown", 0.85f, out template, out confidence);
            if (Match(name, @"\b(mask|遮罩)\b")) return Hit("Mask", 0.9f, out template, out confidence);

            if (node.kind != LayerKind.Group) return false;
            var kids = Kids(children, node.layerId);
            if (kids.Count == 0) return false;

            if (LooksLikeButton(kids, node)) return Hit("Button", 0.75f, out template, out confidence);
            if (LooksLikeToggle(kids)) return Hit("Toggle", 0.7f, out template, out confidence);
            if (LooksLikeSlider(kids, out bool hasHandle))
                return Hit(hasHandle ? "Slider" : "Progress", 0.7f, out template, out confidence);
            if (LooksLikeList(kids)) return Hit("ScrollView", 0.7f, out template, out confidence);
            if (LooksLikeInput(kids)) return Hit("InputField", 0.65f, out template, out confidence);

            return false;
        }

        static bool Hit(string t, float c, out string template, out float confidence)
        {
            template = t;
            confidence = c;
            return true;
        }

        static bool Match(string name, string pattern) =>
            Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase);

        static bool LooksLikeButton(List<PsdNode> kids, PsdNode node)
        {
            var bg = LargestImage(kids);
            if (bg == null) return false;
            bool hasLabelOrIcon = kids.Exists(k => k.kind == LayerKind.Text || (IsImage(k) && k != bg));
            if (!hasLabelOrIcon) return false;
            var r = bg.image.trimmedRect;
            return r.width > 0 && r.width <= 600 && r.height > 0 && r.height <= 200;
        }

        static bool LooksLikeToggle(List<PsdNode> kids)
        {
            var bg = LargestImage(kids);
            if (bg == null) return false;
            return kids.Exists(k => k != bg && (IsCheckName(k) || (IsImage(k) && !k.visible)));
        }

        static bool LooksLikeSlider(List<PsdNode> kids, out bool hasHandle)
        {
            hasHandle = false;
            var images = kids.FindAll(IsImage);
            if (images.Count < 2) return false;
            var track = LargestImage(images);
            if (track == null) return false;
            var tr = track.image.trimmedRect;
            float ratio = tr.height > 0 ? (float)tr.width / tr.height : 0;
            if (ratio < 6f && (tr.width > 0 ? (float)tr.height / tr.width : 0) < 6f) return false;
            hasHandle = images.Exists(i => i != track && i.image.trimmedRect.width <= tr.height * 2);
            return true;
        }

        static bool LooksLikeList(List<PsdNode> kids)
        {
            var groups = kids.FindAll(k => k.kind == LayerKind.Group);
            if (groups.Count < 2) return false;
            var a = groups[0].psdRect;
            for (int i = 1; i < groups.Count; i++)
            {
                var b = groups[i].psdRect;
                if (Math.Abs(a.width - b.width) > 4 || Math.Abs(a.height - b.height) > 4) return false;
            }
            return true;
        }

        static bool LooksLikeInput(List<PsdNode> kids)
        {
            var text = kids.Find(k => k.kind == LayerKind.Text);
            return LargestImage(kids) != null && text != null
                   && (text.text.content?.Contains("请输入") == true || text.opacity < 0.5f);
        }

        static void DetectToggleGroups(PsdTree tree, Dictionary<int, List<PsdNode>> children)
        {
            foreach (var kv in children)
            {
                var toggles = kv.Value.FindAll(n =>
                    n.templateId == "Toggle" || n.templateId == "Tab");
                if (toggles.Count < 2) continue;
                if (!Collinear(toggles)) continue;

                var parent = tree.Find(kv.Key);
                if (parent == null) continue;
                if (Fixed(parent)) continue;
                if (!string.IsNullOrEmpty(parent.templateId) && parent.templateId != "Group") continue;
                parent.templateId = "ToggleGroup";
                parent.source = DecisionSource.Rule;
                parent.confidence = 0.85f;
            }
        }

        static bool Collinear(List<PsdNode> nodes)
        {
            if (nodes.Count < 2) return false;
            float cy = Center(nodes[0]).y;
            float cx = Center(nodes[0]).x;
            bool row = true, col = true;
            for (int i = 1; i < nodes.Count; i++)
            {
                var c = Center(nodes[i]);
                if (Mathf.Abs(c.y - cy) > 4f) row = false;
                if (Mathf.Abs(c.x - cx) > 4f) col = false;
            }
            return row || col;
        }

        static Vector2 Center(PsdNode n) =>
            new Vector2(n.psdRect.x + n.psdRect.width * 0.5f, n.psdRect.y + n.psdRect.height * 0.5f);

        static void AssignDefaultSlots(PsdNode node, Dictionary<int, List<PsdNode>> children)
        {
            var kids = Kids(children, node.layerId);
            switch (node.templateId)
            {
                case "Button":
                    SetSlot(LargestImage(kids), "bg");
                    SetSlot(kids.Find(k => k.kind == LayerKind.Text), "label");
                    break;
                case "Toggle":
                case "Tab":
                    SetSlot(LargestImage(kids), "bg");
                    SetSlot(FindCheck(kids), "checkmark");
                    SetSlot(kids.Find(k => IsImage(k) && k.slot != "bg" && k.slot != "checkmark" && !k.visible), "on");
                    SetSlot(kids.Find(k => k.kind == LayerKind.Text && k.slot != "on"), "label");
                    break;
                case "Scrollbar":
                    SetSlot(LargestImage(kids), "track");
                    SetSlot(kids.Find(k => IsImage(k) && k.slot != "track"), "handle");
                    break;
                case "Slider":
                case "Progress":
                    SetSlot(LargestImage(kids), "track");
                    var fill = kids.Find(k => IsImage(k) && k.slot != "track");
                    SetSlot(fill, "fill");
                    if (node.templateId == "Slider")
                        SetSlot(kids.Find(k => IsImage(k) && k.slot != "track" && k.slot != "fill"), "handle");
                    break;
                case "Dropdown":
                    AssignDropdownSlots(kids);
                    break;
                case "InputField":
                    SetSlot(LargestImage(kids), "bg");
                    var texts = kids.FindAll(k => k.kind == LayerKind.Text);
                    if (texts.Count >= 2)
                    {
                        SetSlot(texts.Find(t => t.opacity < 0.6f) ?? texts[0], "placeholder");
                        SetSlot(texts.Find(t => t.slot != "placeholder"), "text");
                    }
                    else if (texts.Count == 1 && !Fixed(texts[0])
                             && (string.IsNullOrEmpty(texts[0].slot) || texts[0].slot == "label"))
                        texts[0].slot = "placeholder";
                    else if (texts.Count == 1) SetSlot(texts[0], "placeholder");
                    break;
                case "ScrollView":
                    AssignScrollSlots(kids);
                    break;
            }
        }

        static void AssignSlotsByGeometry(PsdTree tree, Dictionary<int, List<PsdNode>> children)
        {
            foreach (var node in tree.nodes)
            {
                if (node.templateId != "Button") continue;
                var kids = Kids(children, node.layerId);
                var bg = kids.Find(k => k.slot == "bg") ?? LargestImage(kids);
                if (bg == null) continue;
                var br = bg.image.trimmedRect.width > 0 ? bg.image.trimmedRect : bg.psdRect;
                foreach (var k in kids)
                {
                    if (!string.IsNullOrEmpty(k.slot) || Fixed(k)) continue;
                    if (!IsImage(k) || k == bg) continue;
                    var r = k.image.trimmedRect.width > 0 ? k.image.trimmedRect : k.psdRect;
                    float area = r.width * r.height;
                    float bgArea = Mathf.Max(1, br.width * br.height);
                    if (area / bgArea < 0.15f && OverlapsTopRight(r, br))
                    {
                        k.slot = "badge";
                        if (k.source == DecisionSource.None) k.source = DecisionSource.Rule;
                    }
                    else if (area / bgArea < 0.5f && Inside(r, br, 0.8f))
                    {
                        k.slot = "icon";
                        if (k.source == DecisionSource.None) k.source = DecisionSource.Rule;
                    }
                }
            }
        }

        static void PromoteImageInputs(PsdTree tree)
        {
            foreach (var node in tree.nodes)
            {
                if (node.templateId != "InputField" || node.kind == LayerKind.Group) continue;
                if (Fixed(node)) continue;
                var parent = tree.Find(node.parentId);
                if (parent == null || parent.kind != LayerKind.Group) continue;
                if (Fixed(parent)) continue;
                if (!string.IsNullOrEmpty(parent.templateId) && parent.templateId != "Group" && parent.templateId != "InputField")
                    continue;
                parent.templateId = "InputField";
                parent.source = DecisionSource.Rule;
                parent.confidence = node.confidence > 0f ? node.confidence : 0.9f;
                node.templateId = string.Empty;
                if (string.IsNullOrEmpty(node.slot)) node.slot = "bg";
            }
        }

        static void AssignDropdownSlots(List<PsdNode> kids)
        {
            SetSlot(LargestImage(kids), "bg");
            var caption = kids.Find(k => k.kind == LayerKind.Text && k.slot != "placeholder");
            if (caption != null && !Fixed(caption) && (string.IsNullOrEmpty(caption.slot) || caption.slot == "label"))
                caption.slot = "caption";
            var arrow = kids.Find(k => k.slot == "arrow") ?? kids.Find(k => Match(NameOf(k), @"arrow|箭头|dpdicon"));
            SetSlot(arrow, "arrow");
            var list = kids.Find(k => k.templateId == "ScrollView" || k.slot == "template");
            if (list != null && !Fixed(list) && list.slot != "template")
                list.slot = "template";
        }

        static bool IsChromeSlot(string slot)
        {
            switch (slot)
            {
                case "bg":
                case "label":
                case "icon":
                case "arrow":
                case "checkmark":
                case "vbarbg":
                case "vbar":
                case "hbarbg":
                case "hbar":
                case "track":
                case "handle":
                case "placeholder":
                case "caption":
                case "viewport":
                case "rect":
                case "hit":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsBarPiece(PsdNode n) =>
            n != null && (n.slot == "vbarbg" || n.slot == "vbar" || n.slot == "hbarbg" || n.slot == "hbar"
                          || n.slot == "scrollbar" || n.templateId == "Scrollbar");

        static void AssignScrollSlots(List<PsdNode> kids)
        {
            var bar = kids.Find(k => k.slot == "scrollbar" || k.templateId == "Scrollbar");
            if (bar == null) bar = kids.Find(IsBarName);
            if (bar == null && kids.Find(k => k.slot == "vbarbg" || k.slot == "hbarbg") == null)
                bar = kids.Find(k => LooksLikeBar(k, kids));
            if (bar != null && string.IsNullOrEmpty(bar.slot)) SetSlot(bar, "scrollbar");

            var items = new List<PsdNode>();
            foreach (var g in kids)
            {
                if (g == bar || IsBarPiece(g) || IsChromeSlot(g.slot)) continue;
                if (g.templateId == "Scrollbar" || g.templateId == "ScrollView") continue;
                if (g.kind != LayerKind.Group && g.templateId != "Toggle" && g.templateId != "Tab") continue;
                items.Add(g);
                SetSlot(g, "item");
            }

            var vp = kids.Find(k => k.slot == "viewport");
            if (vp == null) vp = kids.Find(k => k != bar && IsViewportName(k));
            if (vp == null) vp = FrameAroundItems(kids, items, bar);
            if (vp != null && vp != bar) SetSlot(vp, "viewport");
        }

        static PsdNode FindCheck(List<PsdNode> kids)
        {
            var named = kids.Find(k => k.slot != "bg" && IsCheckName(k) && (IsImage(k) || k.kind == LayerKind.Group));
            if (named != null) return named;

            var bg = LargestImage(kids);
            PsdNode best = null;
            float bestRatio = 1f;
            RectInt bgRect = default;
            if (bg != null) bgRect = BoundsOf(bg);
            float bgArea = bg != null ? Mathf.Max(1f, bgRect.width * bgRect.height) : 1f;
            foreach (var k in kids)
            {
                if (k == bg || !IsImage(k) || !string.IsNullOrEmpty(k.slot)) continue;
                var r = BoundsOf(k);
                float ratio = r.width * r.height / bgArea;
                if (ratio <= 0f || ratio > 0.45f) continue;
                if (bg != null && !Inside(r, bgRect, 0.7f)) continue;
                if (best == null || ratio < bestRatio || (Mathf.Approximately(ratio, bestRatio) && r.x < BoundsOf(best).x))
                {
                    best = k;
                    bestRatio = ratio;
                }
            }
            return best;
        }

        static bool IsCheckName(PsdNode n) =>
            Match(NameOf(n), @"checkmark|check|tick|对勾|勾选|勾|\bmark\b");

        static bool IsBarName(PsdNode n) => Match(NameOf(n), @"scrollbar|滚动条");

        static bool IsViewportName(PsdNode n) => Match(NameOf(n), @"viewport|视口|可视|遮罩|mask|裁剪");

        static string NameOf(PsdNode n) => (n.psdName ?? string.Empty) + " " + (n.goName ?? string.Empty);

        static bool LooksLikeBar(PsdNode n, List<PsdNode> kids)
        {
            if (n.kind == LayerKind.Text) return false;
            var r = BoundsOf(n);
            if (r.width < 4 || r.height < 4) return false;
            float aspect = Mathf.Max(r.width / (float)r.height, r.height / (float)r.width);
            if (aspect < 4f) return false;

            bool any = false;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var k in kids)
            {
                var b = BoundsOf(k);
                if (b.width <= 0 || b.height <= 0) continue;
                any = true;
                x0 = Math.Min(x0, b.x);
                y0 = Math.Min(y0, b.y);
                x1 = Math.Max(x1, b.xMax);
                y1 = Math.Max(y1, b.yMax);
            }
            if (!any) return false;
            var bounds = new RectInt(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));

            bool vertical = r.height > r.width && r.width <= bounds.width * 0.25f
                            && (r.xMax >= bounds.xMax - 6 || r.x <= bounds.x + 6);
            bool horizontal = r.width > r.height && r.height <= bounds.height * 0.25f
                              && (r.yMax >= bounds.yMax - 6 || r.y <= bounds.y + 6);
            return vertical || horizontal;
        }

        static PsdNode FrameAroundItems(List<PsdNode> kids, List<PsdNode> items, PsdNode bar)
        {
            if (items.Count == 0) return null;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var item in items)
            {
                var b = BoundsOf(item);
                x0 = Math.Min(x0, b.x);
                y0 = Math.Min(y0, b.y);
                x1 = Math.Max(x1, b.xMax);
                y1 = Math.Max(y1, b.yMax);
            }
            var union = new RectInt(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
            long unionArea = (long)union.width * union.height;

            PsdNode best = null;
            long bestArea = long.MaxValue;
            foreach (var k in kids)
            {
                if (k == bar || !IsImage(k)) continue;
                var r = BoundsOf(k);
                if (!ContainsCenter(r, union)) continue;
                long area = (long)r.width * r.height;
                if (area < unionArea * 0.5f || area > unionArea * 2.5f) continue;
                if (area < bestArea)
                {
                    bestArea = area;
                    best = k;
                }
            }
            return best;
        }

        static bool ContainsCenter(RectInt outer, RectInt inner)
        {
            float cx = inner.x + inner.width * 0.5f;
            float cy = inner.y + inner.height * 0.5f;
            return cx >= outer.x && cx <= outer.xMax && cy >= outer.y && cy <= outer.yMax;
        }

        static RectInt BoundsOf(PsdNode n)
        {
            if (n.image != null && n.image.trimmedRect.width > 0 && n.image.trimmedRect.height > 0)
                return n.image.trimmedRect;
            return n.psdRect;
        }

        static bool Fixed(PsdNode n) => n.source == DecisionSource.Manual || n.source == DecisionSource.Marker || n.source == DecisionSource.AI;

        static void SetSlot(PsdNode n, string slot)
        {
            if (n == null || Fixed(n)) return;
            if (!string.IsNullOrEmpty(n.slot)) return;
            n.slot = slot;
            if (n.source == DecisionSource.None) n.source = DecisionSource.Rule;
        }

        static bool IsImage(PsdNode n) =>
            n != null && (n.kind == LayerKind.Image || n.kind == LayerKind.Fill || n.kind == LayerKind.SmartObject)
            && !n.image.isEmpty;

        static PsdNode LargestImage(List<PsdNode> kids)
        {
            PsdNode best = null;
            long area = -1;
            foreach (var k in kids)
            {
                if (!IsImage(k)) continue;
                var r = k.image.trimmedRect.width > 0 ? k.image.trimmedRect : k.psdRect;
                long a = (long)r.width * r.height;
                if (a > area) { area = a; best = k; }
            }
            return best;
        }

        static bool Inside(RectInt a, RectInt b, float ratio)
        {
            float inter = IntersectArea(a, b);
            float area = Mathf.Max(1, a.width * a.height);
            return inter / area >= ratio;
        }

        static bool OverlapsTopRight(RectInt a, RectInt b)
        {
            var corner = new RectInt(b.xMax - b.width / 4, b.y, b.width / 4, b.height / 4);
            return IntersectArea(a, corner) > 0;
        }

        static float IntersectArea(RectInt a, RectInt b)
        {
            int x0 = Math.Max(a.x, b.x);
            int y0 = Math.Max(a.y, b.y);
            int x1 = Math.Min(a.xMax, b.xMax);
            int y1 = Math.Min(a.yMax, b.yMax);
            if (x1 <= x0 || y1 <= y0) return 0;
            return (x1 - x0) * (y1 - y0);
        }
    }
}
