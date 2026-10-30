using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static PsdTool.PrefabGenerator;

namespace PsdTool
{
    internal sealed class MaskBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            BuildMask(ctx, node, go, bounds);
        }

        internal static void BuildMask(ControlBuildContext ctx, PsdNode node, GameObject go, Rect rootRect)
        {
            CreateVisualChildren(ctx, node, go, rootRect);
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
            var source = Slot(ctx, node, "bg");
            if (source != null)
            {
                img.sprite = LoadSprite(source);
                img.type = source.image.sliced ? Image.Type.Sliced : Image.Type.Simple;
            }
            else if (node.kind != LayerKind.Group) CreateImage(ctx, node, go);
            var mask = go.AddComponent<Mask>();
            mask.showMaskGraphic = false;
        }
    }
}
