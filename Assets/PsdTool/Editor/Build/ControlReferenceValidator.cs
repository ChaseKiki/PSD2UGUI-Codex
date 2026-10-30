using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    /// <summary>检查生成结果的结构关系；验证对象可以是临时树或保存后的 Prefab。</summary>
    internal static class ControlReferenceValidator
    {
        internal sealed class Issue
        {
            public Component component;
            public string message;
            public override string ToString() => component.name + " [" + component.GetType().Name + "]：" + message;
        }
        internal static List<string> Validate(GameObject root) => Details(root).Select(i => i.ToString()).ToList();
        internal static List<Issue> Details(GameObject root)
        {
            var errors = new List<Issue>();
            foreach (var scroll in root.GetComponentsInChildren<ScrollRect>(true))
            {
                Check(scroll.viewport != null && scroll.viewport.IsChildOf(scroll.transform), scroll, "viewport 必须在 ScrollRect 下", errors);
                Check(scroll.content != null && scroll.viewport != null && scroll.content.parent == scroll.viewport, scroll, "content 必须直接挂在 viewport 下", errors);
                Check(scroll.viewport != null && (scroll.viewport.GetComponent<RectMask2D>() != null || scroll.viewport.GetComponent<Mask>() != null), scroll, "viewport 缺少裁剪组件", errors);
                Check(scroll.horizontalScrollbar == null || scroll.horizontalScrollbar.direction == Scrollbar.Direction.LeftToRight || scroll.horizontalScrollbar.direction == Scrollbar.Direction.RightToLeft, scroll, "横向滚动条方向不对", errors);
                Check(scroll.verticalScrollbar == null || scroll.verticalScrollbar.direction == Scrollbar.Direction.BottomToTop || scroll.verticalScrollbar.direction == Scrollbar.Direction.TopToBottom, scroll, "纵向滚动条方向不对", errors);
            }
            foreach (var bar in root.GetComponentsInChildren<Scrollbar>(true))
            {
                Check(bar.handleRect != null && bar.handleRect.IsChildOf(bar.transform), bar, "handleRect 必须在滚动条下", errors);
                Check(bar.targetGraphic != null, bar, "缺少 targetGraphic", errors);
            }
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                Check(input.targetGraphic != null, input, "缺少 targetGraphic", errors);
                Check(input.textViewport != null && input.textViewport.IsChildOf(input.transform), input, "textViewport 缺失或不在输入框下", errors);
                Check(input.textComponent != null && input.textViewport != null && input.textComponent.transform.IsChildOf(input.textViewport), input, "输入文字不在 textViewport 下", errors);
                Check(input.placeholder == null || (input.textViewport != null && input.placeholder.transform.IsChildOf(input.textViewport)), input, "placeholder 不在 textViewport 下", errors);
                Check(input.placeholder == null || input.placeholder != input.textComponent, input, "placeholder 与 textComponent 不可复用同一个组件", errors);
            }
            foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (!dropdown.interactable && dropdown.template == null) continue; // 已明确降级。
                Check(dropdown.targetGraphic != null, dropdown, "缺少 targetGraphic", errors);
                var template = dropdown.template;
                Check(template != null && template.IsChildOf(dropdown.transform) && template != dropdown.transform, dropdown, "template 层级不对", errors);
                if (template == null) continue;
                Check(!template.gameObject.activeSelf, dropdown, "template 必须默认隐藏", errors);
                var scroll = template.GetComponent<ScrollRect>();
                var items = scroll != null && scroll.content != null ? scroll.content.GetComponentsInChildren<Toggle>(true) : new Toggle[0];
                Check(items.Length >= 1, dropdown, "template/Content 缺少 Toggle 选项模板", errors);
                var item = items.FirstOrDefault();
                if (item == null) continue;
                Check(dropdown.itemText != null || dropdown.itemImage != null, dropdown, "缺少 itemText/itemImage", errors);
                Check(dropdown.itemText == null || dropdown.itemText.transform.IsChildOf(item.transform), dropdown, "itemText 不属于选项 Toggle", errors);
                Check(dropdown.itemImage == null || dropdown.itemImage.transform.IsChildOf(item.transform), dropdown, "itemImage 不属于选项 Toggle", errors);
                var group = scroll.content.GetComponent<LayoutGroup>();
                var fitter = scroll.content.GetComponent<ContentSizeFitter>();
                Check((group == null || !group.enabled) && (fitter == null || !fitter.enabled), dropdown, "TMP Dropdown 的 Content 不应启用 LayoutGroup/ContentSizeFitter", errors);
            }
            foreach (var slider in root.GetComponentsInChildren<Slider>(true))
            {
                Check(slider.fillRect != null || slider.handleRect != null, slider, "fillRect 和 handleRect 均缺失", errors);
                Check(slider.fillRect == null || slider.fillRect.IsChildOf(slider.transform), slider, "fillRect 不属于 Slider", errors);
                Check(slider.handleRect == null || slider.handleRect.IsChildOf(slider.transform), slider, "handleRect 不属于 Slider", errors);
            }
            return errors;
        }

        internal static void EnsureValid(GameObject root, ControlBuildContext context = null)
        {
            var errors = Details(root);
            if (errors.Count != 0 && context != null)
            {
                var issue = errors[0];
                for (var current = issue.component.transform; current != null; current = current.parent)
                {
                    if (context.ControlOwners.TryGetValue(current.gameObject, out int owner))
                        throw new ControlBuildException(owner, issue.message);
                    var node = context.Tree.nodes.Find(n => PrefabGenerator.IsControl(n.templateId) && context.Map.TryGetValue(n.layerId, out var go) && go == current.gameObject);
                    if (node != null) throw new ControlBuildException(node.layerId, issue.message);
                }
            }
            if (errors.Count != 0) throw new InvalidOperationException("复合控件校验失败，未保存目标 Prefab：\n" + string.Join("\n", errors));
        }

        static void Check(bool valid, Component component, string message, List<Issue> errors)
        {
            if (!valid) errors.Add(new Issue { component = component, message = message });
        }
    }
}
