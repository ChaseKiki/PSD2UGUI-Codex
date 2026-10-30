using System.Collections.Generic;

namespace PsdTool
{
    internal static class StretchImagePolicy
    {
        // FX 默认保持完整像素；滚动条/Slider 的可变长度滑块必须额外做九宫回验。
        internal static bool NeedsSliceCheck(PsdTree tree, PsdNode node)
        {
            var visited = new HashSet<int>();
            for (var current = node; current != null && visited.Add(current.layerId); current = tree.Find(current.parentId))
                if (current.slot == "vbar" || current.slot == "hbar" || current.slot == "handle" || current.templateId == "Scrollbar") return true;
            return false;
        }
    }
}
