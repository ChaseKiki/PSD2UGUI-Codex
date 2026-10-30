using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    [Serializable] public sealed class AiRecognitionPlan
    {
        public int version;
        public List<AiControlProposal> controls = new List<AiControlProposal>();
        public List<AiRegionResult> regions = new List<AiRegionResult>();
    }
    public enum AiRegionStatus { Ready, NeedsReview, Unsupported, Failed, Covered, NotControl, Applied, Generated, Ignored }
    [Serializable] public sealed class AiRegionResult
    {
        public int rootId, proposalIndex = -1, ownerRootId = -1;
        public string type, reason, message;
        public float confidence;
        public AiRegionStatus status;
        public AiControlProposal recoveryProposal;
    }
    [Serializable] public sealed class AiControlProposal
    {
        public string title, reason;
        public float confidence;
        public bool requiresReview;
        [NonSerialized] internal HashSet<int> manualOverride;
        public List<AiNodeEdit> nodes = new List<AiNodeEdit>();
        public List<AiGroupEdit> groups = new List<AiGroupEdit>();
    }
    [Serializable] public sealed class AiNodeEdit
    {
        public int layerId;
        public bool setTemplate, setSlot, setParent;
        public string templateId, slot;
        public int parentId;
    }
    [Serializable] public sealed class AiGroupEdit
    {
        public int groupId, parentId;
        public string templateId, slot, name;
        public int[] bounds;
    }

    internal static class AiProtocol
    {
        internal static bool HasEdits(AiControlProposal proposal) => proposal != null &&
            (proposal.nodes?.Count ?? 0) + (proposal.groups?.Count ?? 0) > 0;
        internal const string Version = "psd-ai-region-v3";
        [Serializable] sealed class Response { public Choice[] choices; public Message message; public string error, done_reason; }
        [Serializable] sealed class Choice { public Message message; public string finish_reason; }
        [Serializable] sealed class Message { public string content, refusal, thinking; }
        [Serializable] sealed class LocalDetails { public string[] capabilities; public string remote_host, remote_model; public LocalFamily details; }
        [Serializable] sealed class LocalFamily { public string family; }

        public static string Hash(string value)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char c in value ?? "")
                switch (c)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default: if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4")); else result.Append(c); break;
                }
            return result.Append('"').ToString();
        }
        public static Uri Endpoint(PsdToolSettings settings, string ollamaRoute = "/api/chat")
        {
            if (!Uri.TryCreate(settings.aiEndpoint?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("AI 地址需为 http/https 地址，不要把 Key 放在地址中。请打开“AI 配置”。");
            string path = uri.AbsolutePath.TrimEnd('/');
            if (settings.aiBackend == AiBackend.Ollama)
            {
                if (!uri.IsLoopback) throw new InvalidOperationException("本机 Ollama 请使用 localhost 或 127.0.0.1。其他服务请选“兼容 Chat 接口”。");
                return new Uri(uri.GetLeftPart(UriPartial.Authority) + ollamaRoute);
            }
            if (!path.EndsWith("/chat/completions", StringComparison.Ordinal)) path += path.EndsWith("/v1", StringComparison.Ordinal) ? "/chat/completions" : "/v1/chat/completions";
            return new Uri(uri.GetLeftPart(UriPartial.Authority) + path);
        }
        public static string KeyPref(PsdToolSettings settings) => PsdToolSettings.AiApiKeyPref + "." + Hash(Endpoint(settings).AbsoluteUri);
        public static string ApiKey(PsdToolSettings settings) => settings.aiBackend == AiBackend.Ollama ? "" : EditorPrefs.GetString(KeyPref(settings), "");
        public static bool Protected(PsdNode node) => node.source == DecisionSource.Manual || node.source == DecisionSource.Marker ||
            (node.markers != null && node.markers.Any(m => Markers.Slots.Contains(Markers.KeyOf(m)) || Markers.TypeToTemplate.ContainsKey(Markers.KeyOf(m))));
        public static bool Template(string value) => value != "Prefab" && Markers.TemplateIds.Contains(value ?? "");
        public static string LastJsonLine(string text, bool completeOnly = false)
        {
            text = text ?? "";
            if (completeOnly)
            {
                int end = text.LastIndexOf('\n'); if (end < 0) return ""; text = text.Substring(0, end);
            }
            text = text.Trim(); int start = text.LastIndexOf('\n'); return text.Substring(start + 1).Trim();
        }
        public static string Content(string json)
        {
            Response response;
            try { response = JsonUtility.FromJson<Response>(json); }
            catch (Exception) { throw new InvalidOperationException("服务返回内容不是有效 JSON，请检查接口类型和地址。"); }
            if (response == null) throw new InvalidOperationException("AI 返回为空。");
            if (!string.IsNullOrEmpty(response.error)) throw new InvalidOperationException("模型服务错误：" + response.error);
            var choice = response.choices?.FirstOrDefault();
            var message = choice?.message ?? response.message;
            if (string.IsNullOrWhiteSpace(message?.content) && !string.IsNullOrEmpty(message?.thinking))
                throw new InvalidOperationException("模型只输出了思考文本，尚未给出 JSON。请选择明确的 Instruct 视觉模型，例如 qwen3-vl:4b-instruct；不使用会变动的默认 4b/latest 标签。");
            string finish = choice?.finish_reason ?? response.done_reason;
            if (finish == "length" || finish == "max_tokens") throw new InvalidOperationException("模型输出被截断，未应用任何结果。请提高输出 Token 上限或分开处理界面。");
            if (!string.IsNullOrEmpty(message?.refusal)) throw new InvalidOperationException("模型未返回识别计划。");
            if (string.IsNullOrWhiteSpace(message?.content)) throw new InvalidOperationException("模型没有返回识别正文，请检查模型是否支持图像和当前接口。");
            return message.content.Trim();
        }
        public static void VerifyLocalDetails(string json)
        {
            var details = JsonUtility.FromJson<LocalDetails>(json);
            if (!string.IsNullOrEmpty(details?.remote_host) || !string.IsNullOrEmpty(details?.remote_model))
                throw new InvalidOperationException("所选模型指向远程服务。本机方案不发送 PSD 给 cloud 模型，请选择实际下载到本机的视觉模型。");
            if (details?.capabilities == null || !details.capabilities.Contains("vision")) throw new InvalidOperationException("所选本机模型未声明 vision 图像能力。请选择视觉模型并更新 Ollama。");
            if (details.details?.family == "qwen3vl" && details.capabilities.Contains("thinking"))
                throw new InvalidOperationException("所选 Qwen3-VL 是 Thinking 权重，本工具实测会把预算耗在思考文本。请在模型档位选择明确的 Instruct 版本，例如 qwen3-vl:4b-instruct。");
        }
        public static AiRecognitionPlan Parse(string json)
        {
            json = (json ?? "").Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int line = json.IndexOf('\n');
                if (line < 0 || !json.EndsWith("```", StringComparison.Ordinal)) throw new InvalidOperationException("AI JSON 代码块不完整。");
                json = json.Substring(line + 1, json.Length - line - 4).Trim();
            }
            if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal)) throw new InvalidOperationException("AI 返回了说明文字或不完整 JSON，未应用结果。");
            AiRecognitionPlan plan;
            try { plan = JsonUtility.FromJson<AiRecognitionPlan>(json); }
            catch (Exception) { throw new InvalidOperationException("AI 计划 JSON 解析失败，未应用结果。"); }
            if (plan == null || plan.version != 2 || plan.controls == null || plan.controls.Count > 100) throw new InvalidOperationException("AI 计划版本或控件数量无效。");
            return plan;
        }
        public static string ChatBody(PsdToolSettings settings, string prompt, IEnumerable<string> images, int? outputTokens = null, bool recognition = false)
        {
            if (settings.aiBackend == AiBackend.Ollama && settings.aiModel?.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("本机方案请使用实际下载的模型，不使用 cloud 模型。");
            var list = images?.ToList() ?? new List<string>();
            var result = new StringBuilder("{\"model\":").Append(Quote(settings.aiModel?.Trim())).Append(",\"stream\":false,");
            if (settings.aiBackend == AiBackend.Ollama)
            {
                result.Append("\"format\":").Append(recognition ? AiRecognitionPrompt.Schema : "\"json\"").Append(',');
                if (settings.aiModel.IndexOf("instruct", StringComparison.OrdinalIgnoreCase) < 0) result.Append("\"think\":false,");
                result.Append("\"options\":{\"temperature\":0,\"num_ctx\":").Append(settings.aiContextSize)
                    .Append(",\"num_predict\":").Append(outputTokens ?? settings.aiOutputTokens).Append("},\"messages\":[{\"role\":\"user\",\"content\":").Append(Quote(prompt));
                if (list.Count > 0) result.Append(",\"images\":[").Append(string.Join(",", list.Select(Quote))).Append(']');
                return result.Append("}]}").ToString();
            }
            result.Append("\"temperature\":0,\"max_tokens\":").Append(outputTokens ?? settings.aiOutputTokens).Append(",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":").Append(Quote(prompt)).Append('}');
            foreach (string image in list) result.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":").Append(Quote("data:image/png;base64," + image)).Append("}}");
            return result.Append("]}]}").ToString();
        }
    }
}
