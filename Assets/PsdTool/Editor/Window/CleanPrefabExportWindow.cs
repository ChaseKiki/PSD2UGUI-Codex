using System;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class CleanPrefabExportWindow : EditorWindow
    {
        GameObject source;
        bool removeOptional = true;
        string message;
        internal static void Open(GameObject source)
        {
            var window = CreateInstance<CleanPrefabExportWindow>(); window.source = source;
            window.titleContent = new GUIContent("导出干净副本"); window.minSize = new Vector2(400, 240); window.ShowUtility();
        }
        void OnGUI()
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("编辑用原 Prefab", source, typeof(GameObject), false);
            EditorGUILayout.HelpBox("另存一个脱离 PSD 增量更新的副本。移除来源标识，保留控件及必要的点击/视觉辅助组件。原 Prefab 与引用不变，后续改稿仍使用原稿。", MessageType.Info);
            removeOptional = EditorGUILayout.ToggleLeft("同时移除可选的按压缩放 / 置灰组件", removeOptional);
            EditorGUILayout.LabelField("被事件或其他组件引用的辅助脚本，以及嵌套 Prefab 内的组件，会保留并给出统计。副本仍引用原图片、字体和材质，不会复制资源。", EditorStyles.wordWrappedMiniLabel);
            if (message != null) EditorGUILayout.HelpBox(message, MessageType.Info);
            using (new EditorGUI.DisabledScope(source == null))
                if (GUILayout.Button("选择另存位置并导出", GUILayout.Height(28)))
                {
                    string path = EditorUtility.SaveFilePanelInProject("导出干净副本", source.name + "_Clean", "prefab", "选择新文件；不覆盖编辑用原 Prefab。");
                    if (string.IsNullOrEmpty(path)) return;
                    try { var result = CleanPrefabExporter.Export(source, path, removeOptional, out message); EditorGUIUtility.PingObject(result); }
                    catch (Exception e) { message = e.Message; }
                }
        }
    }
}
