using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class PsdNodePreview : IDisposable
    {
        Texture2D texture;string signature,error;
        internal static Rect Bounds(PsdTree tree,PsdNode node,HashSet<int> seen=null)
        {
            seen=seen??new HashSet<int>();if(node==null||!seen.Add(node.layerId))return default;
            if(node.aiBoundsOverride||node.kind!=LayerKind.Group)return node.kind==LayerKind.Text&&node.text?.rect.width>0?node.text.rect:new Rect(node.psdRect.x,node.psdRect.y,node.psdRect.width,node.psdRect.height);
            Rect? result=null;
            foreach(var child in tree.nodes)if(child.parentId==node.layerId&&child.status!=NodeStatus.PendingDelete&&child.visible)
            {var r=Bounds(tree,child,seen);if(r.width>0&&r.height>0)result=result.HasValue?PrefabGenerator.Union(result.Value,r):r;}
            return result??new Rect(node.psdRect.x,node.psdRect.y,node.psdRect.width,node.psdRect.height);
        }
        void Load(PsdTree tree)
        {
            string key=tree.psdPath+"|"+(File.Exists(tree.psdPath)?TreeBuilder.Stamp(tree.psdPath):"missing");if(key==signature)return;
            Dispose();signature=key;error=null;
            try
            {
                if(!File.Exists(tree.psdPath))throw new IOException("PSD 不可访问，暂不能定位预览。");
                var doc=Parser.PsdParser.Parse(tree.psdPath);
                using(var reader=new Parser.PsdPixelReader(doc))
                {
                    var pixels=reader.DecodeComposite();if(pixels==null)throw new IOException("PSD 无合成预览，请启用最大兼容后保存。");
                    float scale=Mathf.Min(1,1536f/Mathf.Max(doc.Width,doc.Height));int w=Mathf.Max(1,Mathf.RoundToInt(doc.Width*scale)),h=Mathf.Max(1,Mathf.RoundToInt(doc.Height*scale));
                    var small=new Color32[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)small[(h-1-y)*w+x]=pixels[(y*doc.Height/h)*doc.Width+x*doc.Width/w];
                    texture=new Texture2D(w,h,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};texture.SetPixels32(small);texture.Apply();
                }
            }catch(Exception e){error=e.Message;}
        }
        internal void Draw(PsdTree tree,PsdNode node,float height=140,PsdToolSettings settings=null)
        {
            Load(tree);
            if(texture==null){PsdEditorUI.Notice(error??"暂无预览",MessageType.Info);return;}
            using(new EditorGUILayout.HorizontalScope())
            {
                var crop=GUILayoutUtility.GetRect(40,height,GUILayout.ExpandWidth(true));DrawCrop(crop,tree,node);
                if(GUI.Button(crop,new GUIContent("","定位节点"),GUIStyle.none))PsdToolWindow.FocusNode(tree,settings??PsdToolSettings.TryGet(),node.layerId);
                var overview=GUILayoutUtility.GetRect(40,height,GUILayout.ExpandWidth(true));DrawOverview(overview,tree,node);
                if(GUI.Button(overview,new GUIContent("","定位节点"),GUIStyle.none))PsdToolWindow.FocusNode(tree,settings??PsdToolSettings.TryGet(),node.layerId);
            }
            PsdEditorUI.Text("左：原稿局部　右：整页定位（黄框）",EditorStyles.wordWrappedMiniLabel);
        }
        internal void DrawCrop(Rect area,PsdTree tree,PsdNode node)
        {
            Load(tree);EditorGUI.DrawRect(area,new Color(.1f,.1f,.1f));
            if(texture==null){GUI.Label(area,"暂无 PSD 预览",EditorStyles.centeredGreyMiniLabel);return;}
            var r=Bounds(tree,node);float dw=Mathf.Max(1,tree.docSize.x),dh=Mathf.Max(1,tree.docSize.y);
            float left=Mathf.Clamp01(r.xMin/dw),right=Mathf.Clamp01(r.xMax/dw),top=Mathf.Clamp01(r.yMin/dh),bottom=Mathf.Clamp01(r.yMax/dh);
            if(right>left&&bottom>top)
            {var fitted=AiRegionPreviewWindow.Fit(area,(right-left)*dw,(bottom-top)*dh);GUI.DrawTextureWithTexCoords(fitted,texture,new Rect(left,1-bottom,right-left,bottom-top));}
        }
        void DrawOverview(Rect area,PsdTree tree,PsdNode node)
        {
            EditorGUI.DrawRect(area,new Color(.1f,.1f,.1f));
            var r=Bounds(tree,node);float dw=Mathf.Max(1,tree.docSize.x),dh=Mathf.Max(1,tree.docSize.y);
            float left=Mathf.Clamp01(r.xMin/dw),right=Mathf.Clamp01(r.xMax/dw),top=Mathf.Clamp01(r.yMin/dh),bottom=Mathf.Clamp01(r.yMax/dh);
            var fit=AiRegionPreviewWindow.Fit(area,texture.width,texture.height);GUI.DrawTexture(fit,texture);
            var box=Rect.MinMaxRect(fit.x+left*fit.width,fit.y+top*fit.height,fit.x+right*fit.width,fit.y+bottom*fit.height);
            EditorGUI.DrawRect(new Rect(box.x,box.y,box.width,2),Color.yellow);EditorGUI.DrawRect(new Rect(box.x,box.yMax-2,box.width,2),Color.yellow);
            EditorGUI.DrawRect(new Rect(box.x,box.y,2,box.height),Color.yellow);EditorGUI.DrawRect(new Rect(box.xMax-2,box.y,2,box.height),Color.yellow);
        }
        public void Dispose(){if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);texture=null;signature=null;}
    }

    internal sealed class PsdNodePreviewWindow : EditorWindow
    {
        PsdTree tree;int id;PsdToolSettings config;readonly PsdNodePreview preview=new PsdNodePreview();
        internal static void Open(PsdTree tree,int id,PsdToolSettings settings=null)
        {
            PsdToolWindow.FocusNode(tree,settings??PsdToolSettings.TryGet(),id);
            var window=CreateInstance<PsdNodePreviewWindow>();window.tree=tree;window.id=id;window.config=settings;
            window.titleContent=new GUIContent("节点原稿预览");window.minSize=new Vector2(580,250);window.ShowUtility();
        }
        void OnGUI()
        {
            var node=tree!=null?tree.Find(id):null;if(node==null){Close();return;}
            PsdEditorUI.Text(NodeNaming.Path(tree,node),EditorStyles.wordWrappedLabel);
            preview.Draw(tree,node,Mathf.Max(140,position.height-65),config);
        }
        void OnDisable()=>preview.Dispose();
    }
}
