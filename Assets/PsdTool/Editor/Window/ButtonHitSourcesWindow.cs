using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class ButtonHitSourcesWindow : EditorWindow
    {
        PsdTree tree;
        int buttonId;
        HashSet<string> selected;
        Vector2 scroll;
        string search = "";
        internal static void Open(PsdTree tree, PsdNode node)
        {
            var window = CreateInstance<ButtonHitSourcesWindow>();
            window.tree = tree; window.buttonId = node.layerId;
            window.selected = new HashSet<string>(node.buttonHitKeys ?? new List<string>());
            window.titleContent = new GUIContent("按钮热区来源");
            window.minSize = new Vector2(460, 340); window.ShowUtility();
        }

        void OnGUI()
        {
            var button = tree != null ? tree.Find(buttonId) : null;
            if (button == null || button.templateId != "Button") { Close(); return; }
            PsdEditorUI.Text(button.goName, EditorStyles.boldLabel);
            PsdEditorUI.Notice("勾选图片、文字或组，以其正常可见视觉范围的并集计算热区。Button 仍挂根节点，图片和文字不会移动。全不选表示使用全部正常视觉。", MessageType.Info);
            search = EditorGUILayout.TextField("筛选节点", search);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var node in tree.nodes.Where(n => ButtonHitSources.BelongsTo(tree, n, button)))
            {
                var path = new List<string>();
                for (var n = node; n != null; n = tree.Find(n.parentId)) { path.Insert(0, n.goName); if (n == button) break; }
                string label = string.Join(" / ", path);
                if (!string.IsNullOrEmpty(search) && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                string key = ButtonHitSources.Key(node);
                bool normal = ButtonHitSources.NormalVisual(tree, node, button);
                using (new EditorGUI.DisabledScope(!normal && !selected.Contains(key)))
                {
                    bool value = PsdEditorUI.CopyToggleLeft(label + (normal ? "" : "（隐藏/状态/已删除，不参与）"), selected.Contains(key));
                    if (value) selected.Add(key); else selected.Remove(key);
                }
            }
            EditorGUILayout.EndScrollView();
            int missing = selected.Count(key => !tree.nodes.Any(n => ButtonHitSources.Key(n) == key && ButtonHitSources.NormalVisual(tree, n, button)));
            if (missing > 0) PsdEditorUI.Notice(missing + " 个来源已失效或不可见；将忽略，全部失效时退回默认范围。可恢复默认后重选。", MessageType.Warning);
            if (PsdEditorUI.CopyButton("恢复默认（全部正常视觉）")) selected.Clear();
            using (new EditorGUI.DisabledScope((button.locks & PropGroup.Rect) != 0))
                if (PsdEditorUI.CopyButton("保存热区来源（下次生成生效）", GUILayout.Height(28)))
                {
                    Undo.RegisterCompleteObjectUndo(tree, "设置按钮热区来源");
                    button.buttonHitKeys = selected.OrderBy(k => k, System.StringComparer.Ordinal).ToList();
                    button.forceUpdateGroups |= PropGroup.Rect;
                    EditorUtility.SetDirty(tree); AssetDatabase.SaveAssetIfDirty(tree);
                    if (AiMarker.Last?.tree == tree) { AiMarker.Last.generated = false; AiMarker.Last.generationChecked = false; }
                    Close();
                }
            if ((button.locks & PropGroup.Rect) != 0) PsdEditorUI.Notice("矩形已锁定，请先在节点详情解除矩形锁定。", MessageType.Info);
        }
    }
}
