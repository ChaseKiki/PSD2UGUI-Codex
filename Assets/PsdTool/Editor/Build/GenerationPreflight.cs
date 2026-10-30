using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PsdTool
{
    internal sealed class ControlBuildException : InvalidOperationException
    {
        internal readonly int layerId;
        internal ControlBuildException(int layerId, string message) : base(message) { this.layerId = layerId; }
    }
    [Serializable] internal sealed class GenerationIssue
    {
        public int layerId;
        public bool placeholder;
        public string nodePath, type, message;
        public string resolution = "本次保留外观，暂不更新该控件交互；请定位并确认控件类型与部件插槽。已有人工/历史组件不会自动删除，需单独验收。";
    }
    internal static class GenerationPreflight
    {
        internal static List<GenerationIssue> Inspect(PsdTree tree, PsdToolSettings settings)
        {
            var issues = new List<GenerationIssue>();
            using (var draft = Build(tree, settings, issues, true)) { }
            return issues;
        }
        // Only diagnosed control failures are recoverable here. Source/IO/asset errors still propagate.
        // Work on a clone: fallback must never erase source decisions, locks or manual edits.
        internal static PrefabGenerator.Draft Build(PsdTree tree, PsdToolSettings settings, List<GenerationIssue> issues, bool inspect = false)
        {
            AiPlanEngine.CheckParents(tree);
            var copy = Object.Instantiate(tree);
            var found = issues ?? new List<GenerationIssue>();
            var disabled = new HashSet<int>();
            try
            {
                if (inspect) PrepareImages(copy);
                for (int attempt = 0; attempt <= tree.nodes.Count; attempt++)
                {
                    try
                    {
                        var draft = PrefabGenerator.BuildDraft(copy, settings);
                        foreach (var repair in draft.Repairs)
                        {
                            repair.nodePath = PathOf(tree, repair.layerId);
                            found.Add(repair);
                        }
                        string source = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tree));
                        if (!string.IsNullOrEmpty(source))
                            foreach (var id in draft.Root.GetComponentsInChildren<PsdGeneratedNode>(true)) id.sourceGuid = source;
                        if (!inspect)
                        {
                            foreach (string warning in copy.warnings) if (!tree.warnings.Contains(warning)) tree.warnings.Add(warning);
                            foreach (var issue in found)
                            {
                                string warning = "[交互待处理] " + issue.nodePath + " [" + issue.type + "]：" + issue.message + "；" + issue.resolution;
                                if (!tree.warnings.Contains(warning)) tree.warnings.Add(warning);
                            }
                        }
                        return draft;
                    }
                    catch (ControlBuildException e)
                    {
                        var node = copy.Find(e.layerId);
                        if (node == null || !PrefabGenerator.IsControl(node.templateId) || !disabled.Add(node.layerId)) throw;
                        found.Add(new GenerationIssue { layerId = node.layerId, nodePath = PathOf(tree, node.layerId), type = node.templateId, message = e.Message });
                        node.templateId = node.kind == LayerKind.Group ? "Group" : "";
                        // Helpers are rebuilt as source visuals. Retain usable nested controls.
                        foreach (var child in copy.nodes.Where(n => n.parentId == node.layerId))
                            if (child.slot == "rect" || child.slot == "pressed" || child.slot == "disabled") child.slot = "";
                    }
                }
                throw new InvalidOperationException("生成预检未收敛，请查看控件校验报告。");
            }
            finally { Object.DestroyImmediate(copy); }
        }
        internal static void PrepareImages(PsdTree copy)
        {
            foreach (var n in copy.nodes)
            {
                if (n.kind == LayerKind.Text && n.text != null)
                {
                    if (n.text.outline != null) n.text.outline.enabled = false;
                    if (n.text.shadow != null) n.text.shadow.enabled = false;
                }
                if (n.kind != LayerKind.Group && n.kind != LayerKind.Text && n.image.trimmedRect.width <= 0 && n.psdRect.width > 0)
                { n.image.trimmedRect = n.psdRect; n.image.logicalRect = n.psdRect; n.image.isEmpty = false; n.image.isSolid = true; n.image.solidColor = Color.white; }
            }
        }
        static string PathOf(PsdTree tree, int id)
        {
            var names = new List<string>(); var seen = new HashSet<int>();
            for (var node = tree.Find(id); node != null && seen.Add(node.layerId); node = tree.Find(node.parentId)) names.Add(node.goName);
            names.Reverse(); return string.Join("/", names);
        }
    }
}
