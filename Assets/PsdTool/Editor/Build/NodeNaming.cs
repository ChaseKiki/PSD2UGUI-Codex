using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class NodeNaming
    {
        internal sealed class Row { public int id; public string before, after, path, structure; public bool selected, locked, manual, program, reviewed; }
        internal static Dictionary<int,string> ReviewStamps(PsdTree tree)
        {
            var live=tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete).ToDictionary(n=>n.layerId);
            var children=live.Values.GroupBy(n=>n.parentId).ToDictionary(g=>g.Key,g=>g.OrderBy(n=>n.layerId).ToArray());
            var sub=new Dictionary<int,string>();var ancestors=new Dictionary<int,string>();var visiting=new HashSet<int>();
            string Part(PsdNode n)=>n.layerId+":"+n.stableKey+":"+n.templateId+":"+n.slot+":"+n.kind;
            string Sub(PsdNode n)
            {
                if(sub.TryGetValue(n.layerId,out var cached))return cached;
                if(!visiting.Add(n.layerId))throw new InvalidOperationException("节点结构存在循环。");
                string value=Part(n)+"["+ (children.TryGetValue(n.layerId,out var kids)?string.Join(",",kids.Select(Sub)):"")+"]";
                visiting.Remove(n.layerId);return sub[n.layerId]=AiProtocol.Hash(value);
            }
            string Anc(PsdNode n)
            {
                if(ancestors.TryGetValue(n.layerId,out var cached))return cached;
                if(!visiting.Add(n.layerId))throw new InvalidOperationException("节点结构存在循环。");
                string value=n.parentId+":"+(live.TryGetValue(n.parentId,out var p)?Part(p)+"/"+Anc(p):"root");
                visiting.Remove(n.layerId);return ancestors[n.layerId]=AiProtocol.Hash(value);
            }
            return live.Values.ToDictionary(n=>n.layerId,n=>AiProtocol.Hash(Sub(n)+"/"+Anc(n)));
        }
        internal static bool Reviewed(PsdNode node,string stamp)=>node.nameReviewed&&!string.IsNullOrEmpty(node.nameReviewStructure)&&node.nameReviewStructure==stamp;
        internal static bool ValidToken(string name) => !string.IsNullOrEmpty(name) && name.Length <= 96 && Regex.IsMatch(name, @"^(m_)?[A-Za-z][A-Za-z0-9_]*$");
        internal static bool ProgramNode(PsdNode n) => PrefabGenerator.IsControl(n.templateId) || n.generateBind;
        internal static string Role(PsdNode n)
        {
            switch(n.slot)
            {
                case "bg":return "Background";case "label":return "Label";case "on":return "SelectedState";case "labelOn":return "SelectedLabel";
                case "pressed":return "PressedState";case "disabled":return "DisabledState";case "hit":return "HitArea";case "rect":return "Bounds";
                case "vbarbg":return "VerticalScrollbarBackground";case "vbar":return "VerticalScrollbarHandle";
                case "hbarbg":return "HorizontalScrollbarBackground";case "hbar":return "HorizontalScrollbarHandle";
                case "template":return "DropdownTemplate";case "text":return "InputText";
                default:return Pascal(n.slot);
            }
        }
        static string Pascal(string value) => string.Concat(Regex.Matches(value ?? "", "[A-Za-z0-9]+").Cast<Match>().Select(m => char.ToUpperInvariant(m.Value[0]) + m.Value.Substring(1)));
        static string Base(string value) => (value ?? "").StartsWith("m_", StringComparison.Ordinal) ? value.Substring(2) : value ?? "";
        static bool UnderControl(PsdTree tree, PsdNode node)
        {
            var seen = new HashSet<int>();
            for (var p = tree.Find(node.parentId); p != null && seen.Add(p.layerId); p = tree.Find(p.parentId)) if (PrefabGenerator.IsControl(p.templateId)) return true;
            return false;
        }
        internal static string Suggest(PsdTree tree, PsdNode node)
        {
            string type = PrefabGenerator.IsControl(node.templateId) ? node.templateId : node.kind == LayerKind.Group ? "Group" : node.kind == LayerKind.Text ? "Text" : "Image";
            string name = ValidToken(node.englishNameHint) ? Base(node.englishNameHint) : ValidToken(node.goName) ? Base(node.goName) : "";
            name = Pascal(name);
            if (PrefabGenerator.IsControl(node.templateId) && name.StartsWith(type,StringComparison.OrdinalIgnoreCase) && name.Length>type.Length && char.IsUpper(name[type.Length]))
                name=name.Substring(type.Length);
            if(name.Length>48)name=name.Substring(0,48);
            if (!PrefabGenerator.IsControl(node.templateId) && UnderControl(tree, node))
            {
                string role = Role(node); if (string.IsNullOrEmpty(role)) role = "Decoration";
                if(string.Equals(name,Pascal(node.slot),StringComparison.OrdinalIgnoreCase))name="";
                if (name.IndexOf(role, StringComparison.OrdinalIgnoreCase) < 0) name += role;
            }
            if (string.IsNullOrEmpty(name)) name = type;
            if (!name.EndsWith(type, StringComparison.OrdinalIgnoreCase)) name += type;
            return (ProgramNode(node) ? "m_" : "") + name;
        }
        internal static List<Row> Preview(PsdTree tree)
        {
            var nodes = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete).ToList();
            var stamps=ReviewStamps(tree);
            var rows = nodes.Select(n => new Row { id = n.layerId, before = n.goName, structure=stamps[n.layerId],reviewed=Reviewed(n,stamps[n.layerId]),
                after = n.manualName || (n.locks & PropGroup.Name) != 0 ? n.goName : Reviewed(n,stamps[n.layerId]) && NameError(tree,n,n.goName)==null ? n.goName : Suggest(tree,n),
                path = Path(tree,n), locked = (n.locks & PropGroup.Name) != 0, manual = n.manualName, selected = !Reviewed(n,stamps[n.layerId]) && (n.locks & PropGroup.Name)==0 }).ToList();
            foreach (var siblings in rows.GroupBy(r => tree.Find(r.id).parentId))
            {
                var used = new HashSet<string>(siblings.Where(r=>!r.selected||r.manual).Select(r=>r.before), StringComparer.OrdinalIgnoreCase);
                foreach (var row in siblings.Where(r=>r.selected&&!r.manual).OrderBy(r=>r.id))
                {
                    string seed=row.after; int i=2;
                    while (!used.Add(row.after)) row.after=seed+"_"+i++;
                }
            }
            return rows;
        }
        internal static string Path(PsdTree tree,PsdNode node)
        {
            var names=new List<string>();var seen=new HashSet<int>();
            for(var n=node;n!=null&&seen.Add(n.layerId);n=tree.Find(n.parentId))names.Insert(0,n.goName);
            return string.Join("/",names);
        }
        internal static string NameError(PsdTree tree,PsdNode node,string name)
        {
            if(!ValidToken(name))return "名称须使用英文标识符：字母开头，仅含 ASCII 字母、数字、下划线，最多 96 字符。";
            if(ProgramNode(node)&&!name.StartsWith("m_",StringComparison.Ordinal))return "控件或程序访问节点必须以 m_ 开头。";
            if(!PrefabGenerator.IsControl(node.templateId)&&UnderControl(tree,node))
            { string role=Role(node);if(string.IsNullOrEmpty(role))role="Decoration";if(name.IndexOf(role,StringComparison.OrdinalIgnoreCase)<0)return "该部件名称须体现角色："+role; }
            return null;
        }
        internal static string Validate(PsdTree tree,List<Row> rows,bool allSiblings=true)
        {
            foreach(var row in rows.Where(r=>r.selected))
            {
                var node=tree.Find(row.id); if(node==null||node.status==NodeStatus.PendingDelete)return "来源节点已变化，请重新打开命名预览。";
                if(node.goName!=row.before)return "节点名称已在其它窗口修改，请重新打开预览。";
                if(row.program&&!row.after.StartsWith("m_",StringComparison.Ordinal))return "程序访问节点必须以 m_ 开头："+row.after;
                if((node.locks&PropGroup.Name)!=0 && row.after!=row.before)return "名称已锁定："+row.before;
                string error=NameError(tree,node,row.after);if(error!=null)return row.before+"："+error;
            }
            foreach(var siblings in tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete).GroupBy(n=>n.parentId))
            {
                var used=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                foreach(var node in siblings)
                {
                    var row=rows.Find(r=>r.id==node.layerId);string name=row?.selected==true?row.after:node.goName;
                    if(used.TryGetValue(name??"",out int previous) && (allSiblings||row?.selected==true||rows.Any(r=>r.id==previous&&r.selected)))return "同层名称重复（不区分大小写）："+name;
                    used[name??""]=node.layerId;
                }
            }
            return null;
        }
        internal static void Apply(PsdTree tree,List<Row> rows,bool manual=false)
        {
            string error=Validate(tree,rows,false);if(error!=null)throw new InvalidOperationException(error);
            var stamps=ReviewStamps(tree);
            Undo.RegisterCompleteObjectUndo(tree,"确认 Prefab 节点命名");
            foreach(var row in rows.Where(r=>r.selected))
            { var n=tree.Find(row.id); if(n.goName!=row.after)n.forceUpdateGroups|=PropGroup.Name; n.goName=row.after;n.nameReviewed=true;n.nameReviewStructure=stamps[n.layerId];n.manualName|=manual||row.manual;n.generateBind|=row.program||row.after.StartsWith("m_",StringComparison.Ordinal); }
            EditorUtility.SetDirty(tree);AssetDatabase.SaveAssetIfDirty(tree);
            if(AiMarker.Last?.tree==tree)AiMarker.Last.AcceptNamingEdits();
        }
        internal static void Rename(PsdTree tree,PsdNode node,string name)
        { Apply(tree,new List<Row>{new Row{id=node.layerId,before=node.goName,after=name,selected=true}},true); }

        internal static string ProgramName(PsdTree tree,PsdNode node,string current=null)
        {
            var options=JsonUtility.FromJson<PsdNode>(JsonUtility.ToJson(node));options.generateBind=true;
            options.goName=current??node.goName;
            string name="m_"+Base(options.goName);
            if(NameError(tree,options,name)!=null)
            {
                if(ValidToken(options.goName))options.englishNameHint=null;
                name=Suggest(tree,options);
            }
            string seed=name;int suffix=2;
            while(tree.nodes.Any(n=>n!=node&&n.parentId==node.parentId&&n.status!=NodeStatus.PendingDelete&&string.Equals(n.goName,name,StringComparison.OrdinalIgnoreCase)))name=seed+"_"+suffix++;
            if(!ValidToken(name))throw new InvalidOperationException("名称过长，请先缩短节点名称。");
            return name;
        }
        internal static void MarkProgramNode(PsdTree tree,PsdNode node)
        {
            if(node==null||node.status==NodeStatus.PendingDelete)throw new InvalidOperationException("请选择有效节点。");
            if((node.locks&PropGroup.Name)!=0)throw new InvalidOperationException("名称已锁定，请先解除名称锁。");
            string name=ProgramName(tree,node);
            Undo.RegisterCompleteObjectUndo(tree,"设为 m_ 程序访问节点");
            node.generateBind=true;node.goName=name;node.manualName=true;node.nameReviewed=true;node.forceUpdateGroups|=PropGroup.Name;
            node.nameReviewStructure=ReviewStamps(tree)[node.layerId];
            EditorUtility.SetDirty(tree);AssetDatabase.SaveAssetIfDirty(tree);
            if(AiMarker.Last?.tree==tree)AiMarker.Last.AcceptNamingEdits();
        }

        internal static void NameHelpers(Transform parent)
        {
            var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(Transform child in parent)
            {var id=child.GetComponent<PsdGeneratedNode>();if(id==null||!id.key.Contains("/"))used.Add(child.name);}
            foreach(Transform child in parent)
            {
                var id=child.GetComponent<PsdGeneratedNode>();
                if(id!=null&&id.key.Contains("/"))
                {
                    string seed=child.name=="Source Visual"?"BackgroundImage":child.name=="FX Visual"?"EffectsImage":child.name=="bg"?"BackgroundImage":Pascal(child.name);
                    if(string.IsNullOrEmpty(seed))seed="GeneratedGroup";
                    string name=seed;int suffix=2;while(!used.Add(name))name=seed+"_"+suffix++;
                    child.name=name;
                }
                NameHelpers(child);
            }
        }
    }
}
