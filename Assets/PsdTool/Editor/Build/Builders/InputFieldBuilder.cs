using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class InputFieldBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            Graphic background = null;
            if (node.kind != LayerKind.Group && node.kind != LayerKind.Text)
            {
                var own = EnsureChild(root.transform, "bg", bounds, bounds, "stretch");
                CreateImage(ctx, node, own);
                background = own.GetComponent<Graphic>() ?? own.GetComponentInChildren<Graphic>(true);
            }
            CreateVisualChildren(ctx, node, root, bounds);
            var bg = Slot(ctx, node, "bg");
            if (bg != null && ctx.Map.TryGetValue(bg.layerId, out var bgGo))
                background = bgGo.GetComponent<Graphic>() ?? bgGo.GetComponentInChildren<Graphic>(true);
            var placeholderNode = Slot(ctx, node, "placeholder");
            var textNode = Slot(ctx, node, "text");
            TMP_Text placeholder = TextFrom(ctx, placeholderNode);
            TMP_Text text = TextFrom(ctx, textNode);
            Rect textRect = RectOf(ctx, textNode) ?? RectOf(ctx, placeholderNode) ??
                new Rect(bounds.x + 8, bounds.y + 4, Mathf.Max(1, bounds.width - 16), Mathf.Max(1, bounds.height - 8));
            Rect areaRect = textRect;
            var placeholderRect = RectOf(ctx, placeholderNode) ?? textRect;
            if (placeholder != null) areaRect = Union(areaRect, placeholderRect);
            var area = EnsureChild(root.transform, "Text Area", areaRect, bounds);
            area.AddComponent<RectMask2D>();
            if (text == null)
            {
                if (placeholder != null)
                {
                    var clone = Object.Instantiate(placeholder.gameObject, area.transform);
                    clone.name = "Text";
                    text = clone.GetComponent<TMP_Text>();
                    var color = text.color; color.a = 1; text.color = color;
                    text.text = string.Empty;
                }
                else
                {
                    var textGo = EnsureChild(area.transform, "Text", textRect, areaRect);
                    var synthetic = new PsdNode { opacity = 1, goName = "Text", text = new TextInfo
                    { content = string.Empty, fontSize = 24, color = Color.white, rect = textRect } };
                    CreateText(ctx, synthetic, textGo);
                    text = textGo.GetComponent<TMP_Text>();
                    ctx.Tree.warnings.Add("输入框“" + node.goName + "”没有文字样式，已使用默认字体/24 号；请指定 placeholder 或 text。");
                }
            }
            text.gameObject.SetActive(true);
            text.transform.SetParent(area.transform, false);
            AnchorUtil.Apply((RectTransform)text.transform, textRect, areaRect, "stretch", 0);
            text.raycastTarget = false;
            if (placeholder != null)
            {
                placeholder.gameObject.SetActive(true);
                placeholder.transform.SetParent(area.transform, false);
                AnchorUtil.Apply((RectTransform)placeholder.transform, placeholderRect, areaRect, "stretch", 0);
                placeholder.raycastTarget = false;
            }
            var input = root.AddComponent<TMP_InputField>();
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            input.targetGraphic = background != null ? background : root.AddComponent<NonDrawingGraphic>();
            input.targetGraphic.raycastTarget = true;
            input.textViewport = (RectTransform)area.transform;
            input.textComponent = text;
            input.placeholder = placeholder;
            bool multiline = textNode != null && (textNode.text.lineCount > 1 || (textNode.text.content ?? "").Contains("\n"));
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.text = textNode != null ? textNode.text.content ?? "" : "";
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = graphic == input.targetGraphic;
        }

        static TMP_Text TextFrom(ControlBuildContext ctx, PsdNode node) =>
            node != null && ctx.Map.TryGetValue(node.layerId, out var go) ? go.GetComponent<TMP_Text>() : null;
    }
}
