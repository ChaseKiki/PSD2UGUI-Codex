using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class ScrollbarBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            BuildScrollbar(ctx, node, go, bounds);
        }

        internal static Scrollbar BuildScrollbarPieces(ControlBuildContext ctx, GameObject parent, Rect rootRect, PsdNode track, PsdNode handle, out bool horizontal, Rect? viewportRect = null)
        {
            Rect? trackRect = track != null ? RectOf(ctx, track) : null;
            Rect? handleRect = handle != null ? RectOf(ctx, handle) : null;
            string slot = (track ?? handle).slot ?? string.Empty;
            Rect axisRect = trackRect ?? handleRect ?? rootRect;
            horizontal = slot == "hbar" || slot == "hbarbg" || (slot != "vbar" && slot != "vbarbg" && axisRect.width > axisRect.height);
            // The thumb's PSD sample must not shorten or enlarge its travel range.
            Rect barRect = trackRect ?? (viewportRect.HasValue ? TrackFromViewport(handleRect, viewportRect.Value, horizontal) : handleRect ?? rootRect);

            var barGo = new GameObject("Scrollbar", typeof(RectTransform));
            barGo.transform.SetParent(parent.transform, false);
            AnchorUtil.Apply((RectTransform)barGo.transform, barRect, rootRect, "c", 0f);

            var bgGo = EnsureChild(barGo.transform, "Background", trackRect ?? barRect, barRect);
            if (track != null) CopyImageTo(ctx, track, bgGo);
            else bgGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

            var sliding = EnsureChild(barGo.transform, "Sliding Area", barRect, barRect, "stretch");
            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(sliding.transform, false);
            var hrt = (RectTransform)handleGo.transform;
            var hr = handleRect ?? barRect;
            if (horizontal)
            {
                hrt.anchorMin = new Vector2(0f, 0f);
                hrt.anchorMax = new Vector2(0f, 1f);
                hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = Vector2.zero;
            }
            else
            {
                hrt.anchorMin = new Vector2(0f, 1f);
                hrt.anchorMax = new Vector2(1f, 1f);
                hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = Vector2.zero;
            }
            hrt.anchoredPosition = Vector2.zero;
            if (handle != null) CopyImageTo(ctx, handle, handleGo);
            else handleGo.AddComponent<Image>().color = Color.white;
            if (handle != null && handle.kind == LayerKind.Group) FitHandleChildren(ctx, handle, handleGo.transform, hr);
            var graphic = handleGo.GetComponent<Graphic>() ?? handleGo.GetComponentInChildren<Graphic>(true);
            if (graphic != null) graphic.raycastTarget = true;

            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = hrt;
            scrollbar.targetGraphic = graphic;
            scrollbar.direction = horizontal ? Scrollbar.Direction.LeftToRight : Scrollbar.Direction.BottomToTop;
            float along = horizontal ? barRect.width : barRect.height;
            float handleLen = horizontal ? hr.width : hr.height;
            scrollbar.size = Mathf.Clamp(along > 1f ? handleLen / along : 0.2f, 0.05f, 1f);
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbar.value = ScrollbarValue(barRect, hr, horizontal);
            return scrollbar;
        }

        internal static Scrollbar BuildScrollbar(ControlBuildContext ctx, PsdNode node, GameObject go, Rect rootRect)
        {
            var track = Slot(ctx, node, "track") ?? Slot(ctx, node, "bg");
            var handle = Slot(ctx, node, "handle");
            if (track == null && handle == null && node.kind != LayerKind.Group)
                handle = node;
            else if (handle == null)
            {
                PsdNode smallest = null;
                long area = long.MaxValue;
                foreach (var c in ctx.ChildrenOf(node.layerId))
                {
                    if (c == track || c.kind == LayerKind.Text) continue;
                    var r = RectOf(ctx, c);
                    if (r == null) continue;
                    long a = (long)r.Value.width * (long)r.Value.height;
                    if (a < area) { area = a; smallest = c; }
                }
                handle = smallest;
            }

            if (track == null)
            {
                PsdNode largest = null;
                long area = -1;
                foreach (var c in ctx.ChildrenOf(node.layerId))
                {
                    if (c == handle || c.kind == LayerKind.Text) continue;
                    var r = RectOf(ctx, c);
                    if (r == null) continue;
                    long a = (long)r.Value.width * (long)r.Value.height;
                    if (a > area) { area = a; largest = c; }
                }
                track = largest;
            }

            int skipTrack = track != null && track != node ? track.layerId : -1;
            int skipHandle = handle != null && handle != node ? handle.layerId : -1;
            CreateDecorChildren(ctx, node, go, rootRect, skipTrack, skipHandle, "track", "bg", "handle");

            var trackRect = (track != null ? RectOf(ctx, track) : null) ?? rootRect;
            bool horizontal = node.slot == "hbar" || node.slot == "hbarbg" ||
                (node.slot != "vbar" && node.slot != "vbarbg" && trackRect.width >= trackRect.height);
            var bgGo = EnsureChild(go.transform, "Background", trackRect, rootRect);
            if (track != null) CopyImageTo(ctx, track, bgGo);
            else
            {
                var bg = bgGo.AddComponent<Image>();
                bg.color = new Color(1f, 1f, 1f, 0.15f);
                bg.raycastTarget = true;
            }

            var handleRect = (handle != null ? RectOf(ctx, handle) : null) ?? trackRect;
            var sliding = EnsureChild(go.transform, "Sliding Area", trackRect, rootRect, "stretch");
            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(sliding.transform, false);
            var hrt = (RectTransform)handleGo.transform;
            if (horizontal)
            {
                hrt.anchorMin = new Vector2(0f, 0f);
                hrt.anchorMax = new Vector2(0f, 1f);
                hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = Vector2.zero;
            }
            else
            {
                hrt.anchorMin = new Vector2(0f, 1f);
                hrt.anchorMax = new Vector2(1f, 1f);
                hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = Vector2.zero;
            }
            hrt.anchoredPosition = Vector2.zero;
            if (handle != null && handle != node && handle.kind == LayerKind.Group)
            {
                ctx.Map[handle.layerId] = handleGo;
                CreateVisualChildren(ctx, handle, handleGo, handleRect);
                FitHandleChildren(ctx, handle, handleGo.transform, handleRect);
            }
            else if (handle != null && handle != node) CopyImageTo(ctx, handle, handleGo);
            else if (node.kind != LayerKind.Group) CreateImage(ctx, node, handleGo);
            else handleGo.AddComponent<Image>().color = Color.white;
            var handleGraphic = handleGo.GetComponent<Graphic>() ?? handleGo.GetComponentInChildren<Graphic>(true);
            if (handleGraphic != null) handleGraphic.raycastTarget = true;

            var scrollbar = go.AddComponent<Scrollbar>();
            scrollbar.handleRect = hrt;
            scrollbar.targetGraphic = handleGraphic;
            scrollbar.direction = horizontal ? Scrollbar.Direction.LeftToRight : Scrollbar.Direction.BottomToTop;
            float along = horizontal ? trackRect.width : trackRect.height;
            float handleLen = horizontal ? handleRect.width : handleRect.height;
            scrollbar.size = Mathf.Clamp(along > 1f ? handleLen / along : 0.2f, 0.05f, 1f);
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            scrollbar.value = ScrollbarValue(trackRect, handleRect, horizontal);
            return scrollbar;
        }

        internal static Rect TrackFromViewport(Rect? handle, Rect viewport, bool horizontal)
        {
            var thumb = handle ?? viewport;
            return horizontal ? new Rect(viewport.x, thumb.y, viewport.width, thumb.height) :
                new Rect(thumb.x, viewport.y, thumb.width, viewport.height);
        }

        static void FitHandleChildren(ControlBuildContext ctx, PsdNode node, Transform parent, Rect bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0) return;
            foreach (var child in ctx.ChildrenOf(node.layerId))
            {
                if (!ctx.Map.TryGetValue(child.layerId, out var go) || go.transform.parent != parent) continue;
                var rect = RectOf(ctx, child); if (!rect.HasValue) continue;
                var r = rect.Value; var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2((r.xMin-bounds.xMin)/bounds.width, 1-(r.yMax-bounds.yMin)/bounds.height);
                rt.anchorMax = new Vector2((r.xMax-bounds.xMin)/bounds.width, 1-(r.yMin-bounds.yMin)/bounds.height);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                if (child.kind == LayerKind.Group) FitHandleChildren(ctx, child, go.transform, r);
            }
        }

        internal static float ScrollbarValue(Rect track, Rect handle, bool horizontal)
        {
            if (horizontal)
            {
                float travel = Mathf.Max(1f, track.width - handle.width);
                return Mathf.Clamp01((handle.center.x - track.xMin - handle.width * 0.5f) / travel);
            }

            float travelV = Mathf.Max(1f, track.height - handle.height);
            float fromTop = handle.yMin - track.yMin;
            return Mathf.Clamp01(1f - fromTop / travelV);
        }
    }
}
