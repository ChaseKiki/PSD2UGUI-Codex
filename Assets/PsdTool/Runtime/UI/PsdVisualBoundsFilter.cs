using UnityEngine;

namespace PsdTool
{
    /// <summary>FX 视觉可越出逻辑矩形，但射线仍限于原矩形。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PsdVisualBoundsFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) =>
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screenPoint, eventCamera);
    }
}
