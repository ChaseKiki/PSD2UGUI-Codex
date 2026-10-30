using System;
using System.Collections.Generic;
using UnityEngine;

namespace PsdTool
{
    public enum LayerKind
    {
        Image,
        Text,
        Group,
        Fill,
        SmartObject,
    }

    public enum DecisionSource
    {
        None,
        Marker,
        Rule,
        AI,
        Manual,
    }

    public enum NodeStatus
    {
        New,
        Synced,
        PendingDelete,
    }

    public enum NineSliceOverride
    {
        Auto,
        Force,
        Forbid,
    }

    [Flags]
    public enum PropGroup
    {
        None = 0,
        Rect = 1 << 0,
        Anchor = 1 << 1,
        Sprite = 1 << 2,
        Color = 1 << 3,
        TextContent = 1 << 4,
        TextStyle = 1 << 5,
        Active = 1 << 6,
        Name = 1 << 7,
        Parent = 1 << 8,
        Component = 1 << 9,
    }

    [Serializable]
    public class ImageInfo
    {
        public long pixelHash;
        public string assetPath;
        public string assetGuid;
        public RectInt trimmedRect;
        public bool fxBaked;
        public RectInt logicalRect;
        public float renderOpacity = 1f;
        public bool isEmpty;
        public bool isSolid;
        public Color32 solidColor;
        public bool useRawImage;
        public bool sliced;
        public bool tiled;
        public int reusedFromLayerId = -1;
        public string reusedFromPath;
        public Vector4 border;
    }

    public enum SpriteScope
    {
        Other,
        Panel,
        Module,
        Common,
    }

    public enum SuggestionKind
    {
        MoveToCommon,
        UseSimilar,
        ApplyRule,
        ApplyAi,
    }

    [Serializable]
    public class ResourceSuggestion
    {
        public SuggestionKind kind;
        public int layerId;
        public string assetPath;
        public string message;
        public string assetGuid;
        public bool done;

        /// <summary>ApplyRule 时写入的模板 ID。</summary>
        public string templateId;
        public string slot;
    }

    [Serializable]
    public class TextRunInfo
    {
        public int start;
        public int length;
        public string font;
        public float fontSize;
        public Color32 color;
        public bool bold;
        public bool italic;
    }

    [Serializable]
    public class TextEffectInfo
    {
        public bool enabled;
        public Color32 color = new Color32(0, 0, 0, 255);
        public float size;
        public float distance;
        public float angle = 120f;
        public float opacity = 1f;
    }

    [Serializable]
    public class TextInfo
    {
        public string content;
        public string font;
        public float fontSize;
        public Color32 color;
        public bool bold;
        public bool italic;
        public float tracking;
        public float leading;
        public bool autoLeading;
        public int justification;
        public bool isBox;
        public int lineCount;

        /// <summary>文本框矩形（PSD 坐标）。</summary>
        public Rect rect;

        public List<TextRunInfo> runs = new List<TextRunInfo>();
        public TextEffectInfo outline = new TextEffectInfo();
        public TextEffectInfo shadow = new TextEffectInfo();
        public bool hasUnsupportedEffects;
    }

    public enum ProgressFillMode { Auto, Horizontal, Vertical, Radial360, Radial180, Radial90 }

    [Serializable]
    public class PsdNode
    {
        public int layerId;
        public string stableKey;
        public string generatedAssetGuid;
        public UnityEngine.Object resourceOverride;
        public bool aiGeneratedGroup;
        public bool aiHasParentOverride;
        public int aiParentId = -1;
        public bool aiBoundsOverride;
        public int parentId = -1;
        public int order;
        public string psdName;
        public string goName;

        public LayerKind kind;
        public string templateId;
        public string slot;
        public List<string> markers = new List<string>();
        public DecisionSource source;
        public float confidence = 1f;

        /// <summary>空 = 居中；否则为 tl/t/tr/l/c/r/bl/b/br/stretch/hstretch/vstretch。</summary>
        public string anchorPreset = string.Empty;
        public ProgressFillMode progressFill;
        public bool progressReverse, progressClockwise = true;
        public int progressOrigin;
        public float progressValue = 1f;
        public bool generateBind;
        public bool underSafeArea;
        public NineSliceOverride nineOverride;
        public Vector4 nineBorder;

        /// <summary>-1 表示使用设置默认值。</summary>
        public float pressScale = -1f;

        /// <summary>PSD 图层记录中的像素边界（不含图层样式效果）。</summary>
        public RectInt psdRect;
        public bool visible = true;
        public float opacity = 1f;
        public string blendKey = "norm";
        public bool clipping;
        public bool hasEffects;

        public ImageInfo image = new ImageInfo();
        public TextInfo text = new TextInfo();

        public PropGroup locks;
        public PropGroup forceUpdateGroups;
        public string snapshotJson;
        public NodeStatus status;
        public string goGlobalId;
    }

    public class PsdTree : ScriptableObject
    {
        public string psdPath;
        public string psdStamp;
        public Vector2Int docSize;
        public string moduleName;
        public string panelName;
        public string prefabGuid;
        public string previewPath;
        public int toolVersion = 3;
        public List<PsdNode> nodes = new List<PsdNode>();
        public List<string> warnings = new List<string>();
        public List<ResourceSuggestion> suggestions = new List<ResourceSuggestion>();

        public string PrefabPath(PsdToolSettings settings) => settings.PrefabPath(moduleName, panelName);

        public PsdNode Find(int layerId)
        {
            foreach (var n in nodes)
                if (n.layerId == layerId) return n;
            return null;
        }
    }
}
