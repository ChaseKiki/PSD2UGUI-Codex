using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class NineShrinkReviewPanel : IDisposable
    {
        readonly List<string> issues=new List<string>();
        List<NineShrinkCandidate> candidates=new List<NineShrinkCandidate>();
        readonly Dictionary<int,Texture2D[]> textures=new Dictionary<int,Texture2D[]>();
        readonly Queue<int> order=new Queue<int>();
        PsdTree tree;
        string stamp,error;
        bool expanded=true,showHandled;
        int page;
        string Context(PsdTree source)=>source.psdPath+"|"+(File.Exists(source.psdPath)?TreeBuilder.Stamp(source.psdPath):"missing")+"|"+
            string.Join(";",source.nodes.Select(n=>n.layerId+":"+n.status+":"+n.nineOverride+":"+n.nineBorder+":"+string.Join(",",n.markers??new List<string>())+":"+(n.resourceOverride==null?0:n.resourceOverride.GetInstanceID())+":"+n.generatedAssetGuid));
        internal void Draw(PsdTree source,PsdToolSettings settings)
        {
            if(tree!=source){Dispose();tree=source;stamp=null;candidates.Clear();issues.Clear();error=null;page=0;}
            int pending=candidates.Count(c=>!Handled(c));
            expanded=PsdEditorUI.CopyFoldout(expanded,"九宫缩图 · 人工确认（待处理 "+pending+" 项）",true);
            if(!expanded)return;
            using(PsdEditorUI.Section(null))
            {
                PsdEditorUI.Notice("默认不缩图。分析不会修改 PSD、切图或 Prefab；只有批准并生成 / 更新后才导出缩图。可跳过本列表继续生成。",MessageType.Info);
                if(PsdEditorUI.Action(stamp==null?"分析可缩图资源":"重新分析九宫缩图建议"))
                {
                    try
                    {
                        var nextIssues=new List<string>();var next=NineShrinkReview.Scan(source,settings,nextIssues);
                        Dispose();candidates=next;issues.Clear();issues.AddRange(nextIssues);stamp=Context(source);page=0;error=null;
                    }
                    catch(Exception e){error=e.Message;}
                    finally{EditorUtility.ClearProgressBar();}
                    GUIUtility.ExitGUI();
                }
                if(error!=null)PsdEditorUI.Notice(error,MessageType.Warning);
                if(stamp==null)return;
                bool stale=stamp!=Context(source);
                if(stale)PsdEditorUI.Notice("来源或九宫配置已变化，请重新分析后确认。旧选择不会强制用于变化后的图片。",MessageType.Warning);
                PsdEditorUI.Notice("PNG 文件差值是实测；RGBA32 是未压缩基准；Android / iOS 是按格式、块对齐和 Mip 估算的独立纹理负载。进图集后的页数、APK / IPA 最终大小及打包压缩收益未测，不能直接相加当作包体减少。",MessageType.Info);
                PsdEditorUI.Text("候选 "+candidates.Count+" 项 · 待确认 "+pending+" 项 · 已处理 "+(candidates.Count-pending)+" 项",EditorStyles.wordWrappedMiniLabel);
                showHandled=PsdEditorUI.CopyToggleLeft("显示已处理项（可撤销或改选）",showHandled);
                var visible=candidates.Where(c=>showHandled||!Handled(c)).ToArray();
                const int perPage=12;page=Mathf.Clamp(page,0,Math.Max(0,(visible.Length-1)/perPage));
                using(new EditorGUILayout.HorizontalScope())
                {
                    PsdEditorUI.Text("第 "+(page+1)+" / "+Math.Max(1,(visible.Length+perPage-1)/perPage)+" 页",EditorStyles.miniLabel);
                    using(new EditorGUI.DisabledScope(page==0))if(PsdEditorUI.CopyButton("上一页",GUILayout.Width(62)))page--;
                    using(new EditorGUI.DisabledScope((page+1)*perPage>=visible.Length))if(PsdEditorUI.CopyButton("下一页",GUILayout.Width(62)))page++;
                }
                foreach(var candidate in visible.Skip(page*perPage).Take(perPage).ToArray())
                {
                    var node=source.Find(candidate.id);if(node==null)continue;
                    using(PsdEditorUI.Section(null))
                    {
                        PsdEditorUI.Text(NodeNaming.Path(source,node),EditorStyles.wordWrappedLabel);
                        PsdEditorUI.Text(candidate.width+"×"+candidate.height+" → "+candidate.reducedWidth+"×"+candidate.reducedHeight+
                            " · "+(Handled(candidate)?node.nineShrinkDecision==NineShrinkDecision.Shrink?"已批准缩图，下次生成使用":"已选择保留原尺寸":"等待人工确认"),EditorStyles.wordWrappedMiniLabel);
                        var pictures=Pictures(candidate);
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            Preview(pictures[0],"原切图",()=>PsdToolWindow.FocusNode(source,settings,node.layerId));
                            Preview(pictures[1],"缩图还原到原尺寸（预览已缩放）",()=>PsdToolWindow.FocusNode(source,settings,node.layerId));
                        }
                        PsdEditorUI.Text("像素还原误差：平均 "+candidate.averageError.ToString("0.###")+" / 通道，最大 "+candidate.maxError+"（0～255）；这是九宫重建误差，未包含平台有损压缩。",EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text("PNG 文件（实测）："+NineShrinkReview.Bytes(candidate.pngBefore)+" → "+NineShrinkReview.Bytes(candidate.pngAfter)+"；"+NineShrinkReview.Difference(candidate.pngBefore,candidate.pngAfter),EditorStyles.wordWrappedLabel);
                        long before=(long)candidate.width*candidate.height*4,after=(long)candidate.reducedWidth*candidate.reducedHeight*4;
                        PsdEditorUI.Text("RGBA32 未压缩基准："+NineShrinkReview.Bytes(before)+" → "+NineShrinkReview.Bytes(after)+"；"+NineShrinkReview.Difference(before,after),EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text("图集矩形面积（未计 padding / 对齐）："+((long)candidate.width*candidate.height)+" → "+((long)candidate.reducedWidth*candidate.reducedHeight)+" 像素；释放矩形空间不等于图集页字节立即减少。",EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text(NineShrinkReview.PlatformEstimate(source,node,settings,candidate,"Android"),EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text(NineShrinkReview.PlatformEstimate(source,node,settings,candidate,"iPhone"),EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text("Prefab 的 PSD 布局尺寸保留；Sprite.rect 会变小，程序调用 SetNativeSize 时请注意。共享 / 手改资源按现有保护规则另存，不覆盖外部显式引用。",EditorStyles.wordWrappedMiniLabel);
                        using(new EditorGUILayout.HorizontalScope())using(new EditorGUI.DisabledScope(stale))
                        {
                            if(PsdEditorUI.Action("批准缩图"))Choose(candidate,NineShrinkDecision.Shrink);
                            if(PsdEditorUI.Action("保留原尺寸"))Choose(candidate,NineShrinkDecision.KeepOriginal);
                            if(Handled(candidate)&&PsdEditorUI.Action("重新待确认"))Choose(candidate,NineShrinkDecision.Unreviewed);
                        }
                        if(PsdEditorUI.Action("定位节点"))PsdToolWindow.FocusNode(source,settings,node.layerId);
                    }
                }
                foreach(var issue in issues)PsdEditorUI.Notice(issue,MessageType.Warning);
                if(candidates.Count==0)PsdEditorUI.Text("未找到通过像素回验的缩图候选。纯色、RawImage、Tiled、禁用九宫和外部指定资源不参与缩图。",EditorStyles.wordWrappedLabel);
            }
        }
        bool Handled(NineShrinkCandidate c){var node=tree?.Find(c.id);return node!=null&&node.nineShrinkDecision!=NineShrinkDecision.Unreviewed&&node.nineShrinkDecisionHash==c.fingerprint;}
        void Choose(NineShrinkCandidate candidate,NineShrinkDecision decision){NineShrinkReview.Decide(tree,candidate,decision);GUIUtility.ExitGUI();}
        static void Preview(Texture2D image,string title,Action locate)
        {
            using(new EditorGUILayout.VerticalScope())
            {
                PsdEditorUI.Text(title,EditorStyles.wordWrappedMiniLabel);
                var area=GUILayoutUtility.GetRect(30,120,GUILayout.ExpandWidth(true));EditorGUI.DrawRect(area,new Color(.18f,.18f,.18f));
                if(image!=null)GUI.DrawTexture(area,image,ScaleMode.ScaleToFit);
                if(GUI.Button(area,new GUIContent("","定位节点"),GUIStyle.none))locate();
            }
        }
        Texture2D[] Pictures(NineShrinkCandidate c)
        {
            if(textures.TryGetValue(c.id,out var current))return current;
            while(order.Count>=16){int id=order.Dequeue();foreach(var texture in textures[id])UnityEngine.Object.DestroyImmediate(texture);textures.Remove(id);}
            var images=new[]{new Texture2D(2,2),new Texture2D(2,2)};
            images[0].LoadImage(c.beforePreview);images[1].LoadImage(c.afterPreview);foreach(var texture in images)texture.hideFlags=HideFlags.HideAndDontSave;
            textures[c.id]=images;order.Enqueue(c.id);return images;
        }
        public void Dispose(){foreach(var pair in textures.Values)foreach(var texture in pair)UnityEngine.Object.DestroyImmediate(texture);textures.Clear();order.Clear();}
    }
}
