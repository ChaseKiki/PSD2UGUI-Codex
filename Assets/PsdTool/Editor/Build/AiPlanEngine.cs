using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PsdTool
{
    internal static class AiPlanEngine
    {
        internal static bool Structural(AiControlProposal plan) => (plan.groups?.Count ?? 0) > 0 || (plan.nodes?.Any(n => n.setParent) ?? false);

        internal static PsdTree Preview(PsdTree tree, PsdToolSettings settings, IEnumerable<AiControlProposal> proposals)
        {
            var copy = Object.Instantiate(tree);
            try
            {
                foreach (var proposal in proposals)
                {
                    if (!Validate(copy, settings, proposal, out _)) continue;
                    Edit(copy, settings, proposal, false);
                }
                return copy;
            }
            catch { Object.DestroyImmediate(copy); throw; }
        }

        public static bool Validate(PsdTree tree, PsdToolSettings settings, AiControlProposal proposal, out string error)
        {
            var copy = Object.Instantiate(tree);
            try { Edit(copy, settings, proposal, false); CheckBuild(copy, settings, proposal); error = null; return true; }
            catch (Exception e) { error = e.Message; return false; }
            finally { Object.DestroyImmediate(copy); }
        }
        public static void Apply(PsdTree tree, PsdToolSettings settings, IEnumerable<AiControlProposal> proposals, bool confirmed)
        {
            var copy = Object.Instantiate(tree);
            var originalNodes = tree.nodes;
            bool committed = false;
            try
            {
                var touched = new HashSet<int>();
                foreach (var proposal in proposals)
                {
                    foreach (var node in proposal.nodes ?? new List<AiNodeEdit>())
                        if (!touched.Add(node.layerId)) throw new InvalidOperationException("多个控件计划重复修改图层 " + node.layerId + "，请分开检查。");
                    Edit(copy, settings, proposal, confirmed); CheckBuild(copy, settings, proposal);
                }
                Undo.RegisterCompleteObjectUndo(tree, "AI 识别控件与分组");
                tree.nodes = copy.nodes;
                committed = true;
                EditorUtility.SetDirty(tree); AssetDatabase.SaveAssetIfDirty(tree);
            }
            catch
            {
                if (committed) { tree.nodes = originalNodes; EditorUtility.SetDirty(tree); }
                throw;
            }
            finally { Object.DestroyImmediate(copy); }
        }
        static void Edit(PsdTree tree, PsdToolSettings settings, AiControlProposal proposal, bool confirmed)
        {
            if (proposal == null || float.IsNaN(proposal.confidence) || float.IsInfinity(proposal.confidence) || proposal.confidence < 0 || proposal.confidence > 1)
                throw new InvalidOperationException("AI 置信度无效。");
            var edits = proposal.nodes ?? new List<AiNodeEdit>(); var groups = proposal.groups ?? new List<AiGroupEdit>();
            var opacity = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete).ToDictionary(n => n.layerId, n => ParentOpacity(tree, n));
            var inheritance = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete).ToDictionary(n => n.layerId, n => VisualAncestors(tree, n));
            if (edits.Count + groups.Count == 0 || edits.Count > 256 || groups.Count > 64) throw new InvalidOperationException("控件计划为空或超过数量限制。");
            if (!settings.aiAllowStructure && (groups.Count > 0 || edits.Any(n => n.setParent))) throw new InvalidOperationException("当前配置未允许 AI 提出结构调整。");
            if (edits.Any(n => n == null) || groups.Any(n => n == null)) throw new InvalidOperationException("计划包含空节点。");
            if (edits.Select(n => n.layerId).Distinct().Count() != edits.Count || groups.Select(g => g.groupId).Distinct().Count() != groups.Count) throw new InvalidOperationException("同一计划重复声明节点或分组。");
            var map = new Dictionary<int, int>();
            foreach (var group in groups)
            {
                if (group.groupId >= -1 || tree.Find(group.groupId) != null) throw new InvalidOperationException("新分组必须使用未占用的负数 groupId（-1 是根）。");
                if (!AiProtocol.Template(group.templateId) || string.IsNullOrEmpty(group.templateId)) throw new InvalidOperationException("新分组的控件模板无效。");
                if (!string.IsNullOrEmpty(group.slot) && !Markers.Slots.Contains(group.slot)) throw new InvalidOperationException("新分组插槽无效。");
                var members = edits.Where(e => Within(e.setParent ? e.parentId : tree.Find(e.layerId)?.parentId ?? -1, group.groupId, groups))
                    .Select(e => tree.Find(e.layerId)?.stableKey ?? "layer:" + e.layerId).OrderBy(k => k).ToArray();
                if (members.Length == 0) throw new InvalidOperationException("新分组没有任何来源图层，不创建空控件。");
                string key = "ai-group:" + AiProtocol.Hash(group.templateId + "/" + group.slot + "/" + string.Join(",", members));
                var existing = tree.nodes.Find(n => n.stableKey == key && n.aiGeneratedGroup);
                if (existing != null)
                {
                    if (AiProtocol.Protected(existing) && !(proposal.manualOverride?.Contains(existing.layerId) ?? false)) throw new InvalidOperationException("AI 分组已被人工确认，请在后续计划中直接引用现有节点。");
                    if (existing.locks != PropGroup.None) throw new InvalidOperationException("AI 分组存在属性锁，请先明确解锁：" + existing.goName);
                    map[group.groupId] = existing.layerId; continue;
                }
                int id = -1000000 - (int)(Convert.ToUInt32(key.Substring(9, 8), 16) & 0x3fffffff);
                while (tree.Find(id) != null) id--;
                map[group.groupId] = id;
                tree.nodes.Add(new PsdNode { layerId = id, stableKey = key, kind = LayerKind.Group, aiGeneratedGroup = true,
                    goName = GeneratedGroupName(group, key),
                    psdName = "AI Group", status = NodeStatus.New, source = confirmed ? DecisionSource.Manual : DecisionSource.AI, confidence = proposal.confidence });
            }
            foreach (var group in groups)
            {
                var node = tree.Find(map[group.groupId]);
                node.parentId = Resolve(group.parentId, map); node.aiParentId = node.parentId; node.aiHasParentOverride = true;
                node.templateId = group.templateId; node.slot = group.slot ?? "";
                if (group.bounds != null && group.bounds.Length > 0)
                {
                    if (group.slot != "viewport" || group.bounds.Length != 4 || group.bounds[0] < 0 || group.bounds[1] < 0 || group.bounds[2] <= 0 || group.bounds[3] <= 0 ||
                        (long)group.bounds[0] + group.bounds[2] > tree.docSize.x || (long)group.bounds[1] + group.bounds[3] > tree.docSize.y)
                        throw new InvalidOperationException("只有 viewport 组可提供 PSD 画布内的有效裁剪边界。");
                    node.aiBoundsOverride = true; node.psdRect = new RectInt(group.bounds[0], group.bounds[1], group.bounds[2], group.bounds[3]);
                }
            }
            foreach (var edit in edits)
            {
                var node = tree.Find(edit.layerId);
                if (node == null || node.status == NodeStatus.PendingDelete) throw new InvalidOperationException("AI 引用了不存在或待删除的图层 " + edit.layerId);
                if (!edit.setTemplate && !edit.setSlot && !edit.setParent) throw new InvalidOperationException("AI 节点操作没有指定要修改的字段。");
                int parent = edit.setParent ? Resolve(edit.parentId, map) : node.parentId;
                bool changes = edit.setTemplate && node.templateId != (edit.templateId ?? "") || edit.setSlot && node.slot != (edit.slot ?? "") || edit.setParent && node.parentId != parent;
                if (changes && AiProtocol.Protected(node) && !(proposal.manualOverride?.Contains(node.layerId) ?? false)) throw new InvalidOperationException("“" + node.goName + "”由人工或 PSD 标记确定，AI 不会覆盖。");
                if (edit.setTemplate)
                {
                    if (!AiProtocol.Template(edit.templateId)) throw new InvalidOperationException("未知控件模板：" + edit.templateId);
                    if (edit.templateId == "Group" && node.kind != LayerKind.Group)
                        throw new InvalidOperationException("图片和文字不能改成 Group；请保留图层并创建虚拟父组。");
                    if ((node.locks & PropGroup.Component) != 0 && node.templateId != edit.templateId) throw new InvalidOperationException("控件组件组已锁定：" + node.goName);
                    if (node.kind == LayerKind.Text && !string.IsNullOrEmpty(edit.templateId)) throw new InvalidOperationException("文字图层只能作为插槽，不能直接变成控件根。");
                    if ((edit.templateId == "InputField" || edit.templateId == "Dropdown" || edit.templateId == "ScrollView" || edit.templateId == "ToggleGroup") && node.kind != LayerKind.Group)
                        throw new InvalidOperationException("复合控件需以组为根；请创建虚拟组并保留原图片为子节点。");
                    node.templateId = edit.templateId ?? "";
                }
                if (edit.setSlot)
                {
                    if (!string.IsNullOrEmpty(edit.slot) && !Markers.Slots.Contains(edit.slot)) throw new InvalidOperationException("未知插槽：" + edit.slot);
                    node.slot = edit.slot ?? "";
                }
                if (edit.setParent && parent != node.parentId)
                {
                    if ((node.locks & PropGroup.Parent) != 0) throw new InvalidOperationException("父节点属性已锁定：" + node.goName);
                    node.parentId = parent; node.aiHasParentOverride = true; node.aiParentId = parent;
                }
                if (changes) { node.source = confirmed ? DecisionSource.Manual : DecisionSource.AI; node.confidence = proposal.confidence; }
            }
            CheckParents(tree);
            foreach (var node in tree.nodes.Where(n => n.status != NodeStatus.PendingDelete))
            {
                if (opacity.TryGetValue(node.layerId, out float before) && !Mathf.Approximately(before, ParentOpacity(tree, node)))
                    throw new InvalidOperationException("结构调整会改变组透明度的继承：" + node.goName + "，请保留原透明度分组。");
                if (inheritance.TryGetValue(node.layerId, out string prior) && prior != VisualAncestors(tree, node))
                    throw new InvalidOperationException("结构调整会改变隐藏状态、遮罩/剪贴或组效果继承：" + node.goName);
            }
            foreach (var group in groups)
            {
                var node = tree.Find(map[group.groupId]);
                var children = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete && n.parentId == node.layerId).ToList();
                if (children.Count == 0) throw new InvalidOperationException("分组没有子图层。");
                node.order = children.Min(n => n.order);
            }
        }
        internal static string GeneratedGroupName(AiGroupEdit group, string stableKey)
        {
            string name = (group.name ?? "").Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9 _-]{0,63}\z")) return name;
            string role;
            switch (group.slot)
            {
                case "viewport": role = "Viewport"; break;
                case "content": role = "Content"; break;
                case "template": role = "Template"; break;
                case "item": role = "Item"; break;
                default: role = group.templateId; break;
            }
            return (string.IsNullOrEmpty(role) ? "Group" : role) + "_" + stableKey.Substring(9, 8);
        }

        static bool Within(int parent, int group, List<AiGroupEdit> groups)
        {
            var visited = new HashSet<int>();
            while (visited.Add(parent)) { if (parent == group) return true; var next = groups.Find(g => g.groupId == parent); if (next == null) return false; parent = next.parentId; }
            throw new InvalidOperationException("虚拟分组有循环引用。");
        }
        static int Resolve(int id, Dictionary<int, int> map) => map.TryGetValue(id, out int mapped) ? mapped : id;
        static float ParentOpacity(PsdTree tree, PsdNode node)
        {
            float alpha = 1; var seen = new HashSet<int>();
            for (var parent = tree.Find(node.parentId); parent != null && seen.Add(parent.layerId); parent = tree.Find(parent.parentId)) alpha *= parent.opacity;
            return alpha;
        }
        static string VisualAncestors(PsdTree tree, PsdNode node)
        {
            var ids = new List<int>(); var seen = new HashSet<int>();
            for (var p = tree.Find(node.parentId); p != null && seen.Add(p.layerId); p = tree.Find(p.parentId))
                if (!p.visible || p.clipping || p.hasEffects || p.templateId == "Mask") ids.Add(p.layerId);
            // 剪贴图层依赖相邻的底层，不跨父组移动。
            if (node.clipping) ids.Add(node.parentId);
            return string.Join(",", ids);
        }
        internal static void CheckParents(PsdTree tree)
        {
            var active = tree.nodes.Where(n => n.status != NodeStatus.PendingDelete).ToDictionary(n => n.layerId);
            foreach (var node in active.Values)
            {
                var seen = new HashSet<int> { node.layerId }; int parent = node.parentId;
                while (parent != -1)
                {
                    if (!active.TryGetValue(parent, out var group) || group.kind != LayerKind.Group) throw new InvalidOperationException("父节点不存在或不是组：" + node.goName);
                    if (!seen.Add(parent)) throw new InvalidOperationException("结构调整会产生父子循环：" + node.goName);
                    parent = group.parentId;
                }
            }
        }
        static void CheckBuild(PsdTree tree, PsdToolSettings settings, AiControlProposal proposal)
        {
            var validation = Object.Instantiate(tree);
            try
            {
                var affected = new HashSet<int>(proposal.nodes.Select(n => n.layerId));
                // 新组必有来源成员；下方沿这些成员向上收集本方案的控件祖先。
                // 不把历史所有 AI 组纳入，否则一个无关坏组会拖累其它区域。
                foreach (int id in affected.ToArray())
                    for (var parent = tree.Find(id); parent != null; parent = tree.Find(parent.parentId))
                        if (PrefabGenerator.IsControl(parent.templateId)) affected.Add(parent.layerId);
                bool Relevant(PsdNode n)
                {
                    for (var current = n; current != null; current = tree.Find(current.parentId)) if (affected.Contains(current.layerId)) return true;
                    return false;
                }
                foreach (var n in validation.nodes.Where(n => PrefabGenerator.IsControl(n.templateId) && !Relevant(n))) n.templateId = n.kind == LayerKind.Group ? "Group" : "";
                // 这里只验证结构和引用，不为尚未确认的计划创建字体效果材质资产。
                foreach (var n in validation.nodes.Where(n => n.kind == LayerKind.Text && n.text != null))
                { if (n.text.outline != null) n.text.outline.enabled = false; if (n.text.shadow != null) n.text.shadow.enabled = false; }
                // 首次识别发生在切图前，仅为几何/引用校验补图片占位，不写入真实节点。
                foreach (var n in validation.nodes.Where(n => n.kind != LayerKind.Group && n.kind != LayerKind.Text))
                    if (n.image.trimmedRect.width <= 0 && n.psdRect.width > 0)
                    { n.image.trimmedRect = n.psdRect; n.image.logicalRect = n.psdRect; n.image.isEmpty = false; n.image.isSolid = true; n.image.solidColor = Color.white; }
                using (var draft = PrefabGenerator.BuildDraft(validation, settings))
                {
                    foreach (var dropdown in draft.Root.GetComponentsInChildren<TMP_Dropdown>(true))
                        if (!dropdown.interactable || dropdown.template == null || dropdown.itemText == null && dropdown.itemImage == null)
                            throw new InvalidOperationException("下拉识别缺少可用的展开层或选项模板，未应用计划。");
                    foreach (var toggle in draft.Root.GetComponentsInChildren<Toggle>(true))
                        if (toggle.graphic == null && tree.nodes.Any(n => (n.stableKey ?? "layer:" + n.layerId) == toggle.GetComponent<PsdGeneratedNode>()?.key && n.templateId == "Toggle"))
                            throw new InvalidOperationException("Toggle 缺少 checkmark/on 勾选图：" + toggle.name);
                    foreach (var slider in draft.Root.GetComponentsInChildren<Slider>(true))
                        if (slider.fillRect == null || slider.handleRect == null || slider.targetGraphic == null)
                            throw new InvalidOperationException("Slider 缺少 fill/handle/targetGraphic，不能标记为可应用。");
                    foreach (var input in draft.Root.GetComponentsInChildren<TMP_InputField>(true))
                        if (input.textComponent == null || input.textViewport == null || input.targetGraphic == null)
                            throw new InvalidOperationException("InputField 缺少输入文字/Viewport/点击背景。");
                    foreach (var progress in validation.nodes.Where(n => n.templateId == "Progress" && Relevant(n)))
                    {
                        var slots = validation.nodes.Where(n => n.parentId == progress.layerId).Select(n => n.slot).ToArray();
                        if (!slots.Contains("fill") || progress.progressFill < ProgressFillMode.Radial360 && !slots.Contains("track")) throw new InvalidOperationException("Progress 缺少 track/fill，不能标记为完整控件。");
                        var track = validation.nodes.Find(n => n.parentId == progress.layerId && n.slot == "track");
                        var rect = track != null ? AiRegions.Bounds(validation, track) : new Rect();
                        if (progress.progressFill == ProgressFillMode.Auto && Mathf.Max(rect.width, rect.height) < Mathf.Min(rect.width, rect.height) * 2)
                            throw new InvalidOperationException("暂不支持环形进度条绑定，保留视觉节点。");
                    }
                }
            }
            finally { Object.DestroyImmediate(validation); }
        }
    }
}
