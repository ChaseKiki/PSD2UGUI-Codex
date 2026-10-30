using UnityEngine;

namespace PsdTool
{
    public static class AnchorUtil
    {
        public static readonly string[] Presets =
        {
            "", "tl", "t", "tr", "l", "c", "r", "bl", "b", "br", "stretch", "hstretch", "vstretch", "topbar", "bottombar",
        };

        public static readonly string[] PresetLabels =
        {
            "居中", "左上", "上中", "右上", "左中", "中心", "右中", "左下", "下中", "右下",
            "四边拉伸", "水平拉伸", "垂直拉伸", "顶栏", "底栏",
        };

        public static void Apply(RectTransform rt, Rect r, Rect parent, string preset, float snap)
        {
            // 空预设 = 居中。不按贴边推断，未在节点树里改锚点时位置与 PSD 一致。
            if (string.IsNullOrEmpty(preset))
                preset = "c";

            Get(preset, out Vector2 min, out Vector2 max, out Vector2 pivot);
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;

            float parentW = Mathf.Max(1e-3f, parent.width);
            float parentH = Mathf.Max(1e-3f, parent.height);

            if (min == max)
            {
                rt.sizeDelta = r.size;
                float ax = parent.xMin + parentW * min.x;
                float ayTop = parent.yMin + parentH * (1f - min.y);
                rt.anchoredPosition = new Vector2(r.center.x - ax, ayTop - r.center.y);
                return;
            }

            float left = r.xMin - parent.xMin;
            float right = parent.xMax - r.xMax;
            float top = r.yMin - parent.yMin;
            float bottom = parent.yMax - r.yMax;

            if (Mathf.Approximately(min.x, 0f) && Mathf.Approximately(max.x, 1f))
            {
                rt.offsetMin = new Vector2(left, rt.offsetMin.y);
                rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
            }
            else
            {
                rt.sizeDelta = new Vector2(r.width, rt.sizeDelta.y);
                float ax = parent.xMin + parentW * min.x;
                rt.anchoredPosition = new Vector2(r.center.x - ax, rt.anchoredPosition.y);
            }

            if (Mathf.Approximately(min.y, 0f) && Mathf.Approximately(max.y, 1f))
            {
                rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
                rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
            }
            else
            {
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, r.height);
                float ayTop = parent.yMin + parentH * (1f - min.y);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, ayTop - r.center.y);
            }
        }

        public static string Infer(Rect r, Rect parent, float snap)
        {
            bool left = r.xMin - parent.xMin <= snap;
            bool right = parent.xMax - r.xMax <= snap;
            bool top = r.yMin - parent.yMin <= snap;
            bool bottom = parent.yMax - r.yMax <= snap;
            bool fullW = left && right && r.width >= parent.width * 0.9f;
            bool fullH = top && bottom && r.height >= parent.height * 0.9f;

            if (fullW && fullH) return "stretch";
            if (fullW && top && !bottom) return "topbar";
            if (fullW && bottom && !top) return "bottombar";
            if (fullH && left && !right) return "vstretch";
            if (fullH && right && !left) return "vstretch";
            if (fullW) return "hstretch";
            if (fullH) return "vstretch";

            if (top && left) return "tl";
            if (top && right) return "tr";
            if (bottom && left) return "bl";
            if (bottom && right) return "br";
            if (top) return "t";
            if (bottom) return "b";
            if (left) return "l";
            if (right) return "r";
            return "c";
        }

        /// <summary>顶部或底部栏：靠近文档上下边缘且宽度接近全屏。</summary>
        public static bool LooksLikeSafeBar(Rect r, Vector2Int doc, float snap)
        {
            bool fullW = r.width >= doc.x * 0.85f;
            bool top = r.yMin <= snap;
            bool bottom = doc.y - r.yMax <= snap;
            return fullW && (top || bottom);
        }

        static void Get(string preset, out Vector2 min, out Vector2 max, out Vector2 pivot)
        {
            switch (preset)
            {
                case "tl": min = max = pivot = new Vector2(0f, 1f); break;
                case "t": min = max = pivot = new Vector2(0.5f, 1f); break;
                case "tr": min = max = pivot = new Vector2(1f, 1f); break;
                case "l": min = max = pivot = new Vector2(0f, 0.5f); break;
                case "r": min = max = pivot = new Vector2(1f, 0.5f); break;
                case "bl": min = max = pivot = new Vector2(0f, 0f); break;
                case "b": min = max = pivot = new Vector2(0.5f, 0f); break;
                case "br": min = max = pivot = new Vector2(1f, 0f); break;
                case "stretch":
                    min = Vector2.zero; max = Vector2.one; pivot = new Vector2(0.5f, 0.5f);
                    break;
                case "hstretch":
                    min = new Vector2(0f, 0.5f); max = new Vector2(1f, 0.5f); pivot = new Vector2(0.5f, 0.5f);
                    break;
                case "vstretch":
                    min = new Vector2(0.5f, 0f); max = new Vector2(0.5f, 1f); pivot = new Vector2(0.5f, 0.5f);
                    break;
                case "topbar":
                    min = new Vector2(0f, 1f); max = new Vector2(1f, 1f); pivot = new Vector2(0.5f, 1f);
                    break;
                case "bottombar":
                    min = new Vector2(0f, 0f); max = new Vector2(1f, 0f); pivot = new Vector2(0.5f, 0f);
                    break;
                default:
                    min = max = pivot = new Vector2(0.5f, 0.5f);
                    break;
            }
        }
    }
}
