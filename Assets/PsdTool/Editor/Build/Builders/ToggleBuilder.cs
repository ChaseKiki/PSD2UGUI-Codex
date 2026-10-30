using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class ToggleBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            BuildToggle(ctx, node, go, bounds, node.templateId == "Tab");
        }

        internal static void BuildToggle(ControlBuildContext ctx, PsdNode node, GameObject go, Rect rootRect, bool isTab)
        {
            CreateVisualChildren(ctx, node, go, rootRect);
            var toggle = go.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };

            toggle.targetGraphic = go.GetComponent<NonDrawingGraphic>() ?? go.AddComponent<NonDrawingGraphic>();

            var check = Slot(ctx, node, "checkmark") ?? Slot(ctx, node, "on");
            var on = Slot(ctx, node, "on");
            var labelOn = Slot(ctx, node, "labelOn");
            if (check != null && ctx.Map.TryGetValue(check.layerId, out var checkGo))
            {
                toggle.graphic = checkGo.GetComponent<Graphic>() ?? checkGo.GetComponentInChildren<Graphic>(true);
                checkGo.SetActive(true);
                toggle.SetIsOnWithoutNotify(check.visible);
            }
            else if (!isTab)
                ctx.Tree.warnings.Add($"开关“{node.goName}”没有勾选图。把勾选图层的插槽设为 checkmark，或使用 AI 标记。");

            if (labelOn != null || (on != null && on.kind == LayerKind.Group))
            {
                var visual = go.AddComponent<ToggleVisualGroup>();
                var onList = new List<GameObject>();
                if (on != null && ctx.Map.TryGetValue(on.layerId, out var og)) onList.Add(og);
                if (labelOn != null && ctx.Map.TryGetValue(labelOn.layerId, out var lg)) onList.Add(lg);
                visual.onObjects = onList.ToArray();
                var label = Slot(ctx, node, "label");
                if (label != null && labelOn != null && ctx.Map.TryGetValue(label.layerId, out var offLabel))
                    visual.offObjects = new[] { offLabel };
            }

            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = g == toggle.targetGraphic || g == toggle.graphic;

            if (isTab) toggle.isOn = node.visible;
        }

        internal static void WireToggleGroups(ControlBuildContext ctx, int parentId)
        {
            var parent = ctx.Tree.Find(parentId);
            ToggleGroup group = null;
            if (parent != null && parent.templateId == "ToggleGroup" && ctx.Map.TryGetValue(parentId, out var pgo))
                group = pgo.GetComponent<ToggleGroup>();
            if (group == null && ctx.Map.TryGetValue(parentId, out var fallback))
                group = fallback.GetComponent<ToggleGroup>();

            if (group == null) return;
            var toggles = new List<Toggle>();
            foreach (var child in ctx.ChildrenOf(parentId))
            {
                if (child.templateId != "Toggle" && child.templateId != "Tab") continue;
                if (!ctx.Map.TryGetValue(child.layerId, out var go)) continue;
                var toggle = go.GetComponent<Toggle>();
                if (toggle == null) continue;
                toggles.Add(toggle);
            }
            var selected = toggles.Find(t => t.isOn) ?? (toggles.Count > 0 ? toggles[0] : null);
            foreach (var toggle in toggles) toggle.SetIsOnWithoutNotify(false);
            foreach (var toggle in toggles) toggle.group = group;
            if (selected != null) selected.SetIsOnWithoutNotify(true);
        }
    }
}
