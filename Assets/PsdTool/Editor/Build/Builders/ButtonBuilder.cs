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
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var bg = Slot(ctx, node, "bg");
            Graphic target = null;
            if (bg != null && ctx.Map.TryGetValue(bg.layerId, out var bgGo))
                target = bgGo.GetComponent<Graphic>();
            if (target == null) target = go.GetComponentInChildren<Graphic>();
            button.targetGraphic = target;

            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = g == target;
            // 根热区覆盖全部视觉范围，适用于纯图标、多层背景和主副标题按钮。
            var hit = go.GetComponent<NonDrawingGraphic>() ?? go.AddComponent<NonDrawingGraphic>();
            hit.raycastTarget = true;
            if (button.targetGraphic == null) button.targetGraphic = hit;

            float scale = node.pressScale >= 0f ? node.pressScale : ctx.Settings.addButtonPressScale ? ctx.Settings.buttonPressScale : 1f;
            if (scale < .999f)
            {
                var press = go.AddComponent<ButtonPressScale>();
                press.pressedScale = scale;
                press.duration = ctx.Settings.buttonPressDuration;
            }

            // Do not enlarge the root after positioning the source children: that
            // shifts center-anchored artwork and creates an invisible oversized hit area.

            var pressed = Slot(ctx, node, "pressed");
            var disabled = Slot(ctx, node, "disabled");
            if (pressed != null || disabled != null)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                var state = button.spriteState;
                if (pressed != null) state.pressedSprite = LoadSprite(pressed);
                if (disabled != null) state.disabledSprite = LoadSprite(disabled);
                button.spriteState = state;
            }

            if (ctx.Settings.addButtonGray) go.AddComponent<UIGray>();
        }
    }
}
