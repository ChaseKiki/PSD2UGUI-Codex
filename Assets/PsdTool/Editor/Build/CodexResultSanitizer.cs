using System;
using System.Collections.Generic;
using System.Linq;

namespace PsdTool
{
    internal sealed class CodexNormalizedResult
    {
        public readonly List<AiRegion> regions = new List<AiRegion>();
        public readonly Dictionary<int, AiControlProposal> structures = new Dictionary<int, AiControlProposal>();
        public readonly List<AiRegionResult> unassigned = new List<AiRegionResult>();
    }
    // 只有整份任务身份/输入完整性由 Bridge 决定；区域语义错误在这里隔离。
    internal static class CodexResultSanitizer
    {
        static readonly string[] Types = { "None", "Button", "Toggle", "InputField", "ScrollView", "Dropdown", "Slider", "Progress" };
        internal static CodexNormalizedResult Normalize(CodexTaskManifest task, CodexTaskAnswer answer, PsdTree tree)
        {
            var result = new CodexNormalizedResult();
            var known = new HashSet<int>(task.regions.Select(r => r.rootId));
            var ids = new HashSet<int>((task.layers ?? new List<CodexTaskLayer>()).Select(l => l.id));
            var rows = (answer.regions ?? new List<CodexTaskClassification>()).Where(r => r != null).GroupBy(r => r.rootId).ToDictionary(g => g.Key, g => g.ToList());
            var plans = (answer.structures ?? new List<CodexTaskStructure>()).Where(r => r != null).GroupBy(r => r.regionId).ToDictionary(g => g.Key, g => g.ToList());
            void Unassigned(string message) => result.unassigned.Add(new AiRegionResult { rootId = -1, status = AiRegionStatus.Failed, message = message, reason = "无法安全关联到来源节点，已忽略，不影响其它区域" });
            if (answer.regions?.Any(r => r == null) == true) Unassigned("返回中有空的区域记录");
            if (answer.structures?.Any(r => r == null) == true) Unassigned("返回中有空的结构记录");
            foreach (int id in rows.Keys.Where(id => !known.Contains(id))) Unassigned("未导出的区域 ID：" + id);
            foreach (int id in plans.Keys.Where(id => !known.Contains(id))) Unassigned("结构引用未导出的区域 ID：" + id);
            foreach (var source in task.regions)
            {
                var region = new AiRegion { rootId = source.rootId, type = "None" };
                result.regions.Add(region);
                if (!rows.TryGetValue(source.rootId, out var matches)) { region.error = "模型遗漏此区域，保留原节点，等待人工处理"; continue; }
                if (matches.Count != 1) { region.error = "同一区域返回多份分类，无法确定应采信哪份，已隔离"; continue; }
                var row = matches[0]; region.type = row.type; region.confidence = row.confidence; region.resolution = row.reason;
                if (!Types.Contains(row.type) || float.IsNaN(row.confidence) || float.IsInfinity(row.confidence) || row.confidence < 0 || row.confidence > 1 ||
                    string.IsNullOrWhiteSpace(row.reason) || row.reason.Length > 240)
                { region.confidence = 0; region.resolution = "无效分类数据"; region.error = "此区域的类型、置信度或依据无效，已忽略该建议"; continue; }
                if (!plans.TryGetValue(source.rootId, out var candidates)) continue;
                if (candidates.Count != 1) { region.error = "同一区域返回多份结构方案，已隔离，未随机采信第一份"; continue; }
                var structure = candidates[0];
                if ((structure.nodes?.Count ?? 0) + (structure.groups?.Count ?? 0) == 0) continue;
                if ((structure.nodes?.Count ?? 0) > 256 || (structure.groups?.Count ?? 0) > 64)
                { region.error = "单区域结构方案超出数量限制，已隔离"; continue; }
                if (structure.nodes == null || structure.nodes.Any(e => e == null || !ids.Contains(e.layerId)))
                { region.error = "结构方案引用了未导出或空的图层，已隔离"; continue; }
                if (structure.groups?.Any(g => g == null) == true)
                { region.error = "结构方案包含空分组，已隔离"; continue; }
                bool containerPlan = row.type == "None";
                int controls = structure.nodes.Count(e => e.setTemplate && Types.Contains(e.templateId) && e.templateId != "None") +
                    (structure.groups?.Count(g => Types.Contains(g.templateId) && g.templateId != "None") ?? 0);
                if (containerPlan && controls == 0)
                { region.error = "容器方案未包含任何可绑定的控件根，已隔离"; continue; }
                if (!containerPlan && !structure.nodes.Any(e => e.setTemplate && e.templateId == row.type) && !(structure.groups?.Any(g => g.templateId == row.type) ?? false))
                { region.error = "结构方案没有对应判型的控件根，已隔离"; continue; }
                result.structures.Add(source.rootId, new AiControlProposal { title = (containerPlan ? "拆组为 " + controls + " 个控件" : row.type) + " · " + tree.Find(source.rootId)?.goName,
                    requiresReview = containerPlan,
                    confidence = row.confidence, reason = "Codex 部件归属/结构建议：" + row.reason,
                    nodes = structure.nodes, groups = structure.groups ?? new List<AiGroupEdit>() });
            }
            return result;
        }
    }
}
