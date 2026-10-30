using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;

namespace PsdTool
{
    internal static class AiLocalRuntime
    {
        internal const string ProxyPref = "PsdTool.Ollama.DownloadHttpsProxy";
        internal const string ExePref = "PsdTool.Ollama.Executable";
        internal static string Executable()
        {
            string configured = EditorPrefs.GetString(ExePref, "");
            if (File.Exists(configured)) return configured;
            string candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            return File.Exists(candidate) ? candidate : "";
        }
        internal static string SystemProxy()
        {
            #if UNITY_EDITOR_WIN
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            if (Convert.ToInt32(Microsoft.Win32.Registry.GetValue(key, "ProxyEnable", 0)) != 1) return "";
            string value = Microsoft.Win32.Registry.GetValue(key, "ProxyServer", "") as string ?? "";
            if (value.Contains("=")) value = value.Split(';').FirstOrDefault(v => v.StartsWith("https=", StringComparison.OrdinalIgnoreCase))?.Substring(6) ?? "";
            if (!string.IsNullOrEmpty(value) && !value.Contains("://")) value = "http://" + value;
            return value;
            #else
            return Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? "";
            #endif
        }
        internal static bool Start(PsdToolSettings settings)
        {
            string executable = Executable();
            if (string.IsNullOrEmpty(executable)) throw new InvalidOperationException("未找到 Ollama，请先安装，或选择现有的 ollama.exe。");
            string proxy = EditorPrefs.GetString(ProxyPref, "").Trim();
            if (!string.IsNullOrEmpty(proxy) && (!Uri.TryCreate(proxy, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")))
                throw new InvalidOperationException("下载代理请使用 http://主机:端口 格式，只作用于本工具启动的 Ollama 进程。");
            string root = Path.GetDirectoryName(Path.GetFullPath(executable)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var processes = Process.GetProcessesByName("ollama").Concat(Process.GetProcessesByName("ollama app")).ToArray();
            try
            {
                if (processes.Length > 0 && !EditorUtility.DisplayDialog("重新启动本机 Ollama", "将结束所选安装目录内的 Ollama 服务/桌面程序，并以当前下载代理设置启动后台服务。其他应用正在使用该服务的推理会中断。系统代理不修改。", "重启", "取消")) return false;
                foreach (var process in processes)
                {
                    string path;
                    try { path = process.MainModule.FileName; } catch (Exception) { throw new InvalidOperationException("无法确认现有 Ollama 的安装位置，请先手动退出 Ollama 后重试。"); }
                    if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("检测到另一个安装目录的 Ollama，请手动退出，不会自动结束它。");
                }
                foreach (var process in processes.OrderBy(p => p.ProcessName == "ollama app" ? 0 : 1))
                    if (!process.HasExited) { process.Kill(); if (!process.WaitForExit(3000)) throw new InvalidOperationException("旧服务尚未退出，请稍后重试。"); }
                var info = new ProcessStartInfo(executable, "serve") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                if (!string.IsNullOrEmpty(proxy)) info.EnvironmentVariables["HTTPS_PROXY"] = proxy;
                info.EnvironmentVariables["OLLAMA_NO_CLOUD"] = "1";
                info.EnvironmentVariables["OLLAMA_HOST"] = AiProtocol.Endpoint(settings).GetLeftPart(UriPartial.Authority);
                using (var process = Process.Start(info)) { }
                return true;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
