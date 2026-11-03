using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class PsdEditorUI
    {
        internal static bool CopyToggleLeft(string text,bool value,params GUILayoutOption[] options)=>CopyToggleLeft(new GUIContent(text),value,EditorStyles.label,options);
        internal static bool CopyToggleLeft(GUIContent text,bool value,params GUILayoutOption[] options)=>CopyToggleLeft(text,value,EditorStyles.label,options);
        internal static bool CopyToggleLeft(string text,bool value,GUIStyle style,params GUILayoutOption[] options)=>CopyToggleLeft(new GUIContent(text),value,style,options);
        internal static bool CopyToggleLeft(GUIContent text,bool value,GUIStyle style,params GUILayoutOption[] options)
        {bool result=EditorGUILayout.ToggleLeft(text,value,style,options);CopyCaption(text.text);return result;}
        internal static bool CopyFoldout(bool value,string text,bool toggleOnLabelClick=true)
        {bool result=EditorGUILayout.Foldout(value,text,toggleOnLabelClick);CopyCaption(text);return result;}
        static void CopyCaption(string value)
        {
            if(Event.current.type!=EventType.ContextClick||!GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))return;
            var menu=new GenericMenu();menu.AddItem(new GUIContent("复制文字"),false,()=>EditorGUIUtility.systemCopyBuffer=value??"");menu.ShowAsContext();Event.current.Use();
        }
        internal static bool CopyButton(string text,params GUILayoutOption[] options)=>CopyButton(new GUIContent(text),GUI.skin.button,options);
        internal static bool CopyButton(GUIContent text,params GUILayoutOption[] options)=>CopyButton(text,GUI.skin.button,options);
        internal static bool CopyButton(string text,GUIStyle style,params GUILayoutOption[] options)=>CopyButton(new GUIContent(text),style,options);
        internal static bool CopyButton(string text,string style,params GUILayoutOption[] options)=>CopyButton(new GUIContent(text),GUI.skin.GetStyle(style),options);
        internal static bool CopyButton(GUIContent text,GUIStyle style,params GUILayoutOption[] options)
        {
            bool clicked=GUILayout.Button(text,style,options);
            if(Event.current.type==EventType.ContextClick&&GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                string value=System.Text.RegularExpressions.Regex.Replace(text.text??"","</?(?:color|b|i|size)(?:=[^>]*)?>","");
                var menu=new GenericMenu();menu.AddItem(new GUIContent("复制文字"),false,()=>EditorGUIUtility.systemCopyBuffer=value);
                menu.ShowAsContext();Event.current.Use();return false;
            }
            return clicked;
        }
        // Read-only text remains selectable (Ctrl+C), including paths and errors.
        internal static void Text(string value, params GUILayoutOption[] options)=>Text(new GUIContent(value??""),EditorStyles.label,options);
        internal static void Text(string value,GUIStyle style,params GUILayoutOption[] options)=>Text(new GUIContent(value??""),style,options);
        internal static void Text(GUIContent value,params GUILayoutOption[] options)=>Text(value,EditorStyles.label,options);
        internal static void Text(string label,string value,params GUILayoutOption[] options)=>Text(label+"  "+value,EditorStyles.label,options);
        internal static void Text(string label,string value,GUIStyle style,params GUILayoutOption[] options)=>Text(label+"  "+value,style,options);
        internal static void Text(GUIContent value,GUIStyle style,params GUILayoutOption[] options)
        {
            style=style??EditorStyles.label;
            var layout=new GUILayoutOption[options.Length+2];layout[0]=GUILayout.MinWidth(0);layout[1]=GUILayout.ExpandWidth(true);
            System.Array.Copy(options,0,layout,2,options.Length);
            var rect=GUILayoutUtility.GetRect(value,style,layout);
            bool enabled=GUI.enabled;GUI.enabled=true;
            try{EditorGUI.SelectableLabel(rect,value?.text??"",style);}
            finally{GUI.enabled=enabled;}
            if(!string.IsNullOrEmpty(value?.tooltip))GUI.Label(rect,new GUIContent("",value.tooltip),GUIStyle.none);
        }
        internal static void Notice(string message,MessageType type,bool wide=true)
        {
            using(new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if(type!=MessageType.None)
                {
                    string icon=type==MessageType.Error?"console.erroricon":type==MessageType.Warning?"console.warnicon":"console.infoicon";
                    GUILayout.Label(EditorGUIUtility.IconContent(icon),GUILayout.Width(28),GUILayout.Height(28));
                }
                Text(message,EditorStyles.wordWrappedLabel,GUILayout.ExpandWidth(true));
            }
        }
        static GUIStyle title, card;
        internal static GUIStyle Title => title ?? (title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, wordWrap = true });
        internal static GUIStyle Card => card ?? (card = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(6, 6, 5, 5) });
        internal static EditorGUILayout.VerticalScope Section(string heading)
        {
            var scope=new EditorGUILayout.VerticalScope(Card);
            if(!string.IsNullOrEmpty(heading))Text(heading,EditorStyles.boldLabel);
            return scope;
        }
        internal static void Header(string heading, string description)
        {
            using (new EditorGUILayout.VerticalScope(Card))
            {
                Text(heading, Title);
                if (!string.IsNullOrEmpty(description)) Text(description, EditorStyles.wordWrappedMiniLabel);
            }
        }
        internal static void Divider(string label)
        {
            EditorGUILayout.Space(6);
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, .14f) : new Color(0, 0, 0, .16f));
            EditorGUILayout.Space(6);
            Text(label, EditorStyles.boldLabel);
        }
        internal static bool Action(string text, bool primary = false)
            => CopyButton(text, GUILayout.Height(primary ? 30 : 24));
    }
}
