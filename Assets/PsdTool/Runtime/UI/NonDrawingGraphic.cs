using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    /// <summary>只参与射线检测、不绘制的 Graphic。用于扩大按钮热区，避免 alpha=0 的 Image 产生过度绘制。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class NonDrawingGraphic : Graphic
    {
        public override void SetMaterialDirty() { }
        public override void SetVerticesDirty() { }

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }
}
