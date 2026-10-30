using System;
using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    internal static class ScrollLayoutOptions
    {
        internal static void Copy(PsdNode from, PsdNode to)
        {
            if (!Enum.IsDefined(typeof(ScrollLayoutMode), from.scrollLayout) || float.IsNaN(from.scrollSpacing) || float.IsInfinity(from.scrollSpacing))
                throw new InvalidOperationException("排列方式或间距无效。");
            to.scrollLayout = from.scrollLayout; to.scrollSpacing = Mathf.Max(0, from.scrollSpacing);
            to.scrollPaddingLeft = Mathf.Max(0, from.scrollPaddingLeft); to.scrollPaddingRight = Mathf.Max(0, from.scrollPaddingRight);
            to.scrollPaddingTop = Mathf.Max(0, from.scrollPaddingTop); to.scrollPaddingBottom = Mathf.Max(0, from.scrollPaddingBottom);
        }
        internal static void Apply(PsdNode node, RectTransform content)
        {
            if (node.scrollLayout == ScrollLayoutMode.KeepPsd) return;
            bool vertical = node.scrollLayout == ScrollLayoutMode.Vertical;
            var layout = vertical ? (HorizontalOrVerticalLayoutGroup)content.gameObject.AddComponent<VerticalLayoutGroup>() : content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = Mathf.Max(0, node.scrollSpacing);
            layout.padding = new RectOffset(node.scrollPaddingLeft, node.scrollPaddingRight, node.scrollPaddingTop, node.scrollPaddingBottom);
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = !vertical; layout.childForceExpandWidth = vertical;
            foreach (RectTransform child in content)
            {
                var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = child.rect.height; element.preferredWidth = child.rect.width;
                element.flexibleHeight = element.flexibleWidth = 0;
            }
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.horizontalFit = vertical ? ContentSizeFitter.FitMode.Unconstrained : ContentSizeFitter.FitMode.PreferredSize;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }
    }
}
