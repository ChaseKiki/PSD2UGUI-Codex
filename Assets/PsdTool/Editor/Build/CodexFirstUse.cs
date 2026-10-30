using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    // 仅当前 Unity 会话的首次体验，不注销桌面端，不读取或复制已有凭据。
    internal static class CodexFirstUse
    {
        const string Prefix = "PsdTool.FirstUse.";
        internal static bool Active => SessionState.GetBool(Prefix + "active", false);
        internal static string Home => SessionState.GetString(Prefix + "home", "");
        internal static string Executable
        {
            get => SessionState.GetString(Prefix + "executable", "");
            set => SessionState.SetString(Prefix + "executable", value ?? "");
        }
        internal static void Begin()
        {
            if (CodexTaskRunner.Busy) throw new InvalidOperationException("请先结束正在进行的 Codex 任务。");
            string home = Path.Combine(Path.GetTempPath(), "PsdToolFirstUse", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            SessionState.SetString(Prefix + "home", home); Executable = "";
            SessionState.SetBool(Prefix + "active", true);
            EditorApplication.ExecuteMenuItem("Tools/PSD 工具/打开工具");
            Debug.Log("已进入新电脑体验：安装检测从空开始，CLI 使用独立登录配置；当前 App 登录未修改。");
        }
        internal static void End()
        {
            CodexTaskRunner.Cancel();
            SessionState.SetBool(Prefix + "active", false); Executable = "";
            SessionState.EraseString(Prefix + "home");
            Debug.Log("已结束新电脑体验，工具恢复使用原来的客户端检测与登录配置。");
        }
    }
}
