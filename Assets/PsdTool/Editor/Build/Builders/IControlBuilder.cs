using UnityEngine;

namespace PsdTool
{
    internal interface IControlBuilder
    {
        void Build(ControlBuildContext context, PsdNode node, GameObject root, Rect bounds);
    }
}
