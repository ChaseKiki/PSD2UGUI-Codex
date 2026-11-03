using System;
using System.Collections.Generic;
using System.Linq;
using PsdTool.Parser;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class NineShrinkCandidate
    {
        internal int id,width,height,reducedWidth,reducedHeight,maxError;
        internal string fingerprint,path;
        internal float averageError;
        internal long pngBefore,pngAfter;
        internal byte[] beforePreview,afterPreview;
    }
    internal static class NineShrinkReview
    {
        internal static bool TryCandidate(PsdTree tree,PsdNode node,PsdToolSettings settings,ResourceCatalog catalog,Color32[] pixels,int w,int h,bool fxBaked,out NineSliceResult reduced,out string fingerprint)
        {
            reduced=default;fingerprint=null;
            if(node.resourceOverride!=null||node.status==NodeStatus.PendingDelete||node.nineOverride==NineSliceOverride.Forbid||
                Markers.Has(node.markers,"no9s")||Markers.Has(node.markers,"tile")||SpriteExporter.IsRaw(tree,node,settings,catalog,w,h))return false;
            bool force=node.nineOverride==NineSliceOverride.Force||Markers.Has(node.markers,"9s");
            if(fxBaked&&!force&&!StretchImagePolicy.NeedsSliceCheck(tree,node))return false;
            string arg=null;
            if(node.nineOverride==NineSliceOverride.Force&&node.nineBorder!=Vector4.zero)
                arg=BorderArg(node.nineBorder);
            else if(Markers.TryGetArg(node.markers,"9s",out var marker))arg=marker??"";
            else if(force)arg="";
            string path=AssetDatabase.GUIDToAssetPath(node.image.assetGuid??"");
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer!=null)
            {
                if(importer.textureType!=TextureImporterType.Sprite||importer.spriteImportMode!=SpriteImportMode.Single||SpriteIndex.IsTiled(path))return false;
                var record=catalog?.Find(node.image.assetGuid);
                if(!force&&(record?.manualBorder==true||record!=null&&importer.spriteBorder!=record.generatedBorder))arg=BorderArg(importer.spriteBorder);
            }
            if(!NineSlice.Try(pixels,w,h,arg,out reduced,true))return false;
            fingerprint=AiProtocol.Hash("nine-shrink-review-v1|"+PixelUtil.Hash(pixels,w,h)+"|"+w+"x"+h+"|"+
                PixelUtil.Hash(reduced.pixels,reduced.width,reduced.height)+"|"+reduced.width+"x"+reduced.height+"|"+BorderArg(reduced.border));
            return true;
        }
        static string BorderArg(Vector4 b)=>string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3}",b.x,b.w,b.z,b.y);
        internal static List<NineShrinkCandidate> Scan(PsdTree tree,PsdToolSettings settings,List<string> issues)
        {
            var doc=PsdParser.Parse(tree.psdPath);
            var targets=tree.nodes.Where(n=>n.status!=NodeStatus.PendingDelete&&n.kind!=LayerKind.Group&&n.kind!=LayerKind.Text&&n.resourceOverride==null).ToList();
            Dictionary<int,PhotoshopFxBaker.Entry> baked;
            try{baked=PhotoshopFxBaker.Prepare(doc,targets,settings);}
            catch(Exception e)
            {
                issues.Add("FX 临时烘焙未完成，相关图层本轮不分析："+e.Message);
                baked=new Dictionary<int,PhotoshopFxBaker.Entry>();
            }
            var catalog=ResourceCatalog.Load(settings,false);
            var result=new List<NineShrinkCandidate>();
            using(var reader=new PsdPixelReader(doc))
                foreach(var node in targets)
                {
                    if(EditorUtility.DisplayCancelableProgressBar("分析九宫缩图建议",node.goName,(float)targets.IndexOf(node)/Math.Max(1,targets.Count)))
                        throw new OperationCanceledException("已取消九宫分析；原图与原确认均未修改。");
                    try
                    {
                        var layer=doc.FindLayer(node.layerId);if(layer==null)continue;
                        if(settings.bakeImageEffects&&layer.HasEffects&&!baked.ContainsKey(layer.Id))continue;
                        var pixels=SpriteExporter.SourcePixels(reader,layer,node,baked,out var bounds,out bool fx);
                        if(pixels==null)continue;
                        var opaque=PixelUtil.OpaqueBounds(pixels,bounds.width,bounds.height);
                        if(opaque.width==0||opaque.height==0)continue;
                        var cropped=PixelUtil.Crop(pixels,bounds.width,opaque);
                        if(!fx&&(Markers.Has(node.markers,"color")||PixelUtil.IsSolid(cropped,out _)))continue;
                        if(!TryCandidate(tree,node,settings,catalog,cropped,opaque.width,opaque.height,fx,out var reduced,out string key))continue;
                        var restored=NineSlice.Reconstruct(reduced,opaque.width,opaque.height);
                        result.Add(new NineShrinkCandidate{id=node.layerId,width=opaque.width,height=opaque.height,
                            reducedWidth=reduced.width,reducedHeight=reduced.height,fingerprint=key,path=NodeNaming.Path(tree,node),
                            averageError=reduced.avgError,maxError=reduced.maxError,
                            pngBefore=PixelUtil.EncodePng(cropped,opaque.width,opaque.height).LongLength,pngAfter=PixelUtil.EncodePng(reduced.pixels,reduced.width,reduced.height).LongLength,
                            beforePreview=Convert.FromBase64String(AiVisualInput.EncodeFit(cropped,opaque.width,opaque.height,256)),
                            afterPreview=Convert.FromBase64String(AiVisualInput.EncodeFit(restored,opaque.width,opaque.height,256))});
                    }
                    catch(Exception e){issues.Add(node.goName+"："+e.Message);}
                }
            return result.OrderByDescending(c=>(long)c.width*c.height-(long)c.reducedWidth*c.reducedHeight).ToList();
        }
        internal static void Decide(PsdTree tree,NineShrinkCandidate candidate,NineShrinkDecision decision)
        {
            var node=tree.Find(candidate.id)??throw new InvalidOperationException("节点已删除，请重新分析。");
            Undo.RegisterCompleteObjectUndo(tree,"确认九宫缩图选择");
            node.nineShrinkDecision=decision;node.nineShrinkDecisionHash=candidate.fingerprint;
            EditorUtility.SetDirty(tree);AssetDatabase.SaveAssetIfDirty(tree);
            if(AiMarker.Last?.tree==tree)
            {
                AiMarker.Last.generated=false;AiMarker.Last.generationChecked=false;
                AiMarker.Last.feedback="九宫缩图选择已保存；生成 / 更新 Prefab 后生效。";
                AiMarker.Last.SaveReport();
            }
        }
        internal static string Bytes(long bytes)=>bytes>=1024*1024?(bytes/(1024f*1024f)).ToString("0.##")+" MiB":bytes>=1024?(bytes/1024f).ToString("0.##")+" KiB":bytes+" B";
        internal static string Difference(long before,long after)=>after<=before?"减少 "+Bytes(before-after)+(before>0?"（"+((before-after)*100d/before).ToString("0.#")+"%）":""):"增加 "+Bytes(after-before);
        internal static long Payload(int w,int h,TextureImporterFormat format,bool mipmaps)
        {
            int block=0,blockBytes=0,bytesPerPixel=0;
            string name=format.ToString();
            if(name.StartsWith("ASTC",StringComparison.Ordinal))
            {
                var match=System.Text.RegularExpressions.Regex.Match(name,@"(\d+)x\d+$");
                if(!match.Success)return -1;block=int.Parse(match.Groups[1].Value);blockBytes=16;
            }
            else if(name=="ETC2_RGBA8"||name=="DXT5"||name=="BC5"||name=="BC6H"||name=="BC7"){block=4;blockBytes=16;}
            else if(name=="ETC_RGB4"||name=="ETC2_RGB4"||name=="ETC2_RGB4_PUNCHTHROUGH_ALPHA"||name=="DXT1"||name=="BC4"){block=4;blockBytes=8;}
            else if(name=="RGBA32"||name=="ARGB32")bytesPerPixel=4;
            else if(name=="RGB24")bytesPerPixel=3;
            else if(name=="RGBA16"||name=="RGB16"||name=="RG16")bytesPerPixel=2;
            else if(name=="Alpha8"||name=="R8")bytesPerPixel=1;
            else return -1;
            long total=0;
            do
            {
                total+=block>0?(long)((w+block-1)/block)*((h+block-1)/block)*blockBytes:(long)w*h*bytesPerPixel;
                if(!mipmaps||w==1&&h==1)break;
                w=Math.Max(1,w/2);h=Math.Max(1,h/2);
            }while(true);
            return total;
        }
        internal static string PlatformEstimate(PsdTree tree,PsdNode node,PsdToolSettings settings,NineShrinkCandidate candidate,string platform)
        {
            var rule=settings.texturePlatforms?.FirstOrDefault(r=>r.platform==platform);
            var format=rule!=null&&rule.overridden?rule.format:TextureImporterFormat.Automatic;
            int max=rule?.maxTextureSize??settings.textureMaxSize;bool mipmaps=false;
            string basis="新资源配置";
            var catalog=ResourceCatalog.Load(settings,false);
            string path=AssetDatabase.GUIDToAssetPath(node.generatedAssetGuid??"");
            if(catalog!=null&&catalog.IsOwner(node.generatedAssetGuid,ResourceCatalog.Source(tree),ResourceCatalog.Key(node))&&
                !catalog.IsShared(node.generatedAssetGuid,ResourceCatalog.Source(tree),ResourceCatalog.Key(node))&&
                settings.Classify(path)==SpriteScope.Panel&&AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var actual=importer.GetPlatformTextureSettings(platform);
                format=actual.overridden?actual.format:TextureImporterFormat.Automatic;
                max=actual.overridden?actual.maxTextureSize:importer.maxTextureSize;mipmaps=importer.mipmapEnabled;basis="现有导入配置";
                if(importer.npotScale!=TextureImporterNPOTScale.None)return (platform=="iPhone"?"iOS":platform)+"：有 NPOT 重采样，未估算";
            }
            Vector2Int Fit(int w,int h){float ratio=Math.Min(1,max/(float)Math.Max(w,h));return new Vector2Int(Math.Max(1,Mathf.RoundToInt(w*ratio)),Math.Max(1,Mathf.RoundToInt(h*ratio)));}
            var before=Fit(candidate.width,candidate.height);var after=Fit(candidate.reducedWidth,candidate.reducedHeight);
            long a=Payload(before.x,before.y,format,mipmaps),b=Payload(after.x,after.y,format,mipmaps);
            string label=platform=="iPhone"?"iOS":platform;
            return a<0?label+"：自动 / 未支持格式，未估算纹理负载":label+" "+format+"（"+basis+(mipmaps?"，含 Mip":"")+"）："+Bytes(a)+" → "+Bytes(b)+"；"+Difference(a,b)+"（独立纹理负载估算）";
        }
    }
}
