using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class CodexTaskWindow : EditorWindow
    {
        [SerializeField] PsdTree tree;
        [SerializeField] PsdToolSettings settings;
        [SerializeField] string folder, status;
        [SerializeField] bool watch = true, error;
        double nextPoll, stableSince;
        long observedSize = -1, observedWrite = -1;
        string attemptedHash;
        Vector2 scroll;
        bool advanced;
        string cliAttempt;

        internal static void ExportAndOpen(PsdTree tree, PsdToolSettings settings)
        {
            if (CodexTaskRunner.Busy) throw new InvalidOperationException("已有 Codex 识别正在进行，请打开识别进度或等待完成。");
            string folder;
            try { EditorUtility.DisplayProgressBar("导出 Codex 识别任务", "读取当前 PSD、裁剪区域并生成随任务 skill", 0.3f); folder = CodexTaskBridge.Export(tree, settings); }
            finally { EditorUtility.ClearProgressBar(); }
            Open(tree, settings, folder);
            CodexTaskRunner.Start(folder);
        }
        internal static void Open(PsdTree tree, PsdToolSettings settings, string folder = null)
        {
            var window = GetWindow<CodexTaskWindow>("Codex 识别"); window.tree = tree; window.settings = settings;
            window.minSize = new Vector2(440, 240);
            window.folder = folder ?? CodexTaskBridge.Latest(tree); window.status = "准备就绪，点击开始识别。";
            window.error = false; window.cliAttempt = null; window.attemptedHash = null; window.observedSize = -1; window.Show();
        }
        void OnEnable() { EditorApplication.update += Poll; }
        void OnDisable() { EditorApplication.update -= Poll; }
        void Fail(Exception e) { status = e.Message; error = true; Repaint(); }
        void Execute(Action action) { try { action(); } catch (Exception e) { Fail(e); } }
        void Import(string selected = null)
        {
            var result = CodexTaskBridge.Import(folder, tree, settings, selected);
            AiMarker.ReviewExternal(tree, settings, result.plan, result.signature, result.note, folder);
            if (selected != null && !string.Equals(Path.GetFullPath(selected), Path.GetFullPath(Path.Combine(folder, "result.json")), StringComparison.OrdinalIgnoreCase))
                File.Copy(selected, Path.Combine(folder, "result.json"), true);
            CodexTaskBridge.MarkReviewed(folder, result.resultHash); attemptedHash = result.resultHash;
            status = "识别完成，请在结果列表勾选控件并生成 Prefab。"; error = false; Repaint();
        }
        void Poll()
        {
            if (!watch || tree == null || settings == null || string.IsNullOrEmpty(folder) || EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (CodexTaskRunner.Folder == folder)
            {
                if (CodexTaskRunner.Busy) { status = CodexTaskRunner.Status; Repaint(); return; }
                if (CodexTaskRunner.Error != null) { status = CodexTaskRunner.Error; error = true; Repaint(); }
                if (CodexTaskRunner.Result != null && cliAttempt != CodexTaskRunner.Result)
                {
                    cliAttempt = CodexTaskRunner.Result;
                    Execute(() => {
                        if (CodexTaskBridge.ReviewedHash(folder) == CodexTaskBridge.HashFile(cliAttempt))
                        { status = "本次结果已接收，可点击查看识别结果。"; return; }
                        Import(cliAttempt);
                    });
                    return;
                }
            }
            string path = Path.Combine(folder, "result.json");
            try
            {
                var file = new FileInfo(path); if (!file.Exists) return;
                if (file.Length != observedSize || file.LastWriteTimeUtc.Ticks != observedWrite)
                { observedSize = file.Length; observedWrite = file.LastWriteTimeUtc.Ticks; stableSince = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - stableSince < 1 || file.Length <= 0) return;
                if (file.Length > CodexTaskBridge.MaxJsonBytes) { if (attemptedHash != "oversize") { attemptedHash = "oversize"; Fail(new InvalidOperationException("结果超过 512KB，未导入。")); } return; }
                string hash = CodexTaskBridge.HashFile(path);
                if (attemptedHash == hash || CodexTaskBridge.ReviewedHash(folder) == hash) return;
                attemptedHash = hash; Import();
            }
            catch (IOException) { /* 等待 Codex 完成原子写入。 */ }
            catch (Exception e) { Fail(e); }
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            PsdEditorUI.Header(tree != null ? tree.panelName + " · Codex 识别" : "Codex 识别", "识别完成后自动打开结果列表；关闭此窗口不影响后台识别。");
            EditorGUILayout.BeginVertical(PsdEditorUI.Card);
            if (CodexFirstUse.Active)
                EditorGUILayout.HelpBox("新电脑体验中：初始不检测已有安装；登录使用独立配置，不影响当前 App。可在下方高级选项中结束体验。", MessageType.Info);
            bool running = CodexTaskRunner.Busy && CodexTaskRunner.Folder == folder;
            if (running)
            {
                EditorGUILayout.HelpBox(CodexTaskRunner.Status + $"\n已等待 {CodexTaskRunner.Elapsed:mm\\:ss}，完成后自动显示结果列表。", MessageType.Info);
                if (GUILayout.Button("取消识别")) { CodexTaskRunner.Cancel(); status = CodexTaskRunner.Status; }
                Repaint();
            }
            else
            {
                if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, error ? MessageType.Warning : MessageType.Info);
                string existingResult = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "result.json");
                if (existingResult != null && !File.Exists(existingResult)) existingResult = Path.Combine(folder, "cli-response.json");
                bool hasReview = AiMarker.Last != null && AiMarker.Last.tree == tree;
                if (!hasReview && existingResult != null && File.Exists(existingResult) && PsdEditorUI.Action("读取已有识别结果", true))
                    Execute(() => Import(existingResult));
                if (CodexTaskRunner.FindExecutable() == null)
                {
                    EditorGUILayout.LabelField("首次配置 · 第 1 步：安装官方 Codex", EditorStyles.boldLabel);
                    if (GUILayout.Button("打开官方安装指引", GUILayout.Height(28))) Application.OpenURL("https://learn.chatgpt.com/docs/cli");
                    if (GUILayout.Button("已安装，检测并继续", GUILayout.Height(28))) Execute(() => {
                        string executable = CodexTaskRunner.FindInstalledExecutable();
                        if (executable == null) { status = "未检测到 codex.exe；请完成安装，或使用下方按钮选择程序。"; error = true; return; }
                        CodexTaskRunner.ConfiguredExecutable = executable;
                        if (!string.IsNullOrEmpty(folder)) { cliAttempt = null; CodexTaskRunner.Start(folder); }
                    });
                    if (GUILayout.Button("选择已安装的 codex.exe")) SelectExecutable();
                }
                else if (CodexTaskRunner.NeedsLogin)
                {
                    EditorGUILayout.LabelField("首次配置 · 第 2 步：登录 ChatGPT", EditorStyles.boldLabel);
                    if (GUILayout.Button("登录 ChatGPT", GUILayout.Height(32))) Execute(CodexTaskRunner.Login);
                }
                if (AiMarker.Last?.tree == tree && AiMarker.Last != null && GUILayout.Button("查看识别结果", GUILayout.Height(30))) AiRecognitionWindow.Open(AiMarker.Last);
                if (CodexTaskRunner.FindExecutable() != null && !CodexTaskRunner.NeedsLogin)
                    using (new EditorGUI.DisabledScope(tree == null || settings == null || CodexTaskRunner.Busy))
                        if (PsdEditorUI.Action(error ? "重试识别" : hasReview ? "重新识别" : "开始识别", !hasReview)) Execute(() => ExportAndOpen(tree, settings));
                if (error && GUILayout.Button("查看官方 Codex 安装与登录说明")) Application.OpenURL("https://learn.chatgpt.com/docs/cli");
            }
            EditorGUILayout.EndVertical();
            PsdEditorUI.Divider("配置与维护");
            advanced = EditorGUILayout.Foldout(advanced, "高级 / 故障处理", true);
            if (advanced)
            {
                string existing = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "result.json");
                if (existing != null && File.Exists(existing) && PsdEditorUI.Action("重新校验已有结果（不再请求 AI）")) Execute(() => Import(existing));
                EditorGUILayout.ObjectField("节点树", tree, typeof(PsdTree), false);
                EditorGUILayout.ObjectField("配置", settings, typeof(PsdToolSettings), false);
                EditorGUILayout.LabelField("任务目录", folder ?? "尚未导出", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Codex", CodexTaskRunner.FindExecutable() ?? "未安装 / 未检测到", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("选择官方 codex.exe（仅本机）"))
                    SelectExecutable();
                using (new EditorGUI.DisabledScope(running || string.IsNullOrEmpty(folder) || !Directory.Exists(folder)))
                {
                    if (GUILayout.Button("手动模式：复制给 Codex 的指令")) { EditorGUIUtility.systemCopyBuffer = CodexTaskBridge.Prompt(folder); status = "已复制；在官方 Codex 粘贴并发送。"; error = false; }
                    if (GUILayout.Button("打开任务目录")) EditorUtility.RevealInFinder(folder);
                    if (GUILayout.Button("重新检查返回结果")) Execute(() => Import());
                    if (GUILayout.Button("选择回传 JSON"))
                    {
                        string selected = EditorUtility.OpenFilePanel("选择 Codex 返回的 result.json", folder, "json");
                        if (!string.IsNullOrEmpty(selected)) Execute(() => Import(selected));
                    }
                }
                EditorGUILayout.HelpBox("首次需要安装并以 ChatGPT 账号登录官方 Codex CLI。关闭窗口不会中断后台识别；重新编译或退出 Unity 会停止识别。失败不会应用任何控件计划。", MessageType.None);
                using (new EditorGUI.DisabledScope(CodexTaskRunner.Busy))
                {
                    if (CodexFirstUse.Active)
                    { if (GUILayout.Button("结束新电脑体验")) CodexFirstUse.End(); }
                    else if (GUILayout.Button("体验新电脑首次配置")) CodexFirstUse.Begin();
                }
            }
            EditorGUILayout.EndScrollView();
        }
        void SelectExecutable()
        {
            string executable = EditorUtility.OpenFilePanel("选择官方 Codex CLI", "", "exe");
            if (!string.IsNullOrEmpty(executable))
            {
                CodexTaskRunner.ConfiguredExecutable = executable;
                if (!CodexTaskRunner.Busy && !string.IsNullOrEmpty(folder)) Execute(() => { cliAttempt = null; CodexTaskRunner.Start(folder); });
            }
        }
    }
}
