using UnityEngine;

namespace PsdTool
{
    /// <summary>把 RectTransform 贴到 Screen.safeArea。挂在全屏 SafeArea 容器上。</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect _last;
        Vector2Int _lastRes;
        RectTransform _rt;

        void OnEnable()
        {
            _rt = (RectTransform)transform;
            Apply(true);
        }

        void Update() => Apply(false);

        void Apply(bool force)
        {
            var area = Screen.safeArea;
            var res = new Vector2Int(Screen.width, Screen.height);
            if (!force && area == _last && res == _lastRes) return;
            _last = area;
            _lastRes = res;

            if (Screen.width <= 0 || Screen.height <= 0) return;
            _rt.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            _rt.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
