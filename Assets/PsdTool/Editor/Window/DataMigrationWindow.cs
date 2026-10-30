using System;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class DataMigrationWindow : EditorWindow
    {
        PsdToolSettings settings;
        string editorRoot = PsdToolSettings.DefaultDataRoot, materials = PsdToolSettings.DefaultRuntimeMaterials, error;
        DataMigrationPlan plan;
        Vector2 scroll;
        internal static void Open(PsdToolSettings settings)
        { var window = GetWindow<DataMigrationWindow>("数据隔离迁移"); window.settings = settings; window.plan = null; window.minSize = new Vector2(540, 380); window.Show(); }
        void OnGUI()
        {
            PsdEditorUI.Header("数据隔离迁移", "1 设置目标目录   →   2 预览移动清单   →   3 确认迁移");
            EditorGUILayout.HelpBox("移动保留 GUID；运行材质与编辑数据分开保存。目标冲突会停止。", MessageType.None);
            EditorGUI.BeginChangeCheck();
            editorRoot = EditorGUILayout.TextField("持久编辑数据", editorRoot);
            materials = EditorGUILayout.TextField("运行文字材质", materials);
            if (EditorGUI.EndChangeCheck()) plan = null;
            if (GUILayout.Button("预览迁移清单"))
                try { plan = DataLayoutMigration.Preview(settings, editorRoot, materials); error = null; } catch (Exception e) { plan = null; error = e.Message; }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (plan == null) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var move in plan.moves)
                using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card))
                    EditorGUILayout.LabelField(move.from + "\n→ " + move.to, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"移动 {plan.moves.Count} 项；确认后更新配置目录。");
            if (GUILayout.Button("确认清单并迁移（保留 GUID）", GUILayout.Height(30)))
                try { DataLayoutMigration.Apply(settings, plan); plan = null; error = null; ShowNotification(new GUIContent("迁移完成")); } catch (Exception e) { plan = null; error = e.Message; }
        }
    }
}
