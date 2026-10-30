using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    /// <summary>
    /// UGUI Toggle 只能控制一个 graphic 显隐。本组件在值变化时同步多节点与文字颜色，
    /// 用于页签「选中/未选中两套样式」或「文字变色」。
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public sealed class ToggleVisualGroup : MonoBehaviour
    {
        public GameObject[] onObjects;
        public GameObject[] offObjects;
        public Graphic[] tintTargets;
        public Color onColor = Color.white;
        public Color offColor = Color.white;

        Toggle _toggle;

        void Awake()
        {
            _toggle = GetComponent<Toggle>();
            _toggle.onValueChanged.AddListener(Apply);
            Apply(_toggle.isOn);
        }

        void OnDestroy()
        {
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(Apply);
        }

        public void Apply(bool on)
        {
            if (onObjects != null)
                foreach (var go in onObjects)
                    if (go != null) go.SetActive(on);
            if (offObjects != null)
                foreach (var go in offObjects)
                    if (go != null) go.SetActive(!on);
            if (tintTargets != null)
            {
                var c = on ? onColor : offColor;
                foreach (var g in tintTargets)
                    if (g != null) g.color = c;
            }
        }
    }
}
