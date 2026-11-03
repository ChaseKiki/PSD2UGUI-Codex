using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    // Compatibility entry: all naming links now reveal the same workbench section.
    internal sealed class NodeNamingWindow : EditorWindow
    {
        internal static void Open(PsdTree tree,Action next=null,int focus=-1,PsdToolSettings settings=null)
            =>AiRecognitionWindow.OpenNaming(tree,next,focus,settings);
        void OnEnable(){EditorApplication.delayCall+=()=>{if(this!=null)Close();};}
    }
    internal sealed class OmissionCandidate
    {
        internal int id,score;
        internal string reason;
    }
    internal static class NodeOmissionScreen
    {
        internal static List<OmissionCandidate> Find(PsdTree tree)
        {
            var result=new List<OmissionCandidate>();
            var active=tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete).ToArray();
            foreach(var node in active)
            {
                if(PrefabGenerator.IsControl(node.templateId)||node.kind==LayerKind.Text||!string.IsNullOrEmpty(node.slot))continue;
                bool part=false;var seen=new HashSet<int>();
                for(var p=tree.Find(node.parentId);p!=null&&seen.Add(p.layerId);p=tree.Find(p.parentId))
                    if(PrefabGenerator.IsControl(p.templateId)&&p.templateId!="ScrollView"&&p.templateId!="ToggleGroup"){part=true;break;}
                if(part)continue;
                var bounds=AiRegions.Bounds(tree,node);
                if(bounds.width<8||bounds.height<8||bounds.width*bounds.height>tree.docSize.x*tree.docSize.y*.35f)continue;
                bool Owns(PsdNode child)
                {
                    var visited=new HashSet<int>();
                    for(var p=tree.Find(child.parentId);p!=null&&visited.Add(p.layerId);p=tree.Find(p.parentId))if(p.layerId==node.layerId)return true;
                    return false;
                }
                if(node.kind==LayerKind.Group)
                {
                    var members=active.Where(Owns).ToArray();
                    if(members.Any(n=>PrefabGenerator.IsControl(n.templateId)))continue;
                    if(members.Any(n=>n.kind==LayerKind.Image)&&members.Any(n=>n.kind==LayerKind.Text))
                        result.Add(new OmissionCandidate{id=node.layerId,score=3,reason="图片与文字组合，但尚未标成控件；可能是按钮，也可能只是信息展示。"});
                }
                else
                {
                    bool label=active.Any(n=>n.kind==LayerKind.Text&&n.parentId==node.parentId&&bounds.Contains(AiRegions.Bounds(tree,n).center));
                    if(label)result.Add(new OmissionCandidate{id=node.layerId,score=2,reason="同组文字落在图片范围内，可能遗漏了按钮组合。"});
                    else if(node.visible&&bounds.width*bounds.height<tree.docSize.x*tree.docSize.y*.025f)
                        result.Add(new OmissionCandidate{id=node.layerId,score=1,reason="独立小图，可能是图标按钮；仅凭图片不能确认交互。"});
                }
            }
            return result.OrderByDescending(r=>r.score).ThenBy(r=>r.id).ToList();
        }
    }
    internal sealed class NodeNamingPanel : IDisposable
    {
        PsdTree tree;
        List<NodeNaming.Row> rows=new List<NodeNaming.Row>();
        readonly PsdNodePreview preview=new PsdNodePreview();
        readonly HashSet<int> reviewIds=new HashSet<int>();
        List<OmissionCandidate> candidates=new List<OmissionCandidate>();
        string context,search="",error;
        int mode,page;
        internal int PendingCount=>rows.Count(r=>!r.reviewed);
        internal void MarkSubmitted(IEnumerable<int> ids){foreach(int id in ids)reviewIds.Remove(id);}
        internal void Focus(int id){search=tree?.Find(id)?.goName??"";mode=2;}
        internal void Sync(PsdTree value,bool force=false)
        {
            if(value==null)return;
            string next=value.psdStamp+"|"+value.docSize+"|"+string.Join(";",value.nodes.Select(n=>n.layerId+":"+n.parentId+":"+n.kind+":"+n.templateId+":"+n.slot+":"+n.goName+":"+n.locks+":"+n.generateBind+":"+n.status+":"+n.englishNameHint+":"+n.manualName+":"+n.nameReviewed+":"+n.nameReviewStructure+":"+n.psdRect+":"+n.visible));
            if(!force&&tree==value&&next==context)return;
            bool same=tree==value;
            var old=rows.ToDictionary(r=>r.id);
            tree=value;rows=NodeNaming.Preview(tree);context=next;
            if(same)
            {
                foreach(var r in rows)
                    if(old.TryGetValue(r.id,out var draft)&&draft.structure==r.structure&&draft.before==r.before&&!draft.reviewed)
                    {r.after=draft.after;r.manual=draft.manual;r.program=draft.program;r.selected=draft.selected;}
            }
            else {reviewIds.Clear();search="";error=null;}
            candidates=NodeOmissionScreen.Find(tree);
            reviewIds.RemoveWhere(id=>tree.Find(id)==null||tree.Find(id).status==NodeStatus.PendingDelete);
        }
        internal void ApplyPending()
        {
            Sync(tree);
            var pending=rows.Where(r=>!r.reviewed||r.after!=r.before).ToList();
            foreach(var r in pending)r.selected=true;
            if(pending.Count>0)NodeNaming.Apply(tree,pending);
            Sync(tree,true);error=null;
        }
        internal void Draw(PsdTree source,Action<int[]> submit,Action generate,PsdToolSettings settings=null)
        {
            Sync(source);if(tree==null)return;
            PsdEditorUI.Notice("应用名称只更新待生成数据，不会立即修改 Prefab。结构、插槽或父级变化后，已确认节点会重新待确认；人工名称保留供复核。",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                search=EditorGUILayout.TextField("名称 / 路径 / ID",search);
                mode=EditorGUILayout.Popup(mode,new[]{"待命名","疑似漏识别","全部节点"},GUILayout.Width(120));
                if(EditorGUI.EndChangeCheck())page=0;
            }
            PsdEditorUI.Text("待确认命名 "+PendingCount+" 项 · 疑似遗漏 "+candidates.Count+" 项 · 已勾选 AI 复核 "+reviewIds.Count+" 项",EditorStyles.wordWrappedMiniLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(PsdEditorUI.CopyButton("勾选优先候选"))foreach(var c in candidates.Where(c=>c.score>=2&&!WasReviewed(c.id)))reviewIds.Add(c.id);
                if(PsdEditorUI.CopyButton("清空复核勾选"))reviewIds.Clear();
                using(new EditorGUI.DisabledScope(reviewIds.Count==0))
                    if(PsdEditorUI.Action("批量 AI 复核（"+Math.Min(reviewIds.Count,100)+" 项）"))
                    {submit(reviewIds.OrderBy(id=>id).Take(100).ToArray());GUIUtility.ExitGUI();}
            }
            if(reviewIds.Count>100)PsdEditorUI.Notice("每批最多 100 项，本次提交前 100 项，其余保留勾选。",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(PsdEditorUI.Action("应用全部待确认名称"))Try(ApplyPending);
                if(PsdEditorUI.Action("应用待确认名称并生成 / 更新 Prefab",true))generate();
            }
            if(!string.IsNullOrEmpty(error))PsdEditorUI.Notice(error,MessageType.Warning);
            var visible=rows.Where(Matches).ToArray();
            const int perPage=60;page=Mathf.Clamp(page,0,Math.Max(0,(visible.Length-1)/perPage));
            using(new EditorGUILayout.HorizontalScope())
            {
                PsdEditorUI.Text("匹配 "+visible.Length+" 项 · 第 "+(page+1)+" / "+Math.Max(1,(visible.Length+perPage-1)/perPage)+" 页",EditorStyles.miniLabel);
                using(new EditorGUI.DisabledScope(page==0))if(PsdEditorUI.CopyButton("上一页",GUILayout.Width(62)))page--;
                using(new EditorGUI.DisabledScope((page+1)*perPage>=visible.Length))if(PsdEditorUI.CopyButton("下一页",GUILayout.Width(62)))page++;
            }
            foreach(var row in visible.Skip(page*perPage).Take(perPage).ToArray())
            {
                var node=tree.Find(row.id);var candidate=candidates.FirstOrDefault(c=>c.id==row.id);
                using(PsdEditorUI.Section(null))using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUILayout.VerticalScope(GUILayout.Width(84)))
                    {
                        var rect=GUILayoutUtility.GetRect(84,72,GUILayout.ExpandWidth(false));preview.DrawCrop(rect,tree,node);
                        if(GUI.Button(rect,new GUIContent("","放大原稿并定位节点"),GUIStyle.none))PsdNodePreviewWindow.Open(tree,row.id,settings);
                        bool marked=PsdEditorUI.CopyToggleLeft("加入 AI 复核",reviewIds.Contains(row.id));
                        if(marked)reviewIds.Add(row.id);else reviewIds.Remove(row.id);
                    }
                    using(new EditorGUILayout.VerticalScope())
                    {
                        PsdEditorUI.Text(row.path,EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text((string.IsNullOrEmpty(node.templateId)?"Image / Text":node.templateId)+" · "+(row.reviewed?"名称已应用":"命名待确认")+(row.locked?" · 名称锁定":""),EditorStyles.wordWrappedLabel);
                        PsdEditorUI.Text("当前："+row.before,EditorStyles.wordWrappedMiniLabel);
                        if(candidate!=null)PsdEditorUI.Text(candidate.reason,EditorStyles.wordWrappedMiniLabel);
                        if(WasReviewed(row.id))PsdEditorUI.Text("已有识别记录或部件方案；仍可勾选重新复核。",EditorStyles.wordWrappedMiniLabel);
                        using(new EditorGUI.DisabledScope(row.locked))
                        {
                            EditorGUI.BeginChangeCheck();
                            var next=EditorGUILayout.TextField(row.after);
                            if(EditorGUI.EndChangeCheck()){row.after=next;row.manual=true;row.reviewed=false;}
                        }
                    }
                    using(new EditorGUILayout.VerticalScope(GUILayout.Width(114)))
                    {
                        if(PsdEditorUI.Action(row.reviewed?"重新应用名称":"应用名称"))Try(()=>{row.selected=true;NodeNaming.Apply(tree,new List<NodeNaming.Row>{row});Sync(tree,true);});
                        using(new EditorGUI.DisabledScope(row.locked||(row.after??"").StartsWith("m_",StringComparison.Ordinal)))
                            if(PsdEditorUI.Action("m_节点"))Try(()=>{row.after=NodeNaming.ProgramName(tree,node,row.after);row.program=true;row.manual=true;row.reviewed=false;});
                        if(PsdEditorUI.Action("定位节点"))PsdToolWindow.FocusNode(tree,settings??PsdToolSettings.TryGet(),row.id);
                        if(PsdEditorUI.Action("AI 复核此节点")){submit(new[]{row.id});GUIUtility.ExitGUI();}
                    }
                }
            }
        }
        bool WasReviewed(int id)
        {
            var review=AiMarker.Last;
            return review?.tree==tree&&(review.regions.Any(r=>r.rootId==id)||review.rows.Any(r=>!review.IsIgnored(r)&&r.proposal?.nodes?.Any(e=>e.layerId==id&&(e.setTemplate&&PrefabGenerator.IsControl(e.templateId)||e.setSlot&&!string.IsNullOrEmpty(e.slot)))==true));
        }
        bool Matches(NodeNaming.Row row)
        {
            bool hit=string.IsNullOrWhiteSpace(search)||(row.path+" "+row.after+" "+tree.Find(row.id)?.psdName).IndexOf(search.Trim(),StringComparison.OrdinalIgnoreCase)>=0||row.id.ToString()==search.Trim();
            return hit&&(!string.IsNullOrWhiteSpace(search)||mode==2||mode==0&&!row.reviewed||mode==1&&candidates.Any(c=>c.id==row.id));
        }
        void Try(Action action){try{action();error=null;}catch(ExitGUIException){throw;}catch(Exception e){error=e.Message;}}
        public void Dispose()=>preview.Dispose();
    }
}
