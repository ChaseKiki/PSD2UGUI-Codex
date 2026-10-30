using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class ResourceManagerWindow : EditorWindow
    {
        PsdToolSettings settings;
        List<ResourceUsage> rows;
        Vector2 scroll;
        string error;
        bool onlyUnused, maintenance;

        [MenuItem("Tools/PSD 工具/资源管理")]
        static void OpenMenu() => Open(PsdToolSettings.Load());

        public static void Open(PsdToolSettings settings)
        {
            var window = GetWindow<ResourceManagerWindow>("PSD 资源管理");
            window.settings = settings; window.rows = null; window.minSize = new Vector2(520, 360); window.Show();
        }
        void Run(Action action)
        {
            try { action(); error = null; }
            catch (Exception e) { error = e.Message; Debug.LogException(e); }
        }
        void OnGUI()
        {
            PsdEditorUI.Header("资源管理", "扫描引用，确认用途后回收；回收保留 GUID，可恢复原位置。");
            settings = (PsdToolSettings)EditorGUILayout.ObjectField("配置", settings, typeof(PsdToolSettings), false);
            if (settings == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (PsdEditorUI.Action("扫描引用 / 刷新", true)) Run(() => rows = ResourceMaintenance.Scan(settings));
                onlyUnused = GUILayout.Toggle(onlyUnused, "只看待回收 / 已回收", EditorStyles.miniButton, GUILayout.Width(170));
            }
            maintenance = EditorGUILayout.Foldout(maintenance, "维护 / 压缩与数据目录", true);
            if (maintenance)
            {
                if (PsdEditorUI.Action("数据隔离迁移（先预览清单）")) DataMigrationWindow.Open(settings);
                if (PsdEditorUI.Action("定位项目配置")) Selection.activeObject = settings;
                if (PsdEditorUI.Action("将压缩配置应用到已有工具资源"))
                    if (EditorUtility.DisplayDialog("应用已有资源配置", "将覆盖登记贴图和图集的压缩、平台、尺寸配置。指定引用的外部资源不修改。", "应用", "取消"))
                        Run(() => ResourceMaintenance.ApplyProfiles(settings));
                EditorGUILayout.HelpBox("生成不会自动删图。字符串地址加载无法完整追踪，回收前请确认用途；恢复后重新生成可更新图集。", MessageType.None);
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (rows == null) return;
            EditorGUILayout.LabelField($"登记 {rows.Count} 项，可回收 {rows.Count(r => r.CanRecycle)} 项");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var row in rows)
            {
                bool recycled = !string.IsNullOrEmpty(row.record.restorePath);
                if (onlyUnused && !row.CanRecycle && !recycled) continue;
                using (new EditorGUILayout.HorizontalScope(PsdEditorUI.Card))
                {
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(row.path) ? "丢失：" + row.record.guid : row.path, GUILayout.Height(18));
                    EditorGUILayout.LabelField(recycled ? "已回收，原路径：" + row.record.restorePath : row.references.Count == 0 ? "未发现引用（回收前将再次检查）" : "引用：" + row.references.Count);
                    foreach (string reference in row.references.Distinct().Take(6)) EditorGUILayout.LabelField(reference, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(110)))
                    {
                        if (PsdEditorUI.Action("定位")) Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(row.path);
                        if (recycled && PsdEditorUI.Action("恢复原位置"))
                        { Run(() => ResourceMaintenance.Restore(settings, row.record.guid)); rows = null; break; }
                        using (new EditorGUI.DisabledScope(!row.CanRecycle))
                            if (!recycled && PsdEditorUI.Action("移入回收区"))
                            { Run(() => ResourceMaintenance.Recycle(settings, row.record.guid)); rows = null; break; }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
