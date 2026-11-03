using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class ButtonBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            BuildButton(ctx, node, go, bounds);
        }

        internal static void BuildButton(ControlBuildContext ctx, PsdNode node, GameObject go, Rect rootRect)
        {
            if (node.kind != LayerKind.Group && node.kind != LayerKind.Text)
            {
                var visual = EnsureChild(go.transform, "Source Visual", rootRect, rootRect);
                CreateImage(ctx, node, visual);
            }
            CreateVisualChildren(ctx, node, go, rootRect);

            var button = go.AddComponent<Button>();
            button.transition = node.buttonColorTint?Selectable.Transition.ColorTint:Selectable.Transition.None;
            var colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(.9f, .9f, .9f, 1f);
            colors.pressedColor = new Color(.7f, .7f, .7f, 1f);
            colors.selectedColor = new Color(.95f, .95f, .95f, 1f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            Image target = null;
            float largest = -1;
            var corners = new Vector3[4];
            foreach (var image in go.GetComponentsInChildren<Image>(true))
            {
                bool visible = image.enabled && image.color.a > 0;
                for (var t = image.transform; t != null; t = t.parent)
                {
                    if (!t.gameObject.activeSelf) visible = false;
                    foreach (var sourceNode in ctx.Tree.nodes)
                        if (IsStateSlot(sourceNode.slot) && ctx.Map.TryGetValue(sourceNode.layerId, out var stateGo) && stateGo.transform == t) visible = false;
                    if (t == go.transform) break;
                }
                if (!visible) continue;
                image.rectTransform.GetWorldCorners(corners);
                float area = Vector3.Cross(corners[1] - corners[0], corners[3] - corners[0]).magnitude;
                if (area > largest) { largest = area; target = image; }
            }
            button.targetGraphic = target;

            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
            // 根热区覆盖全部视觉范围，适用于纯图标、多层背景和主副标题按钮。
            var hit = go.GetComponent<NonDrawingGraphic>() ?? go.AddComponent<NonDrawingGraphic>();
            hit.raycastTarget = true;
            if (button.targetGraphic == null) button.targetGraphic = hit;

            // Do not enlarge the root after positioning the source children: that
            // shifts center-anchored artwork and creates an invisible oversized hit area.

            button.spriteState = default;

            if (ctx.Settings.addButtonGray) go.AddComponent<UIGray>();
        }
    }
}
