using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class ButtonFeedbackWindow : EditorWindow
    {
        PsdTree tree;PsdToolSettings settings;
        List<ButtonFeedbackRow> rows;
        Action next;Func<bool> allowed;
        string context,error,search="";
        bool confirming;
        bool stale;double nextValidation;
        Vector2 scroll;
        readonly PsdNodePreview preview=new PsdNodePreview();
        internal static bool Request(PsdTree tree,PsdToolSettings settings,Action next,Func<bool> allowed=null)
        {
            var rows=ButtonFeedbackPlan.Build(tree);
            var window=Resources.FindObjectsOfTypeAll<ButtonFeedbackWindow>().FirstOrDefault();
            if(rows.Count==0){if(window!=null)window.Close();next();return false;}
            string context=ButtonFeedbackPlan.Context(tree,settings);
            if(window==null)window=CreateInstance<ButtonFeedbackWindow>();
            if(window.tree!=tree||window.context!=context)
            {window.tree=tree;window.settings=settings;window.rows=rows;window.context=context;window.error=null;window.scroll=Vector2.zero;window.search="";window.stale=false;window.nextValidation=0;}
            window.settings=settings;window.next=next;window.allowed=allowed;window.error=null;window.stale=false;window.nextValidation=0;
            window.titleContent=new GUIContent("按钮交互反馈确认");window.minSize=new Vector2(680,440);window.ShowUtility();window.Focus();
            return true;
        }
        internal void Confirm()
        {
            if(confirming)return;
            if(tree==null||ButtonFeedbackPlan.Context(tree,settings)!=context||allowed!=null&&!allowed())
                throw new InvalidOperationException("节点、识别建议或配置已变化；请关闭此面板，重新点击生成刷新列表。");
            confirming=true;
            try
            {
                ButtonFeedbackPlan.Apply(tree,rows);context=ButtonFeedbackPlan.Context(tree,settings);
                next();Close();
            }
            finally{confirming=false;}
        }
        internal void SetAll(bool colorTint)=>ButtonFeedbackPlan.SetAll(rows,colorTint);
        void OnGUI()
        {
            if(tree==null||rows==null){Close();return;}
            PsdEditorUI.Header("按钮交互反馈确认","新按钮默认 None。逐项选择 ColorTint，确认后才开始导出资源与生成；关闭或取消不会保存本面板未确认的选择。");
            PsdEditorUI.Notice("本面板仅设置 Button.Transition：None / ColorTint。ColorTint 仍使用最大正常可见 Image；不添加缩放脚本，也不修改项目自定义脚本或 Animator。",MessageType.Info);
            int protectedCount=rows.Count(r=>r.locked);
            PsdEditorUI.Text("按钮 "+rows.Count+" 项 · ColorTint "+rows.Count(r=>!r.locked&&r.colorTint)+" 项 · 锁定 / 手工组件保留 "+protectedCount+" 项",EditorStyles.wordWrappedLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(PsdEditorUI.Action("一键全动画"))SetAll(true);
                if(PsdEditorUI.Action("全禁动画"))SetAll(false);
            }
            search=EditorGUILayout.TextField("筛选名称 / 路径",search);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var row in rows.Where(r=>string.IsNullOrWhiteSpace(search)||r.path.IndexOf(search.Trim(),StringComparison.OrdinalIgnoreCase)>=0))
                using(PsdEditorUI.Section(null))using(new EditorGUILayout.HorizontalScope())
                {
                    var node=tree.Find(row.id);
                    using(new EditorGUILayout.VerticalScope(GUILayout.Width(92)))
                    {
                        var area=GUILayoutUtility.GetRect(92,72,GUILayout.ExpandWidth(false));
                        if(node!=null)preview.DrawCrop(area,tree,node);
                        using(new EditorGUI.DisabledScope(node==null))
                            if(GUI.Button(area,new GUIContent("","放大原稿并定位节点"),GUIStyle.none))PsdNodePreviewWindow.Open(tree,node.layerId,settings);
                    }
                    using(new EditorGUILayout.VerticalScope())
                    {
                        PsdEditorUI.Text(row.path,EditorStyles.wordWrappedLabel);
                        using(new EditorGUI.DisabledScope(row.locked))row.colorTint=PsdEditorUI.CopyToggleLeft("改为 ColorTint（不勾选为 None）",row.colorTint);
                        if(row.locked)PsdEditorUI.Text((row.managed?"组件锁定":"手工 / 非工具管理组件")+"，保留 "+(row.retained??"当前配置")+"；快捷按钮不覆盖此项。",EditorStyles.wordWrappedMiniLabel);
                        else if(!row.hasVisual)PsdEditorUI.Text("当前没有正常可见图片；ColorTint 可能没有可见的变色反馈。",EditorStyles.wordWrappedMiniLabel);
                    }
                }
            EditorGUILayout.EndScrollView();
            if(EditorApplication.timeSinceStartup>=nextValidation)
            {stale=ButtonFeedbackPlan.Context(tree,settings)!=context||allowed!=null&&!allowed();nextValidation=EditorApplication.timeSinceStartup+.35;}
            if(stale)PsdEditorUI.Notice("数据已变化，请重新点击生成刷新本面板。",MessageType.Warning);
            if(error!=null)PsdEditorUI.Notice(error,MessageType.Error);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(PsdEditorUI.Action("取消，不生成")){Close();GUIUtility.ExitGUI();}
                using(new EditorGUI.DisabledScope(stale||confirming))
                    if(PsdEditorUI.Action("确认并生成 / 更新 Prefab",true))
                    {
                        try{Confirm();}catch(ExitGUIException){throw;}catch(Exception e){error=e.Message;}
                        GUIUtility.ExitGUI();
                    }
            }
        }
        void OnDisable()=>preview.Dispose();
    }
}
