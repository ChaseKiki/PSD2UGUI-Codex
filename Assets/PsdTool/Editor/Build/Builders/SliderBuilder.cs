using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class SliderBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            bool interactive = node.templateId != "Progress";
            if (!interactive && node.progressFill != ProgressFillMode.Auto) { BuildExplicitProgress(ctx, node, root, bounds); return; }
            CreateDecorChildren(ctx, node, root, bounds, -1, -1, "track", "fill", "handle");
            var track = Slot(ctx, node, "track") ?? Slot(ctx, node, "bg");
            var fill = Slot(ctx, node, "fill");
            var handle = Slot(ctx, node, "handle");
            Rect tr = RectOf(ctx, track) ?? bounds;
            Rect fr = RectOf(ctx, fill) ?? tr;
            bool horizontal = tr.width >= tr.height;
            var bg = EnsureChild(root.transform, "Background", tr, bounds);
            CopyImageTo(ctx, track, bg);
            bool reverse = horizontal ? tr.xMax - fr.xMax < fr.xMin - tr.xMin : fr.yMin - tr.yMin < tr.yMax - fr.yMax;
            float edge = horizontal ? Mathf.Max(0, Mathf.Min(fr.xMin - tr.xMin, tr.xMax - fr.xMax)) :
                Mathf.Max(0, Mathf.Min(fr.yMin - tr.yMin, tr.yMax - fr.yMax));
            var areaRect = horizontal ? new Rect(tr.x + edge, fr.y, Mathf.Max(1, tr.width - edge * 2), fr.height) :
                new Rect(fr.x, tr.y + edge, fr.width, Mathf.Max(1, tr.height - edge * 2));
            var fillArea = EnsureChild(root.transform, "Fill Area", areaRect, bounds);
            var fillGo = EnsureChild(fillArea.transform, "Fill", areaRect, areaRect, "stretch");
            CopyImageTo(ctx, fill, fillGo);
            var image = fillGo.GetComponent<Image>();
            float value = horizontal ? (reverse ? areaRect.xMax - fr.xMin : fr.xMax - areaRect.xMin) / Mathf.Max(1, areaRect.width) :
                (reverse ? fr.yMax - areaRect.yMin : areaRect.yMax - fr.yMin) / Mathf.Max(1, areaRect.height);
            RectTransform handleRt = null;
            Graphic handleGraphic = null;
            if (handle != null && interactive)
            {
                Rect hr = RectOf(ctx, handle) ?? tr;
                Rect slide = horizontal ? new Rect(tr.x + hr.width * 0.5f, hr.y, Mathf.Max(1, tr.width - hr.width), hr.height) :
                    new Rect(hr.x, tr.y + hr.height * 0.5f, hr.width, Mathf.Max(1, tr.height - hr.height));
                var sliding = EnsureChild(root.transform, "Handle Slide Area", slide, bounds);
                var handleGo = EnsureChild(sliding.transform, "Handle", hr, slide);
                CopyImageTo(ctx, handle, handleGo);
                handleRt = (RectTransform)handleGo.transform;
                handleGraphic = handleGo.GetComponent<Graphic>() ?? handleGo.GetComponentInChildren<Graphic>(true);
                value = horizontal ? (reverse ? slide.xMax - hr.center.x : hr.center.x - slide.xMin) / Mathf.Max(1, slide.width) :
                    (reverse ? hr.center.y - slide.yMin : slide.yMax - hr.center.y) / Mathf.Max(1, slide.height);
            }
            value = Mathf.Clamp01(value);
            if (image != null && !image.hasBorder)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = horizontal ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
                image.fillOrigin = reverse ? 1 : 0;
                image.fillAmount = value;
            }
            if (!interactive)
            {
                if (image != null && image.hasBorder)
                {
                    var rt = (RectTransform)fillGo.transform;
                    if (horizontal) { rt.anchorMin = new Vector2(reverse ? 1 - value : 0, 0); rt.anchorMax = new Vector2(reverse ? 1 : value, 1); }
                    else { rt.anchorMin = new Vector2(0, reverse ? 1 - value : 0); rt.anchorMax = new Vector2(1, reverse ? 1 : value); }
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                }
                return;
            }
            var slider = root.AddComponent<Slider>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.direction = horizontal ? (reverse ? Slider.Direction.RightToLeft : Slider.Direction.LeftToRight) :
                (reverse ? Slider.Direction.TopToBottom : Slider.Direction.BottomToTop);
            slider.fillRect = (RectTransform)fillGo.transform;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleGraphic ?? (Graphic)image ?? root.AddComponent<NonDrawingGraphic>();
            slider.targetGraphic.raycastTarget = true;
            slider.SetValueWithoutNotify(value);
        }
        static void BuildExplicitProgress(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            var fill = Slot(ctx, node, "fill");
            if (fill == null) throw new System.InvalidOperationException("Progress 缺少 fill 图片，请指定填充插槽。");
            var track = Slot(ctx, node, "track") ?? Slot(ctx, node, "bg");
            CreateDecorChildren(ctx, node, root, bounds, -1, -1, "track", "bg", "fill");
            if (track != null) CopyImageTo(ctx, track, EnsureChild(root.transform, "Background", RectOf(ctx, track) ?? bounds, bounds));
            Rect rect = node.progressFill >= ProgressFillMode.Radial360 ? RectOf(ctx, fill) ?? bounds : RectOf(ctx, track) ?? bounds;
            var go = EnsureChild(root.transform, "Fill", rect, bounds);
            CopyImageTo(ctx, fill, go);
            var image = go.GetComponent<Image>() ?? go.GetComponentInChildren<Image>(true);
            if (image == null) throw new System.InvalidOperationException("Progress 填充必须为 Sprite/Image，不能使用 RawImage。");
            image.type = Image.Type.Filled; image.raycastTarget = false;
            switch (node.progressFill)
            {
                case ProgressFillMode.Horizontal: image.fillMethod = Image.FillMethod.Horizontal; break;
                case ProgressFillMode.Vertical: image.fillMethod = Image.FillMethod.Vertical; break;
                case ProgressFillMode.Radial90: image.fillMethod = Image.FillMethod.Radial90; break;
                case ProgressFillMode.Radial180: image.fillMethod = Image.FillMethod.Radial180; break;
                default: image.fillMethod = Image.FillMethod.Radial360; break;
            }
            image.fillOrigin = node.progressFill <= ProgressFillMode.Vertical ? (node.progressReverse ? 1 : 0) : Mathf.Clamp(node.progressOrigin, 0, 3);
            image.fillClockwise = node.progressClockwise; image.fillAmount = Mathf.Clamp01(node.progressValue);
        }
    }
}
