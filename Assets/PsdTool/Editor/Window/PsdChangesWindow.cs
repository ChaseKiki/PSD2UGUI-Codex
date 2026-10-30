using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    public sealed class PsdChangesWindow : EditorWindow
    {
        string[] _lines;
        Vector2 _scroll;
        public static void Show(PrefabIncrementalImport.Report report)
        {
            var window = GetWindow<PsdChangesWindow>("PSD 变更预览");
            window.minSize = new Vector2(500, 320);
            window._lines = report.lines.ToArray();
            window.Show();
        }

        void OnGUI()
        {
            PsdEditorUI.Header("Prefab 变更预览", "核对变更后返回主窗口生成；此窗口不会保存 Prefab。");
            EditorGUILayout.HelpBox("这里只预览 Prefab 变更，未保存 Prefab。图片使用当前切图；首次导入未切图时，预览不包含最终图片和裁边变化。返回主窗口点击“3  生成 Prefab”会重新计算后应用。", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_lines == null || _lines.Length == 0) EditorGUILayout.LabelField("无 Prefab 变更。");
            else foreach (var line in _lines)
                using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card)) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
