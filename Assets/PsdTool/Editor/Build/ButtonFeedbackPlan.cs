using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    internal sealed class ButtonFeedbackRow
    {
        internal int id;
        internal string key,path,retained;
        internal bool managed,locked,colorTint,hasVisual;
    }
    internal static class ButtonFeedbackPlan
    {
        internal static List<ButtonFeedbackRow> Build(PsdTree tree)
        {
            var rows=tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete&&n.templateId=="Button").Select(n=>new ButtonFeedbackRow
            {
                id=n.layerId,key=ResourceCatalog.Key(n),path=NodeNaming.Path(tree,n),managed=true,
                locked=(n.locks&PropGroup.Component)!=0,colorTint=n.buttonColorTint,hasVisual=HasVisual(tree,n)
            }).ToList();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(tree.prefabGuid??""));
            string treeGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tree));
            if(prefab!=null)
                foreach(var button in prefab.GetComponentsInChildren<Button>(true))
                {
                    var marker=button.GetComponent<PsdGeneratedNode>();
                    var row=rows.FirstOrDefault(r=>marker!=null&&marker.sourceGuid==treeGuid&&r.key==marker.key);
                    if(row!=null)
                    {
                        if(row.locked){row.retained=button.transition.ToString();row.colorTint=button.transition==Selectable.Transition.ColorTint;}
                        continue;
                    }
                    PsdNodeSelection.Resolve(button.gameObject,out var ownerTree,out var owner);
                    rows.Add(new ButtonFeedbackRow{id=ownerTree==tree?owner?.layerId??-1:-1,
                        key="manual:"+GlobalObjectId.GetGlobalObjectIdSlow(button),path=Hierarchy(button.transform),
                        locked=true,managed=false,colorTint=button.transition==Selectable.Transition.ColorTint,
                        retained=button.transition.ToString(),hasVisual=button.targetGraphic is Image});
                }
            return rows;
        }
        static string Hierarchy(Transform node){var names=new List<string>();for(var t=node;t!=null;t=t.parent)names.Insert(0,t.name);return string.Join("/",names);}
        static bool HasVisual(PsdTree tree,PsdNode owner)
        {
            foreach(var node in tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete&&n.kind!=LayerKind.Group&&n.kind!=LayerKind.Text))
            {
                bool visible=true;var seen=new HashSet<int>();
                for(var current=node;current!=null&&seen.Add(current.layerId);current=tree.Find(current.parentId))
                {
                    if(!current.visible||current.opacity<=0||PrefabGenerator.IsStateSlot(current.slot))visible=false;
                    if(current.layerId==owner.layerId){if(visible)return true;break;}
                }
            }
            return false;
        }
        internal static string Context(PsdTree tree,PsdToolSettings settings)
        {
            string asset=AssetDatabase.GUIDToAssetPath(tree.prefabGuid??"");
            string path=string.IsNullOrEmpty(asset)?"":PathUtil.ToFullPath(asset);
            return AiProtocol.Hash(AiVisualInput.TreeJson(tree)+"|"+JsonUtility.ToJson(settings)+"|"+tree.psdPath+"|"+
                (File.Exists(tree.psdPath)?TreeBuilder.Stamp(tree.psdPath):"missing")+"|"+tree.prefabGuid+"|"+
                (File.Exists(path)?TreeBuilder.Stamp(path):"missing")+"|"+string.Join(";",tree.nodes.Select(n=>n.layerId+":"+n.stableKey+":"+n.buttonColorTint+":"+n.buttonFeedbackConfirmed)));
        }
        internal static void SetAll(IEnumerable<ButtonFeedbackRow> rows,bool colorTint){foreach(var row in rows.Where(r=>r.managed&&!r.locked))row.colorTint=colorTint;}
        internal static void Apply(PsdTree tree,List<ButtonFeedbackRow> rows)
        {
            var expected=tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete&&n.templateId=="Button").ToArray();
            var managed=rows.Where(r=>r.managed).ToArray();
            if(managed.Length!=expected.Length||managed.Select(r=>r.id).Distinct().Count()!=managed.Length||
                expected.Any(n=>!managed.Any(r=>r.id==n.layerId&&r.key==ResourceCatalog.Key(n))))
                throw new InvalidOperationException("按钮结构已变化，请重新点击生成，刷新确认列表。");
            Undo.RegisterCompleteObjectUndo(tree,"确认按钮交互反馈");
            foreach(var row in managed)
            {
                var node=tree.Find(row.id);
                if((node.locks&PropGroup.Component)!=0)continue;
                node.buttonColorTint=row.colorTint;
                node.buttonFeedbackConfirmed=true;
            }
            EditorUtility.SetDirty(tree);AssetDatabase.SaveAssetIfDirty(tree);
            if(AiMarker.Last?.tree==tree)
            {
                AiMarker.Last.generated=false;AiMarker.Last.generationChecked=false;
                AiMarker.Last.feedback="按钮反馈选择已保存。";
            }
        }
    }
}
