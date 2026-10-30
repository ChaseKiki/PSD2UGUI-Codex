using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    internal sealed class ToggleGroupBuilder : IControlBuilder
    {
        public void Build(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            go.AddComponent<ToggleGroup>().allowSwitchOff = false;
            PrefabGenerator.CreateVisualChildren(ctx, node, go, bounds);
        }
    }
}
