using System.Collections.Generic;
using UnityEngine;

namespace PsdTool
{
    internal static class ButtonHitSources
    {
        internal static string Key(PsdNode node) => string.IsNullOrEmpty(node.stableKey) ? "layer:" + node.layerId : node.stableKey;

        internal static bool BelongsTo(PsdTree tree, PsdNode node, PsdNode button)
        {
            var seen = new HashSet<int>();
            for (var cursor = node; cursor != null && seen.Add(cursor.layerId); cursor = tree.Find(cursor.parentId))
                if (cursor == button) return true;
            return false;
        }

        internal static bool NormalVisual(PsdTree tree, PsdNode node, PsdNode button)
        {
            if (!BelongsTo(tree, node, button)) return false;
            for (var cursor = node; cursor != null; cursor = tree.Find(cursor.parentId))
            {
                if (!cursor.visible || cursor.status == NodeStatus.PendingDelete || PrefabGenerator.IsStateSlot(cursor.slot)) return false;
                if (cursor == button) break;
            }
            return true;
        }

        internal static Rect? Bounds(ControlBuildContext ctx, PsdNode button)
        {
            Rect? result = null;
            foreach (var key in button.buttonHitKeys)
            {
                var node = ctx.Tree.nodes.Find(n => Key(n) == key);
                if (node == null || !NormalVisual(ctx.Tree, node, button)) continue;
                var rect = Visual(ctx, node, button);
                if (rect.HasValue) result = result.HasValue ? PrefabGenerator.Union(result.Value, rect.Value) : rect;
            }
            return result;
        }

        static Rect? Visual(ControlBuildContext ctx, PsdNode node, PsdNode button)
        {
            if (!NormalVisual(ctx.Tree, node, button)) return null;
            if (node.kind == LayerKind.Group)
            {
                Rect? result = null;
                foreach (var child in ctx.ChildrenOf(node.layerId))
                {
                    var rect = Visual(ctx, child, button);
                    if (rect.HasValue) result = result.HasValue ? PrefabGenerator.Union(result.Value, rect.Value) : rect;
                }
                return result;
            }
            if (node.kind == LayerKind.Text)
                return node.text != null && !string.IsNullOrEmpty(node.text.content) && node.text.rect.width > 0 && node.text.rect.height > 0 ? node.text.rect : (Rect?)null;
            if (node.image == null || node.image.isEmpty) return null;
            var r = node.image.fxBaked && node.image.logicalRect.width > 0 ? node.image.logicalRect : node.image.trimmedRect;
            return r.width > 0 && r.height > 0 ? new Rect(r.x, r.y, r.width, r.height) : (Rect?)null;
        }
    }
}
