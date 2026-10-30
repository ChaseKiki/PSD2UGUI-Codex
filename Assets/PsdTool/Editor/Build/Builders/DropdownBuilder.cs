using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class DropdownBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject root, Rect bounds)
        {
            var templateNode = Slot(ctx, node, "template") ?? ctx.ChildrenOf(node.layerId).Find(n => n.templateId == "ScrollView");
            // template 的结构已在上下文中归一化；平级选项也包含在其 Plan 中。
            var optionNodes = templateNode == null ? new List<PsdNode>() : ScrollViewBuilder.Describe(ctx, templateNode).items;
            optionNodes = optionNodes.OrderBy(n => (RectOf(ctx, n) ?? bounds).y).ThenBy(n => (RectOf(ctx, n) ?? bounds).x).ToList();
            CreateVisualChildren(ctx, node, root, bounds);
            // 根节点沿用外部 UI Canvas；TMP 仅在运行时为展开列表配置排序 Canvas。
            var dropdown = root.AddComponent<TMP_Dropdown>();
            dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
            var bg = Slot(ctx, node, "bg");
            if (bg != null && ctx.Map.TryGetValue(bg.layerId, out var bgGo))
                dropdown.targetGraphic = bgGo.GetComponent<Graphic>() ?? bgGo.GetComponentInChildren<Graphic>(true);
            if (dropdown.targetGraphic == null) dropdown.targetGraphic = root.AddComponent<NonDrawingGraphic>();
            dropdown.targetGraphic.raycastTarget = true;
            var caption = Slot(ctx, node, "caption") ?? Slot(ctx, node, "label");
            if (caption != null && ctx.Map.TryGetValue(caption.layerId, out var captionGo))
                dropdown.captionText = captionGo.GetComponent<TMP_Text>();
            string current = dropdown.captionText != null ? dropdown.captionText.text : string.Empty;

            if (templateNode == null || !ctx.Map.TryGetValue(templateNode.layerId, out var template))
            {
                dropdown.interactable = false;
                ctx.Tree.warnings.Add("下拉“" + node.goName + "”缺少 template，已保留外观并禁用交互；请指定展开列表后重新生成。");
                ReportDisabled(ctx, node, "缺少展开列表 template");
                return;
            }
            var scroll = template.GetComponent<ScrollRect>();
            var item = scroll != null && scroll.content != null ? scroll.content.GetComponentInChildren<Toggle>(true) : null;
            if (item == null)
            {
                dropdown.interactable = false;
                template.SetActive(false);
                ctx.Tree.warnings.Add("下拉“" + node.goName + "”缺少带 Toggle 的 item，已禁用交互；请指定选项组。");
                ReportDisabled(ctx, node, "展开列表缺少 Toggle 选项模板");
                return;
            }
            dropdown.itemText = item.GetComponentInChildren<TMP_Text>(true);
            var itemSource = optionNodes.FirstOrDefault();
            var icon = itemSource != null ? Slot(ctx, itemSource, "icon") : null;
            if (icon != null && ctx.Map.TryGetValue(icon.layerId, out var iconGo)) dropdown.itemImage = iconGo.GetComponent<Image>();
            if (dropdown.itemText == null && dropdown.itemImage == null)
            {
                dropdown.interactable = false;
                template.SetActive(false);
                ctx.Tree.warnings.Add("下拉“" + node.goName + "”的 item 缺少文字和图标，已禁用交互。");
                ReportDisabled(ctx, node, "选项缺少文字或图标");
                return;
            }
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var option in optionNodes)
            {
                var text = FindText(ctx, option);
                var optionIcon = Slot(ctx, option, "icon");
                options.Add(new TMP_Dropdown.OptionData(text?.text?.content ?? string.Empty, optionIcon != null ? LoadSprite(optionIcon) : null));
            }
            if (options.Count == 0) options.Add(new TMP_Dropdown.OptionData(current));
            int selected = options.FindIndex(o => o.text == current);
            if (!string.IsNullOrEmpty(current) && selected < 0)
            {
                options.Insert(0, new TMP_Dropdown.OptionData(current)); selected = 0;
                ctx.Tree.warnings.Add("下拉“" + node.goName + "”当前文案不在 PSD 选项中，暂作为首项保留；请在程序中配置最终选项。");
            }
            dropdown.options = options;
            dropdown.template = (RectTransform)template.transform;
            dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
            dropdown.RefreshShownValue();
            template.SetActive(false);
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                if (!graphic.transform.IsChildOf(template.transform)) graphic.raycastTarget = graphic == dropdown.targetGraphic;
        }

        static void ReportDisabled(ControlBuildContext ctx, PsdNode node, string reason)
        {
            ctx.Repairs.Add(new GenerationIssue { layerId = node.layerId, type = "Dropdown", message = reason,
                resolution = "已保留外观并禁用该下拉交互，其余控件继续生成。请在 Unity 指定展开列表及选项部件后重新生成。" });
        }
        static PsdNode FindText(ControlBuildContext ctx, PsdNode parent)
        {
            foreach (var child in ctx.ChildrenOf(parent.layerId))
                if (child.kind == LayerKind.Text && (child.slot == "label" || child.slot == "text")) return child;
            foreach (var child in ctx.ChildrenOf(parent.layerId))
            {
                if (child.kind == LayerKind.Text) return child;
                var found = FindText(ctx, child); if (found != null) return found;
            }
            return null;
        }
    }
}
