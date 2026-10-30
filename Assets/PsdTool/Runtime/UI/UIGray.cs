using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    /// <summary>置灰：把子树里 Image / Text 的颜色乘以灰色系数。无独立灰图、无额外材质实例。</summary>
    public sealed class UIGray : MonoBehaviour
    {
        [Range(0f, 1f)] public float amount = 1f;
        public Color gray = new Color(0.55f, 0.55f, 0.55f, 1f);

        bool _applied;
        Graphic[] _graphics;
        Color[] _originals;

        public void SetGray(bool grayed)
        {
            Cache();
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] == null) continue;
                _graphics[i].color = grayed
                    ? Color.Lerp(_originals[i], new Color(gray.r, gray.g, gray.b, _originals[i].a), amount)
                    : _originals[i];
            }
            _applied = grayed;
        }

        public bool IsGray => _applied;

        void Cache()
        {
            if (_graphics != null) return;
            _graphics = GetComponentsInChildren<Graphic>(true);
            _originals = new Color[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++)
                _originals[i] = _graphics[i] != null ? _graphics[i].color : Color.white;
        }

        void OnDisable()
        {
            if (_applied) SetGray(false);
        }
    }
}
