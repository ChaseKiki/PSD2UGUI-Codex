using System.Collections.Generic;
using UnityEngine;

namespace PsdTool
{
    internal static class ControlBuilderRegistry
    {
        static readonly Dictionary<string, IControlBuilder> Builders = new Dictionary<string, IControlBuilder>
        {
            ["Button"] = new ButtonBuilder(),
            ["Toggle"] = new ToggleBuilder(), ["Tab"] = new ToggleBuilder(),
            ["ToggleGroup"] = new ToggleGroupBuilder(),
            ["Slider"] = new SliderBuilder(), ["Progress"] = new SliderBuilder(),
            ["Scrollbar"] = new ScrollbarBuilder(), ["ScrollView"] = new ScrollViewBuilder(),
            ["Dropdown"] = new DropdownBuilder(), ["InputField"] = new InputFieldBuilder(),
            ["Mask"] = new MaskBuilder()
        };

        public static bool TryBuild(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            if (ctx.DropdownTemplates.Contains(node.layerId))
            {
                Builders["ScrollView"].Build(ctx, node, go, bounds);
                return true;
            }
            if (string.IsNullOrEmpty(node.templateId) || !Builders.TryGetValue(node.templateId, out var builder)) return false;
            builder.Build(ctx, node, go, bounds);
            return true;
        }
    }
}
