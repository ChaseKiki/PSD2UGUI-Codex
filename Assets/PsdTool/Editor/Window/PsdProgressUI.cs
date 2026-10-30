using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class PsdProgressUI
    {
        static readonly string[] Modes = { "自动（线性）", "横向", "纵向", "环形 360°", "扇形 180°", "扇形 90°" };
        internal static void Draw(PsdNode node)
        {
            node.progressFill = (ProgressFillMode)EditorGUILayout.Popup("填充方式", (int)node.progressFill, Modes);
            if (node.progressFill == ProgressFillMode.Auto) return;
            if (node.progressFill <= ProgressFillMode.Vertical)
                node.progressReverse = EditorGUILayout.Popup("填充方向", node.progressReverse ? 1 : 0, node.progressFill == ProgressFillMode.Horizontal ? new[] { "从左到右", "从右到左" } : new[] { "从下到上", "从上到下" }) == 1;
            else
            {
                node.progressClockwise = EditorGUILayout.Popup("旋转方向", node.progressClockwise ? 0 : 1, new[] { "顺时针", "逆时针" }) == 0;
                var origins = node.progressFill == ProgressFillMode.Radial90 ? new[] { "左下", "左上", "右上", "右下" } : node.progressFill == ProgressFillMode.Radial180 ? new[] { "下", "左", "上", "右" } : new[] { "下", "右", "上", "左" };
                node.progressOrigin = EditorGUILayout.Popup("起点", Mathf.Clamp(node.progressOrigin, 0, 3), origins);
            }
            node.progressValue = EditorGUILayout.Slider("初始进度", node.progressValue, 0, 1);
        }
    }
}
