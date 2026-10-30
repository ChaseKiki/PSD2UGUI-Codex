using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class SliderBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            bool interactive = node.templateId != "Progress";
            if (!interactive && (Slot(ctx, node, "fill") == null ||
                node.progressFill < ProgressFillMode.Radial360 && Slot(ctx, node, "track") == null && Slot(ctx, node, "bg") == null))
            {
                BuildIncompleteProgress(ctx, node, root, bounds);
                return;
            }
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
        static void BuildIncompleteProgress(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            var track = Slot(ctx, node, "track") ?? Slot(ctx, node, "bg");
            var fill = Slot(ctx, node, "fill");
            CreateDecorChildren(ctx, node, root, bounds, -1, -1, "track", "bg", "fill");
            bool sourceBackground = track == null && node.kind != LayerKind.Group && node.kind != LayerKind.Text;
            var rect = RectOf(ctx, track) ?? RectOf(ctx, fill) ?? bounds;
            if (rect.width <= 0 || rect.height <= 0) rect = new Rect(bounds.position, new Vector2(100, 12));
            var background = EnsureChild(root.transform, "Background", rect, bounds);
            if (track != null) CopyImageTo(ctx, track, background);
            else if (sourceBackground) CreateImage(ctx, node, background);
            else background.AddComponent<Image>().color = new Color(.15f, .15f, .15f, .35f);
            var visual = EnsureChild(root.transform, "Fill", rect, bounds);
            if (fill != null) CopyImageTo(ctx, fill, visual);
            var image = visual.GetComponent<Image>();
            if (image == null && fill == null) { image = visual.AddComponent<Image>(); image.color = new Color(.25f, .7f, 1, .5f); }
            if (image != null)
            {
                // Image.Filled needs a persistent sprite to render fillAmount;
                // a sprite-less solid Image would always draw its full rectangle.
                if (image.sprite == null) image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                image.type = Image.Type.Filled;
                image.fillMethod = node.progressFill >= ProgressFillMode.Radial360 ?
                    node.progressFill == ProgressFillMode.Radial90 ? Image.FillMethod.Radial90 :
                    node.progressFill == ProgressFillMode.Radial180 ? Image.FillMethod.Radial180 : Image.FillMethod.Radial360 :
                    node.progressFill == ProgressFillMode.Vertical || node.progressFill == ProgressFillMode.Auto && rect.height > rect.width ? Image.FillMethod.Vertical : Image.FillMethod.Horizontal;
                image.fillOrigin = node.progressFill >= ProgressFillMode.Radial360 ? Mathf.Clamp(node.progressOrigin, 0, 3) : node.progressReverse ? 1 : 0;
                image.fillClockwise = node.progressClockwise;
                image.fillAmount = Mathf.Clamp01(node.progressValue);
            }
            foreach (var graphic in background.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var graphic in visual.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            ctx.Repairs.Add(new GenerationIssue { layerId = node.layerId, type = "Progress", placeholder = true,
                message = "Progress 部件不完整，已补建 " + (track == null && !sourceBackground ? "纯色 Background " : "") + (fill == null ? "纯色 Fill" : "") +
                    (sourceBackground ? "；保留原图作为 Background" : ""),
                resolution = "Prefab 可以继续生成。占位尺寸参考已有部件或控件范围，颜色/方向/数值需验收；生成后可在 Unity 替换占位图，无需返回 PSD。" });
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
