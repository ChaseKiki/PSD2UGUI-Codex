using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    [Serializable] internal sealed class CodexTaskRegion
    {
        public int rootId, parentRegionId, depth;
        public int[] rect;
        public string image, imageHash, currentType;
        public bool locked;
    }
    [Serializable] internal sealed class CodexTaskManifest
    {
        public int version = 1;
        public string taskId, signature, treeGuid, title, createdUtc, overview = "overview.png", overviewHash;
        public List<CodexTaskRegion> regions = new List<CodexTaskRegion>();
        public List<CodexTaskLayer> layers = new List<CodexTaskLayer>();
        public List<CodexTaskImage> layerSheets = new List<CodexTaskImage>();
    }
    [Serializable] internal sealed class CodexTaskLayer
    {
        public int id, parentId;
        public string kind, currentType, slot, locks, text;
        public int[] rect;
        public bool locked, visible;
        public float opacity;
    }
    [Serializable] internal sealed class CodexTaskImage { public string image, hash; }
    [Serializable] internal sealed class CodexTaskStructure
    {
        public int regionId;
        public List<AiNodeEdit> nodes = new List<AiNodeEdit>();
        public List<AiGroupEdit> groups = new List<AiGroupEdit>();
    }
    [Serializable] internal sealed class CodexTaskAnswer
    {
        public int version;
        public string taskId, signature, status;
        public List<CodexTaskClassification> regions;
        public List<CodexTaskStructure> structures;
    }
    [Serializable] internal sealed class CodexTaskClassification
    {
        public int rootId;
        public string type, reason;
        public float confidence;
    }
    [Serializable] internal sealed class CodexTaskReceipt
    {
        public string taskId, folder, manifestHash, treeGuid, signature, reviewedHash;
    }
    internal sealed class CodexTaskImport
    {
        public AiRecognitionPlan plan;
        public string signature, note, folder, resultHash;
    }

    /// <summary>纯文件交接；不启动 Codex、不连接模型、不读取凭据。结果只提供区域分类，不能直接改 Unity 层级。</summary>
    internal static class CodexTaskBridge
    {
        internal const string SkillName = "psd-ugui-recognize";
        internal const int MaxJsonBytes = 512 * 1024;
        static readonly string[] Types = { "None", "Button", "Toggle", "InputField", "ScrollView", "Dropdown", "Slider", "Progress" };
        internal static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        internal static string TaskRoot => Path.Combine(ProjectRoot, "Library/PsdTool/CodexTasks");
        static string Tickets => Path.Combine(ProjectRoot, "Library/PsdTool/CodexTickets");
        static string TreeGuid(PsdTree tree) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tree));
        static string TicketPath(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) throw new InvalidOperationException("任务编号无效。");
            return Path.Combine(Tickets, id + ".json");
        }
        internal static string HashFile(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string ReadJson(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > MaxJsonBytes) throw new InvalidOperationException("任务 JSON 不存在、为空或超过 512KB。");
            string json = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal)) throw new InvalidOperationException("结果尚未完整写入，或不是 JSON 对象。");
            return json;
        }
        static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
        internal static string Latest(PsdTree tree)
        {
            string guid = tree == null ? "" : TreeGuid(tree);
            string path = Path.Combine(Tickets, "latest-" + guid + ".txt");
            return guid.Length > 0 && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        static string SkillSource()
        {
            string expected = "Assets/PsdTool/CodexSkill/" + SkillName + "/SKILL.md";
            if (File.Exists(PathUtil.ToFullPath(expected))) return Path.GetDirectoryName(PathUtil.ToFullPath(expected));
            string found = AssetDatabase.GetAllAssetPaths().FirstOrDefault(p => p.EndsWith("/CodexSkill/" + SkillName + "/SKILL.md", StringComparison.Ordinal));
            if (found == null) throw new InvalidOperationException("随包 Codex skill 缺失，请完整导入新版工具包。");
            return Path.GetDirectoryName(PathUtil.ToFullPath(found));
        }
        internal static string Export(PsdTree tree, PsdToolSettings settings)
        {
            if (AiTransport.Busy) throw new InvalidOperationException("先结束正在运行的本地/接口请求，再导出任务。");
            string guid = TreeGuid(tree);
            if (guid.Length == 0) throw new InvalidOperationException("请先将 PSD 解析为项目节点树，再导出任务。");
            EditorUtility.SetDirty(tree); AssetDatabase.SaveAssetIfDirty(tree);
            string skill = SkillSource();
            var input = AiVisualInput.Prepare(tree, settings, true);
            if (input.regions.Count == 0) throw new InvalidOperationException("没有候选控件组。请先常规分组；无需规则命名。");
            string id = Guid.NewGuid().ToString("N"), folder = Path.Combine(TaskRoot, id);
            Directory.CreateDirectory(Path.Combine(folder, "regions"));
            var task = new CodexTaskManifest { taskId = id, signature = input.signature, treeGuid = guid, title = tree.panelName, createdUtc = DateTime.UtcNow.ToString("o") };
            foreach (var node in tree.nodes.Where(n => n.status != NodeStatus.PendingDelete).OrderBy(n => n.layerId))
                task.layers.Add(new CodexTaskLayer { id = node.layerId, parentId = node.parentId, kind = node.kind.ToString(),
                    currentType = node.templateId, slot = node.slot, locks = node.locks.ToString(), locked = AiProtocol.Protected(node), visible = node.visible,
                    opacity = node.opacity, text = node.kind == LayerKind.Text ? node.text?.content : "",
                    rect = new[] { node.psdRect.x, node.psdRect.y, node.psdRect.width, node.psdRect.height } });
            File.WriteAllBytes(Path.Combine(folder, task.overview), Convert.FromBase64String(input.images[0])); task.overviewHash = HashFile(Path.Combine(folder, task.overview));
            for (int i = 1; i < input.images.Count; i++)
            {
                string image = "layers-" + i + ".png", path = Path.Combine(folder, image);
                File.WriteAllBytes(path, Convert.FromBase64String(input.images[i]));
                task.layerSheets.Add(new CodexTaskImage { image = image, hash = HashFile(path) });
            }
            var ids = new HashSet<int>(input.regions.Select(r => r.rootId));
            foreach (var region in input.regions.OrderBy(r => AiRegions.Depth(tree, r.rootId)).ThenBy(r => r.rootId))
            {
                var node = tree.Find(region.rootId); int parent = node.parentId;
                while (parent != -1 && !ids.Contains(parent)) parent = tree.Find(parent)?.parentId ?? -1;
                string image = "regions/" + region.rootId + ".png", path = Path.Combine(folder, image);
                File.WriteAllBytes(path, Convert.FromBase64String(region.image));
                task.regions.Add(new CodexTaskRegion { rootId = region.rootId, parentRegionId = parent, depth = AiRegions.Depth(tree, region.rootId),
                    rect = new[] { region.rect.x, region.rect.y, region.rect.width, region.rect.height }, image = image, imageHash = HashFile(path),
                    currentType = node.templateId, locked = AiProtocol.Protected(node) });
            }
            string skillDestination = Path.Combine(folder, ".agents/skills", SkillName);
            foreach (string relative in new[] { "SKILL.md", "references/result-format.md", "agents/openai.yaml" })
            {
                string destination = Path.Combine(skillDestination, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(Path.Combine(skill, relative), destination, false);
            }
            string manifest = Path.Combine(folder, "task.json"); Write(manifest, JsonUtility.ToJson(task, true));
            Write(Path.Combine(folder, "给Codex的指令.txt"), Prompt(folder));
            // 不导出原 PSD、切图目录、Key、机器上的模型配置或业务脚本。
            var receipt = new CodexTaskReceipt { taskId = id, folder = Path.GetFullPath(folder), manifestHash = HashFile(manifest), treeGuid = guid, signature = input.signature };
            Directory.CreateDirectory(Tickets); Write(TicketPath(id), JsonUtility.ToJson(receipt, true));
            Write(Path.Combine(Tickets, "latest-" + guid + ".txt"), folder);
            return folder;
        }
        internal static string Prompt(string folder) => "请使用任务自带的 psd-ugui-recognize 技能。先读取「" +
            Path.Combine(folder, ".agents/skills/" + SkillName + "/SKILL.md") + "」，再读取「" + Path.Combine(folder, "task.json") +
            "」。查看 overview.png 和每个区域图片，按技能规定生成该任务目录下的 result.json。只写 result.json 及其原子写入临时文件，不修改 Unity 工程或 PSD，不调用 Ollama、模型 API 或其他代理。完成后简要报告。若目录无写入权限，请提示将此任务目录作为工作目录打开，不绕过权限。";
        static CodexTaskReceipt Receipt(string folder, out CodexTaskManifest task)
        {
            string manifest = Path.Combine(folder, "task.json"); task = JsonUtility.FromJson<CodexTaskManifest>(ReadJson(manifest));
            if (task == null || task.version != 1) throw new InvalidOperationException("不支持此任务版本。");
            string ticket = TicketPath(task.taskId);
            if (!File.Exists(ticket)) throw new InvalidOperationException("本项目没有此任务的导出凭据。不要跨项目导入结果，请在目标项目重新导出。");
            var receipt = JsonUtility.FromJson<CodexTaskReceipt>(ReadJson(ticket));
            if (receipt == null || receipt.taskId != task.taskId || !string.Equals(Path.GetFullPath(folder), receipt.folder, StringComparison.OrdinalIgnoreCase) ||
                receipt.manifestHash != HashFile(manifest) || receipt.signature != task.signature || receipt.treeGuid != task.treeGuid)
                throw new InvalidOperationException("任务内容或任务位置已被修改，请重新导出；只应回写 result.json。");
            return receipt;
        }
        internal static string ReviewedHash(string folder) { return Receipt(folder, out _).reviewedHash; }
        internal static void MarkReviewed(string folder, string hash)
        {
            var receipt = Receipt(folder, out _); receipt.reviewedHash = hash; Write(TicketPath(receipt.taskId), JsonUtility.ToJson(receipt, true));
        }
        internal static CodexTaskImport Import(string folder, PsdTree tree, PsdToolSettings settings, string resultFile = null)
        {
            var receipt = Receipt(folder, out var task);
            if (TreeGuid(tree) != receipt.treeGuid || AiVisualInput.TaskSignature(tree, settings) != receipt.signature)
                throw new InvalidOperationException("PSD、节点树或相关配置已变化，拒绝旧结果。请重新导出任务。");
            if (task.regions == null || task.regions.Count < 1 || task.regions.Count > 100) throw new InvalidOperationException("候选区域数量无效。");
            if (task.overview != "overview.png" || HashFile(Path.Combine(folder, task.overview)) != task.overviewHash) throw new InvalidOperationException("整图已被修改。");
            foreach (var region in task.regions)
                if (region.image != "regions/" + region.rootId + ".png" || HashFile(Path.Combine(folder, region.image)) != region.imageHash)
                    throw new InvalidOperationException("区域图片已被修改，请重新导出。");
            foreach (var sheet in task.layerSheets ?? new List<CodexTaskImage>())
                if (!Regex.IsMatch(sheet.image ?? "", "^layers-[0-9]+\\.png$") || HashFile(Path.Combine(folder, sheet.image)) != sheet.hash)
                    throw new InvalidOperationException("图层定位图片已被修改，请重新导出。");
            string resultPath = resultFile ?? Path.Combine(folder, "result.json");
            var answer = JsonUtility.FromJson<CodexTaskAnswer>(ReadJson(resultPath));
            if (answer == null || answer.version != 1 || answer.status != "completed" || answer.taskId != task.taskId || answer.signature != task.signature)
                throw new InvalidOperationException("结果版本、任务编号、来源签名或完成状态不匹配。");
            var normalized = CodexResultSanitizer.Normalize(task, answer, tree);
            var regions = normalized.regions;
            var plan = AiRegionRecognition.Compose(tree, regions, normalized.structures, settings);
            plan.regions.AddRange(normalized.unassigned);
            foreach (var proposal in plan.controls)
            {
                var root = proposal.nodes.FirstOrDefault(e => e.setTemplate && e.templateId != "Group" && !string.IsNullOrEmpty(e.templateId));
                var classification = root == null ? null : answer.regions?.Find(r => r != null && r.rootId == root.layerId);
                if (classification != null && !proposal.reason.StartsWith("Codex 部件归属", StringComparison.Ordinal)) proposal.reason = "Codex：" + classification.reason + "；Unity 按来源层级和几何绑定";
            }
            return new CodexTaskImport { plan = plan, signature = task.signature, folder = folder, resultHash = HashFile(resultPath),
                note = "Codex 任务 " + task.taskId + "，" + regions.Count + " 个来源区域已校验；异常建议单独隔离，其余继续审核和生成。导入仅创建预览。\n" +
                    string.Join("\n", regions.Where(r => !string.IsNullOrEmpty(r.error)).Select(r => "区域 " + r.rootId + "：" + r.error)) };
        }
    }
}
