using System;
using System.Collections.Generic;

namespace PsdTool
{
    /// <summary>
    /// 图层名标记：<c>名称@标记@标记:参数</c>。中英文别名等价，不区分大小写。
    /// 列表与指南 11.2 节一致。
    /// </summary>
    public static class Markers
    {
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 控件类型
            { "按钮", "btn" }, { "开关", "toggle" }, { "页签", "tab" }, { "页签组", "toggles" },
            { "滑动条", "slider" }, { "进度条", "progress" }, { "滚动条", "scrollbar" }, { "列表", "list" },
            { "下拉", "dropdown" }, { "输入框", "input" }, { "遮罩", "mask" }, { "组", "group" },
            { "预制体", "prefab" }, { "合并", "merge" }, { "忽略", "ignore" },
            // 插槽
            { "底", "bg" }, { "文字", "label" }, { "图标", "icon" }, { "红点", "badge" }, { "热区", "hit" },
            { "按下", "pressed" }, { "禁用", "disabled" }, { "选中", "on" }, { "选中文字", "labelOn" },
            { "轨道", "track" }, { "填充", "fill" }, { "滑块", "handle" }, { "视口", "viewport" },
            { "列表项", "item" }, { "内容", "content" }, { "当前值", "caption" }, { "箭头", "arrow" }, { "下拉列表", "template" },
            { "勾选", "checkmark" }, { "对勾", "checkmark" }, { "勾", "checkmark" },
            { "mark", "checkmark" }, { "check", "checkmark" },
            { "占位", "placeholder" }, { "输入文字", "text" }, { "边界", "rect" },
            { "滚动条槽", "scrollbar" },
            { "dpdicon", "arrow" }, { "vbarbg", "vbarbg" }, { "vbar", "vbar" },
            { "hbarbg", "hbarbg" }, { "hbar", "hbar" },
            // 属性
            { "九宫", "9s" }, { "不九宫", "no9s" }, { "平铺", "tile" }, { "大图", "raw" }, { "通用", "common" },
            { "纯色", "color" }, { "名", "name" }, { "绑定", "bind" }, { "锚", "anchor" }, { "安全区", "safe" },
        };

        public static readonly Dictionary<string, string> TypeToTemplate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "btn", "Button" }, { "toggle", "Toggle" }, { "tab", "Tab" }, { "toggles", "ToggleGroup" },
            { "slider", "Slider" }, { "progress", "Progress" }, { "scrollbar", "Scrollbar" }, { "list", "ScrollView" },
            { "dropdown", "Dropdown" }, { "dpd", "Dropdown" }, { "input", "InputField" }, { "mask", "Mask" }, { "group", "Group" },
            { "sv", "ScrollView" }, { "tg", "Toggle" },
            { "prefab", "Prefab" },
        };

        public static readonly HashSet<string> Slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bg", "label", "icon", "badge", "hit", "pressed", "disabled", "on", "labelOn", "track", "fill", "handle",
            "viewport", "content", "item", "scrollbar", "vbarbg", "vbar", "hbarbg", "hbar",
            "caption", "arrow", "template", "checkmark", "placeholder", "text", "rect",
        };

        public static readonly string[] TemplateIds =
        {
            "", "Group", "Button", "Toggle", "Tab", "ToggleGroup", "Slider", "Progress", "Scrollbar", "ScrollView",
            "Dropdown", "InputField", "Mask", "Prefab",
        };

        public static readonly string[] TemplateLabels =
        {
            "（无）", "组 Group", "按钮 Button", "开关 Toggle", "页签 Tab", "开关组 ToggleGroup",
            "滑动条 Slider", "进度条 Progress", "滚动条 Scrollbar", "列表 ScrollView",
            "下拉 Dropdown", "输入框 InputField", "遮罩 Mask", "预制体 Prefab",
        };

        /// <summary>图层名最后一个点后面的后缀。空字符串表示只剥掉、不记录（如 .img）。</summary>
        static readonly Dictionary<string, string> Suffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "sv", "sv" }, { "dpd", "dpd" }, { "tg", "tg" }, { "btn", "btn" }, { "input", "input" },
            { "bg", "bg" }, { "label", "label" }, { "dpdicon", "arrow" }, { "mark", "checkmark" },
            { "viewport", "viewport" }, { "content", "content" }, { "item", "item" },
            { "placeholder", "placeholder" }, { "text", "text" },
            { "vbarbg", "vbarbg" }, { "vbar", "vbar" }, { "hbarbg", "hbarbg" }, { "hbar", "hbar" },
            { "img", "" },
        };

        /// <summary>拆出基础名称，标记以 <c>key</c> 或 <c>key:arg</c> 形式写入 markers。</summary>
        public static string Parse(string rawName, List<string> markers)
        {
            markers.Clear();
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            var parts = rawName.Split('@');
            string baseName = StripSuffixes(parts[0].Trim(), markers);
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;

                int colon = part.IndexOfAny(new[] { ':', '：' });
                string key = colon >= 0 ? part.Substring(0, colon).Trim() : part;
                string arg = colon >= 0 ? part.Substring(colon + 1).Trim() : null;
                if (Aliases.TryGetValue(key, out var canonical)) key = canonical;

                markers.Add(arg == null ? key : $"{key}:{arg}");
            }
            return baseName;
        }

        /// <summary>从右往左剥掉已知后缀，例如 <c>Arrow.img.dpdicon</c> → Arrow，并记下 arrow。</summary>
        static string StripSuffixes(string name, List<string> markers)
        {
            var found = new List<string>();
            while (true)
            {
                int dot = name.LastIndexOf('.');
                if (dot <= 0 || dot >= name.Length - 1) break;
                string token = name.Substring(dot + 1);
                if (!Suffixes.TryGetValue(token, out var canonical)) break;
                if (!string.IsNullOrEmpty(canonical)) found.Add(canonical);
                name = name.Substring(0, dot).Trim();
            }
            found.Reverse();
            markers.AddRange(found);
            return name;
        }

        public static string KeyOf(string marker)
        {
            int colon = marker.IndexOf(':');
            return colon >= 0 ? marker.Substring(0, colon) : marker;
        }

        public static bool Has(List<string> markers, string key) => TryGetArg(markers, key, out _);

        public static bool TryGetArg(List<string> markers, string key, out string arg)
        {
            foreach (var m in markers)
            {
                if (!string.Equals(KeyOf(m), key, StringComparison.OrdinalIgnoreCase)) continue;
                int colon = m.IndexOf(':');
                arg = colon >= 0 ? m.Substring(colon + 1) : null;
                return true;
            }
            arg = null;
            return false;
        }
    }
}
