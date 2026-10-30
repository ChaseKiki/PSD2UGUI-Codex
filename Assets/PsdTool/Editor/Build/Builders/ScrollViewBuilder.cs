using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    /// <summary>普通列表保留全部设计稿条目；下拉模板只生成一个条目，不使用 LayoutGroup 干预 TMP 的运行时排版。</summary>
    internal sealed class ScrollViewBuilder : IControlBuilder
    {
        internal sealed class Plan
        {
            public PsdNode viewport, background, content;
            public readonly List<PsdNode> items = new List<PsdNode>();
            public readonly List<PsdNode> decorations = new List<PsdNode>();
            public readonly List<PsdNode> bars = new List<PsdNode>();
            public readonly List<PsdNode> pieces = new List<PsdNode>();
        }

        internal static Plan Describe(ControlBuildContext ctx, PsdNode node)
        {
            var plan = new Plan();
            var kids = ctx.ChildrenOf(node.layerId);
            plan.viewport = kids.Find(c => c.slot == "viewport");
            plan.background = kids.Find(c => c.slot == "bg");
            plan.content = kids.Find(IsContent);
            Collect(ctx, kids, plan);
            if (plan.viewport != null && plan.viewport.kind == LayerKind.Group)
                Collect(ctx, ctx.ChildrenOf(plan.viewport.layerId), plan);
            if (plan.content != null) Collect(ctx, ctx.ChildrenOf(plan.content.layerId), plan);
            return plan;
        }

        static bool IsContent(PsdNode n) => n.slot == "content" ||
            (string.IsNullOrEmpty(n.slot) && n.kind == LayerKind.Group && string.Equals(n.goName, "Content", StringComparison.OrdinalIgnoreCase));

        static void Collect(ControlBuildContext ctx, List<PsdNode> children, Plan plan)
        {
            foreach (var child in children)
            {
                if (child == plan.viewport || child == plan.background) continue;
                if (IsContent(child)) { if (plan.content == null) plan.content = child; continue; }
                if (child.templateId == "Scrollbar" || child.slot == "scrollbar") { Add(plan.bars, child); continue; }
                if (IsBarPieceSlot(child.slot)) { Add(plan.pieces, child); continue; }
                if (IsListItem(child) && RectOf(ctx, child).HasValue) Add(plan.items, child);
                else if (child.slot != "rect" && child.slot != "hit") Add(plan.decorations, child);
            }
        }

        static void Add(List<PsdNode> list, PsdNode node) { if (!list.Contains(node)) list.Add(node); }

        internal static Rect? ScrollViewRootRect(ControlBuildContext ctx, List<PsdNode> children)
        {
            Rect? result = null;
            foreach (var c in children)
            {
                if (c.slot != "bg" && c.slot != "viewport" && !IsBarPieceSlot(c.slot) && c.templateId != "Scrollbar") continue;
                var rect = RectOf(ctx, c);
                if (rect.HasValue) result = result.HasValue ? Union(result.Value, rect.Value) : rect;
            }
            if (!result.HasValue)
                foreach (var child in children.FindAll(IsListItem))
                {
                    var rect = RectOf(ctx, child);
                    if (rect.HasValue) result = result.HasValue ? Union(result.Value, rect.Value) : rect;
                }
            return result;
        }

        public void Build(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            var plan = Describe(ctx, node);
            bool dropdown = ctx.DropdownTemplates.Contains(node.layerId);
            if (dropdown) plan.items.Sort((a, b) =>
            {
                var ar = RectOf(ctx, a) ?? new Rect(); var br = RectOf(ctx, b) ?? new Rect();
                int y = ar.y.CompareTo(br.y); return y != 0 ? y : ar.x.CompareTo(br.x);
            });
            var viewportNode = plan.viewport ?? plan.background;
            Rect viewportRect = RectOf(ctx, viewportNode) ?? ItemUnion(ctx, plan.items) ?? bounds;
            if (viewportNode == null) ctx.Tree.warnings.Add("列表“" + node.goName + "”没有明确 viewport/bg，暂用条目范围；请指定可视区域以获得正确裁剪。");

            if (plan.background != null && plan.background != viewportNode)
                CreateLooseChild(ctx, plan.background, root.transform, bounds);
            var viewport = EnsureChild(root.transform, "Viewport", viewportRect, bounds);
            viewport.AddComponent<RectMask2D>();
            if (viewportNode != null)
            {
                if (viewportNode.image.fxBaked)
                {
                    // 底板阴影不能放到裁剪容器内部，否则会被自己的 RectMask2D 截断。
                    var background = EnsureChild(root.transform, viewportNode.goName, viewportRect, bounds);
                    background.transform.SetSiblingIndex(viewport.transform.GetSiblingIndex());
                    ctx.Map[viewportNode.layerId] = background;
                    CreateImage(ctx, viewportNode, background);
                }
                else if (viewportNode.kind != LayerKind.Group)
                {
                    ctx.Map[viewportNode.layerId] = viewport;
                    CreateImage(ctx, viewportNode, viewport);
                }
                else ctx.Map[viewportNode.layerId] = viewport;
                if (viewportNode.kind == LayerKind.Group && viewportNode.opacity < 0.999f) viewport.AddComponent<CanvasGroup>().alpha = viewportNode.opacity;
            }
            var surface = viewport.GetComponent<Graphic>() ?? viewport.AddComponent<NonDrawingGraphic>();
            surface.raycastTarget = true;
            foreach (var decor in plan.decorations)
                if (plan.viewport != null && decor.parentId == plan.viewport.layerId && decor.slot == "bg")
                    CreateLooseChild(ctx, decor, viewport.transform, viewportRect);

            bool horizontal = plan.pieces.Any(p => p.slot == "hbar" || p.slot == "hbarbg");
            bool vertical = plan.pieces.Any(p => p.slot == "vbar" || p.slot == "vbarbg");
            foreach (var bar in plan.bars)
            {
                var r = RectOf(ctx, bar) ?? bounds;
                if (BarIsHorizontal(ctx, bar, r)) horizontal = true; else vertical = true;
            }
            if (!horizontal && !vertical)
            {
                var extent = ItemUnion(ctx, plan.items);
                horizontal = extent.HasValue && extent.Value.width > viewportRect.width + 1;
                vertical = extent.HasValue && extent.Value.height > viewportRect.height + 1;
                if (!horizontal && !vertical && plan.items.Count > 1)
                {
                    var a = RectOf(ctx, plan.items[0]) ?? bounds; var b = RectOf(ctx, plan.items[1]) ?? bounds;
                    horizontal = Mathf.Abs(a.center.x - b.center.x) > Mathf.Abs(a.center.y - b.center.y);
                }
                if (!horizontal && !vertical) vertical = true;
            }
            if (dropdown) { horizontal = false; vertical = true; }

            Rect contentRect = viewportRect;
            if (!dropdown)
            {
                foreach (var item in plan.items)
                {
                    var rect = RectOf(ctx, item);
                    if (rect.HasValue) contentRect = Union(contentRect, rect.Value);
                }
            }
            else if (plan.items.Count > 0)
            {
                var first = RectOf(ctx, plan.items[0]) ?? viewportRect;
                float top = Mathf.Max(0, first.yMin - viewportRect.yMin);
                contentRect.height = Mathf.Max(1, top + first.height + top);
            }
            var content = EnsureChild(viewport.transform, "Content", contentRect, viewportRect);
            var crt = (RectTransform)content.transform;
            // 非滚动方向铺满视口，避免转换为 Stretch 后留下半个默认宽高的负偏移。
            if (vertical && !horizontal)
            {
                contentRect.x = viewportRect.x; contentRect.width = viewportRect.width;
                crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
                crt.pivot = new Vector2(0.5f, 1);
                crt.sizeDelta = new Vector2(0, contentRect.height);
            }
            else if (horizontal && !vertical)
            {
                contentRect.y = viewportRect.y; contentRect.height = viewportRect.height;
                crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(0, 1);
                crt.pivot = new Vector2(0, 0.5f);
                crt.sizeDelta = new Vector2(contentRect.width, 0);
            }
            else
            {
                crt.anchorMin = crt.anchorMax = new Vector2(0, 1);
                crt.pivot = new Vector2(0, 1); crt.sizeDelta = contentRect.size;
            }
            crt.anchoredPosition = new Vector2(contentRect.x - viewportRect.x, viewportRect.y - contentRect.y);
            if (plan.content != null) ctx.Map[plan.content.layerId] = content;
            int count = dropdown ? Mathf.Min(1, plan.items.Count) : plan.items.Count;
            for (int i = 0; i < count; i++)
            {
                var item = plan.items[i];
                var rect = RectOf(ctx, item) ?? new Rect(contentRect.position, new Vector2(100, 30));
                var itemGo = EnsureChild(content.transform, item.goName, rect, contentRect);
                ctx.Map[item.layerId] = itemGo;
                if (dropdown && item.templateId != "Toggle" && item.templateId != "Tab")
                    ToggleBuilder.BuildToggle(ctx, item, itemGo, rect, false);
                else BuildNodeContent(ctx, item, itemGo, rect);
                if (!item.visible && !dropdown) itemGo.SetActive(false);
                if (dropdown) itemGo.SetActive(true);
            }
            foreach (var decor in plan.decorations)
            {
                // 视口内的装饰保留裁剪；列表根部的标题保留在根部。
                bool inside = plan.viewport != null && decor.parentId == plan.viewport.layerId;
                CreateLooseChild(ctx, decor, inside ? viewport.transform : root.transform, inside ? viewportRect : bounds);
            }
            var scroll = root.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = crt;
            scroll.horizontal = horizontal; scroll.vertical = vertical;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            foreach (var bar in plan.bars)
            {
                var br = RectOf(ctx, bar) ?? bounds;
                bool axis = BarIsHorizontal(ctx, bar, br);
                var track = Slot(ctx, bar, "track") ?? Slot(ctx, bar, "bg");
                var handle = Slot(ctx, bar, "handle");
                var parts = ctx.ChildrenOf(bar.layerId).Where(c => c.kind != LayerKind.Text && RectOf(ctx, c).HasValue).ToList();
                bool hasTrack = track != null || (handle != null ? parts.Any(c => c != handle) : parts.Count > 1);
                if (!hasTrack) br = ScrollbarBuilder.TrackFromViewport(br, viewportRect, axis);
                var barGo = EnsureChild(root.transform, bar.goName, br, bounds);
                ctx.Map[bar.layerId] = barGo;
                Bind(scroll, ScrollbarBuilder.BuildScrollbar(ctx, bar, barGo, br), axis);
            }
            foreach (bool x in new[] { false, true })
            {
                string track = x ? "hbarbg" : "vbarbg", handle = x ? "hbar" : "vbar";
                var t = plan.pieces.Find(p => p.slot == track); var h = plan.pieces.Find(p => p.slot == handle);
                if (t != null || h != null)
                {
                    if ((x && scroll.horizontalScrollbar != null) || (!x && scroll.verticalScrollbar != null))
                        throw new InvalidOperationException("列表“" + node.goName + "”同一方向同时指定了滚动条组和轨道/滑块分片，请保留一种形式。");
                    Bind(scroll, ScrollbarBuilder.BuildScrollbarPieces(ctx, root, bounds, t, h, out _, viewportRect), x);
                }
            }
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            // Serialize the same bounds/size that uGUI will use on its first layout.
            // Empty or short content fills the track; longer content uses view/content.
            scroll.Rebuild(CanvasUpdate.PostLayout);
            if (plan.items.Count == 0) ctx.Tree.warnings.Add("列表“" + node.goName + "”没有 item，已生成可绑定的空 Content。");
        }

        static void Bind(ScrollRect scroll, Scrollbar bar, bool horizontal)
        {
            // Axis belongs to the ScrollRect slot. Do not rotate/resize the PSD geometry.
            bool barHorizontal = bar.direction == Scrollbar.Direction.LeftToRight || bar.direction == Scrollbar.Direction.RightToLeft;
            if (barHorizontal != horizontal) bar.SetDirection(horizontal ? Scrollbar.Direction.LeftToRight : Scrollbar.Direction.BottomToTop, false);
            if (horizontal) scroll.horizontalScrollbar = bar; else scroll.verticalScrollbar = bar;
        }

        static bool BarIsHorizontal(ControlBuildContext ctx, PsdNode bar, Rect bounds)
        {
            if (bar.slot == "hbar" || bar.slot == "hbarbg") return true;
            if (bar.slot == "vbar" || bar.slot == "vbarbg") return false;
            var track = Slot(ctx, bar, "track") ?? Slot(ctx, bar, "bg");
            var rect = RectOf(ctx, track) ?? bounds;
            return rect.width >= rect.height;
        }

        internal static bool IsBarPieceSlot(string slot) => slot == "scrollbar" || slot == "vbarbg" || slot == "vbar" || slot == "hbarbg" || slot == "hbar";
        internal static bool IsListItem(PsdNode n)
        {
            if (n == null || IsBarPieceSlot(n.slot) || n.slot == "bg" || n.slot == "viewport" || n.slot == "template" || IsContent(n)) return false;
            if (n.templateId == "Scrollbar" || n.templateId == "ScrollView") return false;
            if (n.slot == "item") return true;
            if (!string.IsNullOrEmpty(n.slot)) return false;
            string name = (n.psdName ?? "") + (n.goName ?? "");
            if (name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("标题")) return false;
            return n.kind == LayerKind.Group || n.templateId == "Toggle" || n.templateId == "Tab";
        }
        static Rect? ItemUnion(ControlBuildContext ctx, List<PsdNode> items)
        {
            Rect? result = null;
            foreach (var item in items)
            {
                var rect = RectOf(ctx, item);
                if (rect.HasValue) result = result.HasValue ? Union(result.Value, rect.Value) : rect;
            }
            return result;
        }
    }
}
