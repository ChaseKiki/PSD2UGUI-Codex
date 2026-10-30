using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PsdTool
{
    internal static class AiTransport
    {
        static UnityWebRequest request;
        static UnityWebRequestAsyncOperation operation;
        static Action<string> success;
        static Action<Exception> failure;
        static double started;
        static string title;
        static Action<string> progress;
        static double lastProgress;
        static string progressText;
        static float progressFraction;
        public static bool Busy => request != null;
        public static void Start(PsdToolSettings settings, Uri uri, string body, string label, Action<string> onSuccess, Action<Exception> onFailure, int timeout = 0, Action<string> onProgress = null)
        {
            if (Busy) throw new InvalidOperationException("已有 AI 请求正在进行，可先取消。");
            request = new UnityWebRequest(uri.AbsoluteUri, body == null ? "GET" : "POST");
            try
            {
                if (body != null) { request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); request.SetRequestHeader("Content-Type", "application/json"); }
                request.downloadHandler = new DownloadHandlerBuffer();
                string key = AiProtocol.ApiKey(settings);
                if (!string.IsNullOrEmpty(key)) request.SetRequestHeader("Authorization", "Bearer " + key);
                request.timeout = timeout > 0 ? timeout : Mathf.Clamp(settings.aiTimeoutSeconds, 30, 900);
                request.redirectLimit = 0; // 不将凭据或 PSD 图片跟随重定向发送到另一个地址。
                success = onSuccess; failure = onFailure; title = label; started = EditorApplication.timeSinceStartup;
                progress = onProgress; lastProgress = started; progressText = null; progressFraction = 0;
                operation = request.SendWebRequest();
                EditorApplication.update += Tick; EditorApplication.quitting += Cancel;
            }
            catch { Cleanup(); throw; }
        }
        static void Tick()
        {
            if (request == null) return;
            if (progress != null && EditorApplication.timeSinceStartup - lastProgress >= 0.3)
            {
                lastProgress = EditorApplication.timeSinceStartup;
                try { progress(AiProtocol.LastJsonLine(request.downloadHandler.text, true)); } catch (Exception) { /* 等待下一个完整进度行。 */ }
            }
            if (!Application.isBatchMode && EditorUtility.DisplayCancelableProgressBar(title, progressText ?? $"等待本机模型，已用 {EditorApplication.timeSinceStartup - started:0} 秒；首次加载会较慢。", progressText == null ? Mathf.Clamp01(operation.progress) : progressFraction)) { Cancel(); return; }
            if (!operation.isDone) return;
            string result = request.downloadHandler.text;
            Exception error = request.result == UnityWebRequest.Result.Success ? null : new InvalidOperationException(Error(request.responseCode, request.error));
            var done = success; var failed = failure; Cleanup();
            if (error != null) { failed?.Invoke(error); return; }
            try { done?.Invoke(result); } catch (Exception e) { failed?.Invoke(e); }
        }
        static string Error(long code, string detail)
        {
            if (code == 401 || code == 403) return "AI 服务拒绝访问。兼容接口请在 AI 配置中填写该地址专用的 Key；本机 Ollama 通常无需 Key。";
            if (code == 404) return "未找到模型或 API 路径。请刷新已安装模型列表，检查是否已下载所选模型。";
            if (code == 400) return "模型不接受当前图像/JSON 请求。请运行视觉测试，确认选择的是视觉模型。";
            if (code >= 500) return "本机模型服务处理失败，可能内存/显存不足。请查看 Ollama 日志，选择更小模型或降低图片尺寸/上下文。";
            return "无法完成 AI 请求：" + detail + "。请确认 Ollama 正在运行、地址端口正确；超时可在 AI 配置中调高等待时间。";
        }
        public static void Cancel()
        {
            if (request == null) return;
            var failed = failure; request.Abort(); Cleanup(); failed?.Invoke(new OperationCanceledException("已取消，未应用这次 AI 返回。"));
        }
        public static void ReportProgress(string text, float fraction) { progressText = text; progressFraction = Mathf.Clamp01(fraction); }
        static void Cleanup()
        {
            EditorApplication.update -= Tick; EditorApplication.quitting -= Cancel; EditorUtility.ClearProgressBar();
            request?.Dispose(); request = null; operation = null; success = null; failure = null; progress = null; progressText = null;
        }
    }
}
