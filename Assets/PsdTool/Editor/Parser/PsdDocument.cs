using System;
using System.Collections.Generic;
using UnityEngine;

namespace PsdTool.Parser
{
    public sealed class PsdException : Exception
    {
        public PsdException(string message) : base(message) { }
    }

    public enum PsdLayerKind
    {
        Pixel,
        Group,
        Text,
        Fill,
        SmartObject,
        Adjustment,
    }

    public sealed class PsdDocument
    {
        public string FilePath;
        public bool IsPsb;
        public int Width;
        public int Height;
        public int ChannelCount;
        public int GlobalLightAngle = 120;
        public long CompositeOffset = -1;

        /// <summary>文件顺序（从下到上），不含分组结束标记。</summary>
        public readonly List<PsdLayer> Layers = new List<PsdLayer>();

        /// <summary>顶层图层，从下到上。</summary>
        public readonly List<PsdLayer> Roots = new List<PsdLayer>();

        public readonly List<string> Warnings = new List<string>();

        public PsdLayer FindLayer(int id)
        {
            foreach (var l in Layers)
                if (l.Id == id) return l;
            return null;
        }
    }

    public sealed class PsdChannel
    {
        public short Id;
        public long Offset;
        public long Length;
    }

    public sealed class PsdMask
    {
        public int Top, Left, Bottom, Right;
        public byte DefaultColor;
        public byte Flags;

        public bool Disabled => (Flags & 0x02) != 0;
        public int Width => Math.Max(0, Right - Left);
        public int Height => Math.Max(0, Bottom - Top);
    }

    public sealed class PsdLayer
    {
        public int Id = -1;
        public string Name = string.Empty;
        public int Top, Left, Bottom, Right;
        public byte Opacity = 255;
        public byte FillOpacity = 255;
        public bool Visible = true;
        public bool Clipping;
        public string BlendKey = "norm";
        public int SectionType;
        public PsdLayerKind Kind = PsdLayerKind.Pixel;
        public bool HasEffects;
        public PsdDescriptor Effects;
        public PsdText Text;
        public PsdMask Mask;
        public readonly List<PsdChannel> Channels = new List<PsdChannel>();
        public PsdLayer Parent;

        /// <summary>子图层，从下到上。</summary>
        public readonly List<PsdLayer> Children = new List<PsdLayer>();

        public int Width => Math.Max(0, Right - Left);
        public int Height => Math.Max(0, Bottom - Top);
        public RectInt Bounds => new RectInt(Left, Top, Width, Height);
    }

    public sealed class PsdText
    {
        public string Content = string.Empty;
        public double Xx = 1, Xy, Yx, Yy = 1, Tx, Ty;
        public bool IsBox;
        public double BoxLeft, BoxTop, BoxRight, BoxBottom;

        /// <summary>0 左对齐，1 右对齐，2 居中，3 及以上为两端对齐。</summary>
        public int Justification;

        public readonly List<PsdTextRun> Runs = new List<PsdTextRun>();
    }

    public sealed class PsdTextRun
    {
        public int Length;
        public string Font = string.Empty;
        public double FontSize = 12;
        public Color32 Color = new Color32(0, 0, 0, 255);
        public double Tracking;
        public double Leading;
        public bool AutoLeading = true;
        public bool FauxBold;
        public bool FauxItalic;
    }
}
