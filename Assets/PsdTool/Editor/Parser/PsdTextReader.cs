using System.Collections.Generic;
using UnityEngine;
using static PsdTool.Parser.EngineDataPath;

namespace PsdTool.Parser
{
    /// <summary>解析 TySh 附加信息块。</summary>
    internal static class PsdTextReader
    {
        public static PsdText Read(BigEndianReader r)
        {
            r.ReadUInt16();
            var t = new PsdText
            {
                Xx = r.ReadDouble(),
                Xy = r.ReadDouble(),
                Yx = r.ReadDouble(),
                Yy = r.ReadDouble(),
                Tx = r.ReadDouble(),
                Ty = r.ReadDouble(),
            };
            r.ReadUInt16();
            r.ReadUInt32();
            var desc = DescriptorReader.ReadDescriptor(r);

            string plain = desc.Get<string>("Txt ", null);
            var engineData = EngineDataParser.Parse(desc.Get<byte[]>("EngineData", null));
            var engine = Get(engineData, "EngineDict");

            string text = Get(engine, "Editor", "Text") as string ?? plain ?? string.Empty;
            ReadRuns(engineData, engine, text, t);

            text = text.Replace('\r', '\n');
            if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
            t.Content = text;

            var justification = Get(engine, "ParagraphRun", "RunArray", 0, "ParagraphSheet", "Properties", "Justification")
                                ?? Get(engineData, "ResourceDict", "ParagraphSheetSet", 0, "Properties", "Justification");
            t.Justification = (int)Num(justification, 0);

            var shape = Get(engine, "Rendered", "Shapes", "Children", 0);
            t.IsBox = (int)Num(Get(shape, "ShapeType"), 0) == 1;
            if (t.IsBox && Get(shape, "Cookie", "Photoshop", "BoxBounds") is List<object> box && box.Count == 4)
            {
                t.BoxLeft = Num(box[0], 0);
                t.BoxTop = Num(box[1], 0);
                t.BoxRight = Num(box[2], 0);
                t.BoxBottom = Num(box[3], 0);
            }
            else
            {
                t.IsBox = false;
            }

            return t;
        }

        static void ReadRuns(Dictionary<string, object> engineData, object engine, string text, PsdText t)
        {
            var fontSet = Get(engineData, "ResourceDict", "FontSet") as List<object>;
            var defaultStyle = Get(engineData, "ResourceDict", "StyleSheetSet", 0, "StyleSheetData") as Dictionary<string, object>;
            var runArray = Get(engine, "StyleRun", "RunArray") as List<object>;
            var runLengths = Get(engine, "StyleRun", "RunLengthArray") as List<object>;

            int count = runArray?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var style = Get(runArray[i], "StyleSheet", "StyleSheetData") as Dictionary<string, object>;
                t.Runs.Add(ReadRun(style, defaultStyle, fontSet, (int)Num(Get(runLengths, i), text.Length)));
            }

            if (t.Runs.Count == 0)
                t.Runs.Add(ReadRun(null, defaultStyle, fontSet, text.Length));
        }

        static PsdTextRun ReadRun(Dictionary<string, object> style, Dictionary<string, object> fallback, List<object> fontSet, int length)
        {
            object V(string key)
            {
                if (style != null && style.TryGetValue(key, out var v)) return v;
                if (fallback != null && fallback.TryGetValue(key, out var f)) return f;
                return null;
            }

            var run = new PsdTextRun
            {
                Length = length,
                Font = Get(fontSet, (int)Num(V("Font"), 0), "Name") as string ?? string.Empty,
                FontSize = Num(V("FontSize"), 12),
                AutoLeading = !(V("AutoLeading") is bool al) || al,
                Leading = Num(V("Leading"), 0),
                Tracking = Num(V("Tracking"), 0),
                FauxBold = V("FauxBold") is bool fb && fb,
                FauxItalic = V("FauxItalic") is bool fi && fi,
            };

            if (Get(V("FillColor"), "Values") is List<object> c && c.Count >= 4)
            {
                run.Color = new Color32(
                    ToByte(Num(c[1], 0)),
                    ToByte(Num(c[2], 0)),
                    ToByte(Num(c[3], 0)),
                    ToByte(Num(c[0], 1)));
            }
            return run;
        }

        static byte ToByte(double v) => (byte)Mathf.Clamp(Mathf.RoundToInt((float)v * 255f), 0, 255);
    }
}
