using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PsdTool
{
    internal sealed class AiRegion
    {
        public int rootId;
        public RectInt rect;
        public string image, type, error, resolution;
        public float confidence;
    }

    /// <summary>区域使用 PSD 栅格边界；文字排版框可能远大于可见字形，不用于视觉定位。</summary>
    internal static class AiRegions
    {
        internal static List<PsdNode> Children(PsdTree tree, int id) => tree.nodes.Where(n => n.parentId == id && n.status != NodeStatus.PendingDelete).OrderBy(n => n.layerId).ToList();
        internal static Rect Bounds(PsdTree tree, PsdNode node)
        {
            return Bounds(tree, node, new HashSet<int>());
        }
        static Rect Bounds(PsdTree tree, PsdNode node, HashSet<int> visited)
        {
            if (!visited.Add(node.layerId)) throw new InvalidOperationException("区域定位发现父子循环。");
            Rect r = new Rect(node.psdRect.x, node.psdRect.y, node.psdRect.width, node.psdRect.height);
            if (node.kind == LayerKind.Group && !node.aiBoundsOverride)
            {
                Rect? result = null;
                foreach (var child in Children(tree, node.layerId))
                {
                    var b = Bounds(tree, child, visited);
                    if (b.width > 0 && b.height > 0) result = result.HasValue ? PrefabGenerator.Union(result.Value, b) : b;
                }
                r = result ?? new Rect();
            }
            visited.Remove(node.layerId); return r;
        }
        internal static List<AiRegion> Prepare(PsdTree tree, Color32[] composite, int width, int height, int maxSide)
        {
            AiPlanEngine.CheckParents(tree);
            var result = new List<AiRegion>();
            foreach (var group in tree.nodes.Where(n => n.status != NodeStatus.PendingDelete &&
                (n.kind == LayerKind.Group || n.parentId == -1 && n.kind != LayerKind.Text)).OrderBy(n => n.layerId))
            {
                // 包装组和顶层散图也可作为候选；混合布局由模型明确返回 None。
                if (group.kind == LayerKind.Group && Children(tree, group.layerId).Count == 0) continue;
                var b = Bounds(tree, group);
                int x = Mathf.Clamp(Mathf.FloorToInt(b.xMin) - 4, 0, width), y = Mathf.Clamp(Mathf.FloorToInt(b.yMin) - 4, 0, height);
                int right = Mathf.Clamp(Mathf.CeilToInt(b.xMax) + 4, 0, width), bottom = Mathf.Clamp(Mathf.CeilToInt(b.yMax) + 4, 0, height);
                if (right <= x || bottom <= y) continue;
                var rect = new RectInt(x, y, right - x, bottom - y);
                result.Add(new AiRegion { rootId = group.layerId, rect = rect,
                    image = AiVisualInput.EncodeFit(PixelUtil.Crop(composite, width, rect), rect.width, rect.height, Mathf.Clamp(maxSide, 512, 1536)) });
            }
            if (result.Count > 100) throw new InvalidOperationException("候选控件区域超过 100 个，请按界面拆分 PSD；不会静默漏掉区域。");
            return result;
        }
        internal static int Depth(PsdTree tree, int id)
        {
            int depth = 0;
            for (var node = tree.Find(id); node != null; node = tree.Find(node.parentId)) depth++;
            return depth;
        }
    }
}
