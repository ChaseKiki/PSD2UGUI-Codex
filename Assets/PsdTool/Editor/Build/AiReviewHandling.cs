using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PsdTool
{
    [Serializable] internal sealed class AiReviewReport
    {
        public string panel, signature, generationError;
        public bool generated;
        public List<AiReviewReportEntry> regions = new List<AiReviewReportEntry>();
        public List<GenerationIssue> generationIssues = new List<GenerationIssue>();
        public string preflightError;
    }
    [Serializable] internal sealed class AiReviewReportEntry
    {
        public int rootId;
        public string nodePath, type, status, reason, message;
    }
    internal sealed partial class AiReviewState
    {
        internal bool IsIgnored(AiProposalRow row) => regions.Any(r => r.proposalIndex == rows.IndexOf(row) && r.status == AiRegionStatus.Ignored);
        internal void IgnoreRegion(AiRegionResult entry, bool restore = false)
        {
            if (entry.proposalIndex >= 0 && rows[entry.proposalIndex].applied) throw new InvalidOperationException("建议已应用，请到节点树调整，不能仅从报告撤销。");
            entry.status = restore ? AiRegionStatus.Failed : AiRegionStatus.Ignored;
            entry.message = restore ? "已恢复待处理，请重新校验或定位节点" : "人工确认忽略此建议，保留当前节点配置/视觉";
            if (entry.proposalIndex >= 0) { rows[entry.proposalIndex].error = entry.message; rows[entry.proposalIndex].selected = false; }
            generationChecked = false; SaveReport();
        }
        internal string NodePath(int id)
        {
            var names = new List<string>(); var seen = new HashSet<int>();
            for (var n = tree?.Find(id); n != null && seen.Add(n.layerId); n = tree.Find(n.parentId)) names.Add(n.goName ?? n.layerId.ToString());
            names.Reverse(); return names.Count == 0 ? "未关联来源节点" : string.Join("/", names);
        }
        internal void SaveReport()
        {
            RefreshStatuses();
            if (string.IsNullOrEmpty(reportFolder) || !Directory.Exists(reportFolder)) return;
            var report = new AiReviewReport { panel = tree?.panelName, signature = signature, generated = generated, generationError = error,
                generationIssues = generationIssues.ToList(), preflightError = preflightError };
            foreach (var region in regions)
                report.regions.Add(new AiReviewReportEntry { rootId = region.rootId, nodePath = NodePath(region.rootId), type = region.type,
                    status = region.status.ToString(), reason = region.reason, message = region.message });
            try
            {
                File.WriteAllText(Path.Combine(reportFolder, "review-report.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                var text = new StringBuilder("PSD 识别校验报告\n界面：" + report.panel + "\n" + (generated ? "Prefab 已生成，交互待验收。" : "尚未完成本次 Prefab 生成。") + "\n异常建议已隔离，不影响其它有效项；未关联的记录不应用。\n");
                if (!string.IsNullOrEmpty(error)) text.AppendLine("生成/来源错误：" + error);
                if (!string.IsNullOrEmpty(preflightError)) text.AppendLine("生成预检问题：" + preflightError);
                foreach (var issue in generationIssues) text.AppendLine($"\n[交互待处理] {issue.nodePath}（{issue.layerId}，{issue.type}）\n{issue.message}\n{issue.resolution}");
                foreach (var r in report.regions) text.AppendLine($"\n[{r.status}] {r.nodePath}（区域 {r.rootId}，{r.type}）\n{r.message}\n{r.reason}");
                File.WriteAllText(Path.Combine(reportFolder, "校验报告.txt"), text.ToString(), new UTF8Encoding(false));
            }
            catch (IOException e) { Debug.LogWarning("校验报告保存失败，界面结果仍保留：" + e.Message); }
            catch (UnauthorizedAccessException e) { Debug.LogWarning("校验报告目录不可写，界面结果仍保留：" + e.Message); }
        }
        internal void RetryRegion(AiRegionResult entry, string type)
        {
            if (CurrentSignature() != signature) throw new InvalidOperationException("来源已变化；请重新识别，或在主窗口完成手动处理并生成。");
            if (tree.Find(entry.rootId) == null) throw new InvalidOperationException("此记录无法关联来源节点，已保留在报告中。");
            if (entry.proposalIndex >= 0 && rows[entry.proposalIndex].applied) throw new InvalidOperationException("此方案已经应用，请到节点树手动调整。");
            try
            {
                var plan = AiRegionBindings.Build(tree, new AiRegion { rootId = entry.rootId, type = type, confidence = 1 });
                plan.requiresReview = true; plan.reason = "人工指定类型，重新进行几何定位；尚未应用";
                if (!AiPlanEngine.Validate(tree, settings, plan, out string message)) throw new InvalidOperationException(message);
                var touched = new HashSet<int>(rows.Where((r, i) => i != entry.proposalIndex && r.error == null && r.proposal != null)
                    .SelectMany(r => r.proposal.nodes).Select(n => n.layerId));
                if (plan.nodes.Any(n => touched.Contains(n.layerId))) throw new InvalidOperationException("与其它可用方案重复修改图层，请先在节点树处理归属。");
                var row = new AiProposalRow { proposal = plan, selected = false };
                if (entry.proposalIndex < 0) { entry.proposalIndex = rows.Count; rows.Add(row); } else rows[entry.proposalIndex] = row;
                entry.type = type; entry.confidence = 1; entry.reason = plan.reason; entry.message = "重新校验通过，请勾选确认";
                entry.status = AiRegionStatus.NeedsReview;
            }
            catch (InvalidOperationException e)
            {
                entry.status = AiRegionStatus.Failed; entry.message = e.Message;
                if (entry.proposalIndex >= 0) { rows[entry.proposalIndex].error = e.Message; rows[entry.proposalIndex].selected = false; }
            }
            generationChecked = false; SaveReport();
        }
        internal void OverrideRegion(AiRegionResult entry)
        {
            if (CurrentSignature() != signature) throw new InvalidOperationException("来源已变化，请先更新校验或重新识别。");
            RefreshStatuses();
            // Also handles a click from a row drawn before its parent was applied.
            if (entry.status == AiRegionStatus.Covered) { SaveReport(); return; }
            if (entry.proposalIndex >= 0 && rows[entry.proposalIndex].applied) throw new InvalidOperationException("已应用的建议请到节点树调整。");
            var proposal = entry.recoveryProposal;
            if (!AiProtocol.HasEdits(proposal)) throw new InvalidOperationException("此项没有独立的可应用方案；若属于 Dropdown，请先应用父控件项，否则重新校验此项。");
            proposal = JsonUtility.FromJson<AiControlProposal>(JsonUtility.ToJson(proposal));
            proposal.manualOverride = new HashSet<int>(proposal.nodes.Select(n => n.layerId));
            // Existing virtual groups may be reused by stable member keys.
            foreach (var node in tree.nodes.Where(n => n.aiGeneratedGroup))
            {
                var seen = new HashSet<int>();
                for (var parent = node; parent != null && seen.Add(parent.layerId); parent = tree.Find(parent.parentId))
                    if (parent.layerId == entry.rootId) { proposal.manualOverride.Add(node.layerId); break; }
            }
            if (!AiPlanEngine.Validate(tree, settings, proposal, out string message)) throw new InvalidOperationException(message);
            InstallReviewed(entry, proposal);
            ApplySelected(true, rows[entry.proposalIndex]);
        }
        internal void ConfigureProgress(AiRegionResult entry, PsdNode options)
        {
            if (CurrentSignature() != signature) throw new InvalidOperationException("来源已变化，请先更新校验或重新识别。");
            var node = tree.Find(entry.rootId) ?? throw new InvalidOperationException("源节点已丢失。");
            var copy = UnityEngine.Object.Instantiate(tree);
            AiControlProposal proposal;
            try
            {
                var working = copy.Find(node.layerId); CopyProgress(options, working);
                proposal = AiRegionBindings.Build(copy, new AiRegion { rootId = node.layerId, type = "Progress", confidence = 1 });
                proposal.manualOverride = new HashSet<int>(proposal.nodes.Select(e => e.layerId));
                if (!AiPlanEngine.Validate(copy, settings, proposal, out string message)) throw new InvalidOperationException(message);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            CheckOverlap(entry, proposal);
            UnityEditor.Undo.RegisterCompleteObjectUndo(tree, "配置进度条填充");
            CopyProgress(options, node); node.source = DecisionSource.Manual;
            UnityEditor.EditorUtility.SetDirty(tree); UnityEditor.AssetDatabase.SaveAssetIfDirty(tree);
            signature = CurrentSignature(); generated = false;
            entry.type = "Progress"; proposal.reason = "人工选择填充方式和方向，已校验部件；请应用此方案。";
            InstallReviewed(entry, proposal);
        }
        static void CopyProgress(PsdNode from, PsdNode to)
        {
            to.progressFill = from.progressFill; to.progressReverse = from.progressReverse; to.progressClockwise = from.progressClockwise;
            to.progressOrigin = from.progressOrigin; to.progressValue = from.progressValue;
        }
        void CheckOverlap(AiRegionResult entry, AiControlProposal proposal)
        {
            var touched = new HashSet<int>(rows.Where((r, i) => i != entry.proposalIndex && r.error == null && r.proposal != null && !IsIgnored(r))
                .SelectMany(r => r.proposal.nodes).Select(n => n.layerId));
            if (proposal.nodes.Any(n => touched.Contains(n.layerId))) throw new InvalidOperationException("与其它方案重复修改图层，请先处理归属，未强制覆盖冲突。");
        }
        void InstallReviewed(AiRegionResult entry, AiControlProposal proposal)
        {
            CheckOverlap(entry, proposal);
            proposal.requiresReview = true;
            var row = new AiProposalRow { proposal = proposal };
            if (entry.proposalIndex < 0) { entry.proposalIndex = rows.Count; rows.Add(row); } else rows[entry.proposalIndex] = row;
            entry.reason = proposal.reason; entry.status = AiRegionStatus.NeedsReview;
            generationChecked = false; SaveReport();
        }
        internal void RecheckAfterManualEdits()
        {
            if (string.IsNullOrEmpty(sourcePsdHash) || !File.Exists(tree.psdPath) || CodexTaskBridge.HashFile(tree.psdPath) != sourcePsdHash)
                throw new InvalidOperationException("PSD 来源已改变，不能沿用旧识别；请重新识别。");
            foreach (var row in rows.Where(r => !r.applied))
            {
                row.selected = false;
                if (regions.Any(r => r.proposalIndex == rows.IndexOf(row) && r.status == AiRegionStatus.Ignored)) { row.error = "人工已忽略"; continue; }
                AiPlanEngine.Validate(tree, settings, row.proposal, out row.error);
            }
            foreach (var entry in regions)
            {
                if (entry.status == AiRegionStatus.Ignored) continue;
                if (entry.proposalIndex >= 0 && rows[entry.proposalIndex].applied) continue;
                if (entry.status != AiRegionStatus.Failed && entry.status != AiRegionStatus.Unsupported &&
                    !(entry.proposalIndex >= 0 && rows[entry.proposalIndex].error != null)) continue;
                var node = tree.Find(entry.rootId);
                if (node == null || !PrefabGenerator.IsControl(node.templateId)) continue;
                var manual = new AiControlProposal { title = "人工配置 · " + node.goName, confidence = 1, requiresReview = true,
                    reason = "按当前人工配置重新校验，不再采用原异常 AI 修改" };
                manual.nodes.Add(new AiNodeEdit { layerId = node.layerId, setTemplate = true, templateId = node.templateId });
                if (!AiPlanEngine.Validate(tree, settings, manual, out _)) continue;
                var row = new AiProposalRow { proposal = manual, selected = false };
                if (entry.proposalIndex < 0) { entry.proposalIndex = rows.Count; rows.Add(row); } else rows[entry.proposalIndex] = row;
                entry.type = node.templateId; entry.reason = manual.reason;
            }
            signature = CurrentSignature(); generated = false; error = null; generationChecked = false; SaveReport();
        }
    }
}
