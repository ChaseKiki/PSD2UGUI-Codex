using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class AiProposalRow
    {
        public AiControlProposal proposal;
        public bool selected, applied;
        public string error;
    }
    internal sealed partial class AiReviewState
    {
        public PsdTree tree;
        public PsdToolSettings settings;
        public string signature, note, error, reportFolder;
        public bool externalTask;
        internal string sourcePsdHash;
        string CurrentSignature() => externalTask ? AiVisualInput.TaskSignature(tree, settings, sourcePsdHash) : AiVisualInput.Signature(tree, settings);
        public readonly List<AiProposalRow> rows = new List<AiProposalRow>();
        public readonly List<AiRegionResult> regions = new List<AiRegionResult>();
        public bool generated;
        internal readonly List<GenerationIssue> generationIssues = new List<GenerationIssue>();
        internal bool generationChecked;
        internal string preflightError;
        internal void RefreshGenerationCheck()
        {
            generationIssues.Clear(); preflightError = null;
            try
            {
                var preview = AiPlanEngine.Preview(tree, settings, rows.Where(r => r.selected && !r.applied && r.error == null && !IsIgnored(r)).Select(r => r.proposal));
                try { generationIssues.AddRange(GenerationPreflight.Inspect(preview, settings)); }
                finally { UnityEngine.Object.DestroyImmediate(preview); }
            }
            catch (Exception e) { preflightError = e.Message; }
            generationChecked = true;
        }
        internal void RefreshStatuses()
        {
            foreach (var item in regions.Where(r => r.proposalIndex >= 0 && r.proposalIndex < rows.Count))
            {
                if (item.status == AiRegionStatus.Ignored) continue;
                var row = rows[item.proposalIndex];
                item.status = row.applied ? (generated ? AiRegionStatus.Generated : AiRegionStatus.Applied) : row.error != null ? (row.error.StartsWith("暂不支持", StringComparison.Ordinal) ? AiRegionStatus.Unsupported : AiRegionStatus.Failed) : row.selected ? AiRegionStatus.Ready : AiRegionStatus.NeedsReview;
                item.message = row.error ?? (row.applied ? generated ? "Prefab 已生成，请验收交互" : "已应用标记，尚未生成" : "等待确认");
                if (generated && generationIssues.Any(i => i.layerId == item.rootId))
                    item.message = "Prefab 已生成；该控件暂保留外观，交互仍需处理";
            }
            foreach (var item in regions.Where(r => r.proposalIndex < 0 && r.status != AiRegionStatus.Ignored))
            {
                var owner = regions.FirstOrDefault(r => r.rootId != item.rootId && r.proposalIndex >= 0 && r.proposalIndex < rows.Count &&
                    !IsIgnored(rows[r.proposalIndex]) && rows[r.proposalIndex].error == null &&
                    rows[r.proposalIndex].proposal?.nodes?.Any(n => n.layerId == item.rootId &&
                        (r.rootId == item.ownerRootId || n.setTemplate && n.templateId == item.type)) == true);
                if (owner != null)
                {
                    item.ownerRootId = owner.rootId; item.status = AiRegionStatus.Covered;
                    item.message = rows[owner.proposalIndex].applied ? "父控件方案已应用，包含此节点的结构与绑定" : "由父控件方案统一绑定，请审核父控件项";
                }
                else if (item.status == AiRegionStatus.Covered && item.ownerRootId != -1)
                {
                    item.status = AiRegionStatus.Failed;
                    item.message = "父控件方案尚未通过校验或已忽略，请先处理父控件项。";
                }
            }
        }
        public void ApplySelected(bool confirmed, AiProposalRow only = null)
        {
            if (CurrentSignature() != signature)
                throw new InvalidOperationException("PSD、节点树或 AI 配置已变化，请重新识别，未应用旧结果。");
            var selected = rows.Where(r => (only != null ? r == only : r.selected) && !r.applied && !IsIgnored(r)).ToList();
            if (selected.Count == 0) return;
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("应用可用 AI 建议");
            try
            {
                foreach (var row in selected)
                {
                    if (row.proposal == null || row.error != null) { row.error = row.error ?? "空的控件方案，已忽略"; row.selected = false; continue; }
                    if (!AiPlanEngine.Validate(tree, settings, row.proposal, out row.error)) { row.selected = false; continue; }
                    try
                    {
                        AiPlanEngine.Apply(tree, settings, new[] { row.proposal }, confirmed);
                        row.applied = true; row.selected = false; row.error = null;
                    }
                    catch (InvalidOperationException e) { row.error = e.Message; row.selected = false; }
                }
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
            signature = CurrentSignature();
            foreach (var row in rows.Where(r => !r.applied))
            {
                if (IsIgnored(row)) { row.error = "人工已忽略"; row.selected = false; continue; }
                if (!AiPlanEngine.Validate(tree, settings, row.proposal, out row.error)) row.selected = false;
            }
            generated = false; generationChecked = false; SaveReport();
        }
        internal GameObject ApplyAndGenerate()
        {
            if (tree == null || settings == null) throw new InvalidOperationException("节点树或配置已丢失，请重新打开工具。");
            generated = false;
            ApplySelected(true);
            RefreshGenerationCheck();
            if (CurrentSignature() != signature) throw new InvalidOperationException("来源已变化，请重新识别。");
            try
            {
                EditorUtility.DisplayProgressBar("生成 Prefab", "应用已确认的识别结果并导出资源", 0.3f);
                var doc = Parser.PsdParser.Parse(tree.psdPath);
                SpriteExporter.Export(doc, tree, settings); AtlasBuilder.Update(settings, tree.moduleName, tree.panelName);
                var prefab = PrefabGenerator.Generate(tree, settings, generationIssues);
                EditorUtility.SetDirty(tree); AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(prefab);
                generated = true; error = null; SaveReport(); return prefab;
            }
            catch (Exception e) { error = e.Message; SaveReport(); throw; }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }

    /// <summary>Codex 区域分类 → 校验计划 → 可预览的标记/虚拟分组。不请求切图或修改 Prefab。</summary>
    public static class AiMarker
    {
        internal static AiReviewState Last;
        public static void Run(PsdTree tree, PsdToolSettings settings, bool force)
        {
            if (tree == null || settings == null) throw new InvalidOperationException("请先选择配置并解析 PSD。");
            CodexTaskWindow.ExportAndOpen(tree, settings);
        }
        internal static AiReviewState ReviewExternal(PsdTree tree, PsdToolSettings settings, AiRecognitionPlan plan, string signature, string note, string folder, bool show = true)
        {
            Receive(tree, settings, new AiVisualInput { signature = signature, note = note, reportFolder = folder }, JsonUtility.ToJson(plan), false, true, show);
            return Last;
        }
        static void Receive(PsdTree tree, PsdToolSettings settings, AiVisualInput input, string json, bool cache, bool external = false, bool show = true)
        {
            if (tree == null || settings == null || (external ? AiVisualInput.TaskSignature(tree, settings) : AiVisualInput.Signature(tree, settings)) != input.signature)
                throw new InvalidOperationException("请求期间 PSD、节点树或配置发生了变化，本次结果未应用，请重新识别。");
            var plan = AiProtocol.Parse(json);
            var state = new AiReviewState { tree = tree, settings = settings, signature = input.signature, note = input.note,
                reportFolder = input.reportFolder ?? AiRegionRecognition.ReportFolder(input.signature), externalTask = external };
            if (external) state.sourcePsdHash = CodexTaskBridge.HashFile(tree.psdPath);
            float min = settings.aiAutoApplyMin <= 0 ? 0.85f : settings.aiAutoApplyMin;
            var touched = new HashSet<int>();
            foreach (var proposal in plan.controls)
            {
                var row = new AiProposalRow { proposal = proposal };
                if (AiPlanEngine.Validate(tree, settings, proposal, out row.error))
                {
                    var ids = proposal.nodes.Select(n => n.layerId).ToArray();
                    if (ids.Any(touched.Contains)) row.error = "与另一个控件计划重复修改同一图层，请分开检查。";
                    else { foreach (int id in ids) touched.Add(id); row.selected = proposal.confidence >= min && !proposal.requiresReview; }
                }
                state.rows.Add(row);
            }
            state.regions.AddRange(plan.regions ?? new List<AiRegionResult>());
            state.RefreshStatuses();
            state.RefreshGenerationCheck();
            state.SaveReport();
            Last = state;
            if (show) AiRecognitionWindow.Open(state);
            Debug.Log($"AI 自动识别：已应用 {state.rows.Count(r => r.applied)} 个控件计划，待确认 {state.rows.Count(r => !r.applied && r.error == null)} 个，拒绝 {state.rows.Count(r => r.error != null)} 个。");
        }
        static void ShowError(Exception e)
        {
            if (e is OperationCanceledException) return;
            Debug.LogWarning("AI 自动识别：" + e.Message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("AI 自动识别未完成", e.Message, "确定");
        }
        // 兼容旧版本建议；新版本统一在识别结果窗口按完整控件计划应用。
        public static void Accept(ResourceSuggestion suggestion, PsdTree tree)
        {
            if (suggestion == null || suggestion.kind != SuggestionKind.ApplyAi || suggestion.done) return;
            var settings = PsdToolSettings.TryGet();
            var proposal = new AiControlProposal { title = "旧版 AI 标记", confidence = 1 };
            proposal.nodes.Add(new AiNodeEdit { layerId = suggestion.layerId, setTemplate = !string.IsNullOrEmpty(suggestion.templateId),
                templateId = suggestion.templateId, setSlot = !string.IsNullOrEmpty(suggestion.slot), slot = suggestion.slot });
            AiPlanEngine.Apply(tree, settings, new[] { proposal }, true); suggestion.done = true;
        }
    }
}
