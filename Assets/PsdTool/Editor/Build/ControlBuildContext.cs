using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

namespace PsdTool
{
    internal sealed class ControlBuildContext
    {
        public readonly PsdTree Tree;
        public readonly PsdToolSettings Settings;
        public readonly Dictionary<int, List<PsdNode>> Children = new Dictionary<int, List<PsdNode>>();
        public readonly Dictionary<int, Rect?> Rects = new Dictionary<int, Rect?>();
        public readonly Dictionary<int, GameObject> Map = new Dictionary<int, GameObject>();
        // Part bindings can replace Map entries; keep control ownership separately
        // so a validation error always reaches the per-control fallback.
        public readonly Dictionary<GameObject, int> ControlOwners = new Dictionary<GameObject, int>();
        public readonly HashSet<string> MissingFonts = new HashSet<string>();
        public readonly HashSet<int> DropdownTemplates = new HashSet<int>();
        public readonly HashSet<int> DropdownItems = new HashSet<int>();

        public ControlBuildContext(PsdTree tree, PsdToolSettings settings)
        {
            Tree = tree;
            Settings = settings;
            foreach (var n in tree.nodes)
            {
                if (n.status == NodeStatus.PendingDelete) continue;
                if (!Children.TryGetValue(n.parentId, out var list)) Children[n.parentId] = list = new List<PsdNode>();
                list.Add(n);
            }
            // PSD 可把下拉列表项放在列表组的旁边；生成结构中收进该列表。
            foreach (var n in tree.nodes)
            {
                if (n.templateId != "Dropdown") continue;
                var siblings = ChildrenOf(n.layerId);
                var explicitTemplates = siblings.FindAll(c => c.slot == "template");
                var candidates = explicitTemplates.Count > 0 ? explicitTemplates : siblings.FindAll(c => c.templateId == "ScrollView");
                if (candidates.Count > 1) throw new ControlBuildException(n.layerId, "存在多个 template 候选，请只指定一个展开列表。");
                var template = candidates.Count == 1 ? candidates[0] : null;
                if (template == null) continue;
                DropdownTemplates.Add(template.layerId);
                var items = siblings.FindAll(c => c != template &&
                    (c.slot == "item" || c.templateId == "Toggle" || c.templateId == "Tab"));
                if (!Children.TryGetValue(template.layerId, out var content))
                    Children[template.layerId] = content = new List<PsdNode>();
                foreach (var item in items) { siblings.Remove(item); content.Add(item); }
            }
            foreach (int templateId in DropdownTemplates)
                foreach (var item in ScrollViewBuilder.Describe(this, tree.Find(templateId)).items) DropdownItems.Add(item.layerId);
            foreach (var list in Children.Values) list.Sort((a, b) =>
            {
                int order = a.order.CompareTo(b.order);
                return order != 0 ? order : a.layerId.CompareTo(b.layerId);
            });
            foreach (var node in tree.nodes)
            {
                if (node.status == NodeStatus.PendingDelete) continue;
                string[] roles;
                switch (node.templateId)
                {
                    case "ScrollView": roles = new[] { "bg", "viewport", "content", "vbar", "vbarbg", "hbar", "hbarbg" }; break;
                    case "Dropdown": roles = new[] { "bg", "caption", "template" }; break;
                    case "InputField": roles = new[] { "bg", "placeholder", "text" }; break;
                    case "Toggle": case "Tab": roles = new[] { "bg", "checkmark", "on" }; break;
                    case "Slider": case "Progress": roles = new[] { "track", "fill", "handle" }; break;
                    default: continue;
                }
                foreach (string role in roles)
                    if (ChildrenOf(node.layerId).Count(c => c.slot == role) > 1)
                        throw new ControlBuildException(node.layerId, "存在多个 " + role + " 插槽，请在节点面板明确唯一节点。");
            }
        }

        public List<PsdNode> ChildrenOf(int id) => Children.TryGetValue(id, out var list) ? list : new List<PsdNode>();
    }
}
