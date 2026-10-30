using System;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class ScrollLayoutWindow : EditorWindow
    {
        PsdNode options;
        Action<PsdNode> apply;
        string message;
        internal static void Open(PsdNode node, Action<PsdNode> apply)
        {
            var window = CreateInstance<ScrollLayoutWindow>();
            window.titleContent = new GUIContent("列表排列"); window.options = new PsdNode();
            ScrollLayoutOptions.Copy(node, window.options); window.apply = apply;
            window.minSize = new Vector2(360, 280); window.ShowUtility();
        }
        void OnGUI()
        {
            if (options == null) { Close(); return; }
            EditorGUILayout.HelpBox("默认保持 PSD。手动开启后，下次生成使用原生排列与尺寸自适应，会重新排列列表项；动态条目应提供 LayoutElement 或首选尺寸。", MessageType.Info);
            options.scrollLayout = (ScrollLayoutMode)EditorGUILayout.Popup("排列方式", (int)options.scrollLayout, new[] { "保持 PSD（不添加）", "纵向排列 / 自适应高度", "横向排列 / 自适应宽度" });
            if (options.scrollLayout != ScrollLayoutMode.KeepPsd)
            {
                options.scrollSpacing = Mathf.Max(0, EditorGUILayout.FloatField("条目间距", options.scrollSpacing));
                options.scrollPaddingLeft = Mathf.Max(0, EditorGUILayout.IntField("左边距", options.scrollPaddingLeft));
                options.scrollPaddingRight = Mathf.Max(0, EditorGUILayout.IntField("右边距", options.scrollPaddingRight));
                options.scrollPaddingTop = Mathf.Max(0, EditorGUILayout.IntField("上边距", options.scrollPaddingTop));
                options.scrollPaddingBottom = Mathf.Max(0, EditorGUILayout.IntField("下边距", options.scrollPaddingBottom));
            }
            EditorGUILayout.LabelField("配置在生成时生效；不会修改 PSD。恢复 PSD 排版后，旧排列组件按增量删除审核处理。", EditorStyles.wordWrappedMiniLabel);
            if (message != null) EditorGUILayout.HelpBox(message, MessageType.Warning);
            if (GUILayout.Button("保存配置（下次生成生效）", GUILayout.Height(28)))
                try { apply(options); Close(); } catch (Exception e) { message = e.Message; }
        }
    }
}
