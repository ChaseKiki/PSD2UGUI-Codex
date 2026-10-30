using System;
using System.Collections.Generic;
using System.IO;

namespace PsdTool.Parser
{
    /// <summary>
    /// 只解析结构，不解码像素；像素按需通过 <see cref="PsdPixelReader"/> 读取。
    /// 支持范围：8 位 RGB 的 PSD / PSB。
    /// </summary>
    public static class PsdParser
    {
        static readonly HashSet<string> LongLengthKeys = new HashSet<string>
        {
            "LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid", "PxSD",
        };

        static readonly HashSet<string> AdjustmentKeys = new HashSet<string>
        {
            "levl", "curv", "brit", "hue ", "hue2", "blnc", "selc", "mixr", "grdm", "phfl", "expA", "vibA", "thrs",
            "post", "nvrt", "blwh", "clrL", "CgEd",
        };

        const int SectionOpenFolder = 1;
        const int SectionClosedFolder = 2;
        const int SectionDivider = 3;

        public static PsdDocument Parse(string path)
        {
            if (!File.Exists(path)) throw new PsdException($"找不到文件：{path}");

            var doc = new PsdDocument { FilePath = path };
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var r = new BigEndianReader(fs);
                ReadHeader(r, doc);

                r.Skip(r.ReadUInt32());

                long resourcesEnd = r.ReadUInt32();
                resourcesEnd += r.Position;
                ReadImageResources(r, resourcesEnd, doc);
                r.Position = resourcesEnd;

                long layerMaskLength = r.ReadLength(doc.IsPsb);
                long layerMaskEnd = r.Position + layerMaskLength;
                if (layerMaskLength > 0) ReadLayerInfo(r, doc);
                r.Position = layerMaskEnd;

                doc.CompositeOffset = r.Position < r.Length ? r.Position : -1;
            }

            AssignMissingIds(doc);
            return doc;
        }

        static void ReadHeader(BigEndianReader r, PsdDocument doc)
        {
            if (r.ReadAscii(4) != "8BPS") throw new PsdException("不是有效的 PSD / PSB 文件");

            int version = r.ReadUInt16();
            if (version != 1 && version != 2) throw new PsdException($"不支持的 PSD 版本：{version}");
            doc.IsPsb = version == 2;

            r.Skip(6);
            doc.ChannelCount = r.ReadUInt16();
            doc.Height = r.ReadInt32();
            doc.Width = r.ReadInt32();
            int depth = r.ReadUInt16();
            int colorMode = r.ReadUInt16();

            if (depth != 8)
                throw new PsdException($"只支持 8 位/通道，当前为 {depth} 位。请在 PS 中执行：图像 > 模式 > 8 位/通道。");
            if (colorMode != 3)
                throw new PsdException("只支持 RGB 颜色模式。请在 PS 中执行：图像 > 模式 > RGB 颜色。");
        }

        static void ReadImageResources(BigEndianReader r, long end, PsdDocument doc)
        {
            while (r.Position + 12 <= end)
            {
                if (r.ReadAscii(4) != "8BIM") break;
                int id = r.ReadUInt16();
                r.ReadPascalString(2);
                uint size = r.ReadUInt32();
                long dataStart = r.Position;

                if (id == 1037 && size >= 4) doc.GlobalLightAngle = r.ReadInt32();

                r.Position = dataStart + size + (size & 1);
            }
        }

        static void ReadLayerInfo(BigEndianReader r, PsdDocument doc)
        {
            long length = r.ReadLength(doc.IsPsb);
            if (length == 0) return;

            int count = Math.Abs((int)r.ReadInt16());
            var records = new List<PsdLayer>(count);
            for (int i = 0; i < count; i++)
                records.Add(ReadLayerRecord(r, doc));

            long offset = r.Position;
            foreach (var layer in records)
            {
                foreach (var ch in layer.Channels)
                {
                    ch.Offset = offset;
                    offset += ch.Length;
                }
            }

            BuildTree(doc, records);
        }

        static PsdLayer ReadLayerRecord(BigEndianReader r, PsdDocument doc)
        {
            var layer = new PsdLayer
            {
                Top = r.ReadInt32(),
                Left = r.ReadInt32(),
                Bottom = r.ReadInt32(),
                Right = r.ReadInt32(),
            };

            int channelCount = r.ReadUInt16();
            for (int i = 0; i < channelCount; i++)
            {
                layer.Channels.Add(new PsdChannel
                {
                    Id = r.ReadInt16(),
                    Length = r.ReadLength(doc.IsPsb),
                });
            }

            r.ReadAscii(4);
            layer.BlendKey = r.ReadAscii(4);
            layer.Opacity = r.ReadByte();
            layer.Clipping = r.ReadByte() != 0;
            byte flags = r.ReadByte();
            layer.Visible = (flags & 0x02) == 0;
            r.ReadByte();

            long extraEnd = r.ReadUInt32();
            extraEnd += r.Position;

            long maskEnd = r.ReadUInt32();
            maskEnd += r.Position;
            if (maskEnd - r.Position >= 18)
            {
                layer.Mask = new PsdMask
                {
                    Top = r.ReadInt32(),
                    Left = r.ReadInt32(),
                    Bottom = r.ReadInt32(),
                    Right = r.ReadInt32(),
                    DefaultColor = r.ReadByte(),
                    Flags = r.ReadByte(),
                };
            }
            r.Position = maskEnd;

            r.Skip(r.ReadUInt32());
            layer.Name = r.ReadPascalString(4);

            bool legacyEffects = false;
            while (r.Position + 12 <= extraEnd)
            {
                string signature = r.ReadAscii(4);
                if (signature != "8BIM" && signature != "8B64") break;
                string key = r.ReadAscii(4);
                long length = doc.IsPsb && LongLengthKeys.Contains(key) ? r.ReadInt64() : r.ReadUInt32();
                long dataStart = r.Position;

                try
                {
                    if (key == "lrFX") legacyEffects = true;
                    else ReadLayerBlock(r, key, layer);
                }
                catch (Exception e)
                {
                    doc.Warnings.Add($"图层“{layer.Name}”的 {key} 数据解析失败，已跳过：{e.Message}");
                }

                r.Position = dataStart + length;
            }

            if (layer.Effects == null && legacyEffects) layer.HasEffects = true;

            r.Position = extraEnd;
            return layer;
        }

        static void ReadLayerBlock(BigEndianReader r, string key, PsdLayer layer)
        {
            switch (key)
            {
                case "luni":
                    string name = r.ReadUnicodeString();
                    if (!string.IsNullOrEmpty(name)) layer.Name = name;
                    break;
                case "lyid":
                    layer.Id = r.ReadInt32();
                    break;
                case "iOpa":
                    layer.FillOpacity = r.ReadByte();
                    break;
                case "lsct":
                case "lsdk":
                    layer.SectionType = r.ReadInt32();
                    break;
                case "TySh":
                    layer.Kind = PsdLayerKind.Text;
                    layer.Text = PsdTextReader.Read(r);
                    break;
                case "SoCo":
                case "GdFl":
                case "PtFl":
                    if (layer.Kind == PsdLayerKind.Pixel) layer.Kind = PsdLayerKind.Fill;
                    break;
                case "SoLd":
                case "PlLd":
                case "SoLE":
                    if (layer.Kind == PsdLayerKind.Pixel) layer.Kind = PsdLayerKind.SmartObject;
                    break;
                case "lfx2":
                    r.ReadUInt32();
                    r.ReadUInt32();
                    layer.Effects = DescriptorReader.ReadDescriptor(r);
                    layer.HasEffects = EffectsEnabled(layer.Effects);
                    break;
                default:
                    if (AdjustmentKeys.Contains(key) && layer.Kind == PsdLayerKind.Pixel)
                        layer.Kind = PsdLayerKind.Adjustment;
                    break;
            }
        }

        static bool EffectsEnabled(PsdDescriptor fx)
        {
            if (!fx.Get("masterFXSwitch", true)) return false;
            foreach (var value in fx.Items.Values)
            {
                if (value is PsdDescriptor d && d.Get("enab", false)) return true;
                if (value is List<object> list)
                    foreach (var item in list)
                        if (item is PsdDescriptor ld && ld.Get("enab", false)) return true;
            }
            return false;
        }

        /// <summary>图层记录按从下到上排列，分组以“结束标记 … 分组本身”的形式出现。</summary>
        static void BuildTree(PsdDocument doc, List<PsdLayer> records)
        {
            var stack = new Stack<List<PsdLayer>>();
            var current = new List<PsdLayer>();

            foreach (var layer in records)
            {
                if (layer.SectionType == SectionDivider)
                {
                    stack.Push(current);
                    current = new List<PsdLayer>();
                    continue;
                }

                if (layer.SectionType == SectionOpenFolder || layer.SectionType == SectionClosedFolder)
                {
                    layer.Kind = PsdLayerKind.Group;
                    foreach (var child in current)
                    {
                        child.Parent = layer;
                        layer.Children.Add(child);
                    }
                    current = stack.Count > 0 ? stack.Pop() : new List<PsdLayer>();
                }

                current.Add(layer);
                doc.Layers.Add(layer);
            }

            while (stack.Count > 0)
            {
                var outer = stack.Pop();
                outer.AddRange(current);
                current = outer;
                doc.Warnings.Add("图层分组结构不完整，已尽量还原");
            }

            doc.Roots.AddRange(current);
        }

        static void AssignMissingIds(PsdDocument doc)
        {
            var used = new HashSet<int>();
            int max = 0;
            foreach (var l in doc.Layers)
                if (l.Id > max) max = l.Id;

            bool missing = false;
            foreach (var l in doc.Layers)
            {
                if (l.Id < 0 || !used.Add(l.Id))
                {
                    l.Id = ++max;
                    used.Add(l.Id);
                    missing = true;
                }
            }

            if (missing)
                doc.Warnings.Add("部分图层没有稳定 ID（PSD 由旧版本 PS 保存），重新导入时可能无法准确对应");
        }
    }
}
