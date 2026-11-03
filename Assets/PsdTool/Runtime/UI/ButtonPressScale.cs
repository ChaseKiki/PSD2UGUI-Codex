using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PsdTool
{
    /// <summary>旧 Prefab 引用兼容。新生成按钮不再添加此组件，改用原生 ColorTint。</summary>
    [AddComponentMenu("")]
    public sealed class ButtonPressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Range(0.5f, 1f)] public float pressedScale = 0.95f;
        public float duration = 0.1f;

        Vector3 _normal = Vector3.one;
        Coroutine _tween;

        void Awake() => _normal = transform.localScale;

        public void OnPointerDown(PointerEventData eventData) => Animate(Vector3.one * pressedScale);

        public void OnPointerUp(PointerEventData eventData) => Animate(_normal);

        public void OnPointerExit(PointerEventData eventData) => Animate(_normal);

        void Animate(Vector3 target)
        {
            if (_tween != null) StopCoroutine(_tween);
            _tween = StartCoroutine(Tween(target));
        }

        IEnumerator Tween(Vector3 target)
        {
            Vector3 from = transform.localScale;
            if (duration <= 0f)
            {
                transform.localScale = target;
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.LerpUnclamped(from, target, Mathf.Clamp01(t / duration));
                yield return null;
            }
            transform.localScale = target;
        }
    }
}
