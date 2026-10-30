using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class PsdEditorUI
    {
        static GUIStyle title, card;
        internal static GUIStyle Title => title ?? (title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, wordWrap = true });
        internal static GUIStyle Card => card ?? (card = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(6, 6, 5, 5) });
        internal static void Header(string heading, string description)
        {
            using (new EditorGUILayout.VerticalScope(Card))
            {
                EditorGUILayout.LabelField(heading, Title);
                if (!string.IsNullOrEmpty(description)) EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            }
        }
        internal static void Divider(string label)
        {
            EditorGUILayout.Space(6);
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, .14f) : new Color(0, 0, 0, .16f));
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
        internal static bool Action(string text, bool primary = false)
            => GUILayout.Button(text, GUILayout.Height(primary ? 30 : 24));
    }
}
