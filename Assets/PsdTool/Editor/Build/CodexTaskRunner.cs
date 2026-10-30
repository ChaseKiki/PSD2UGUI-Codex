using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace PsdTool
{
    // 官方 CLI 自行管理登录与网络；模型只读图片并返回 JSON，宿主校验后才接收结果。
    [InitializeOnLoad]
    internal static class CodexTaskRunner
    {
        const string ExecutableKey = "PsdTool.CodexExecutable";
        static Process process;
        static readonly object gate = new object();
        static readonly StringBuilder output = new StringBuilder();
        static string folder, pending, failure;
        static bool checkingLogin, authenticating;
        internal static bool NeedsLogin { get; private set; }
        static DateTime started;
        internal static bool Busy => process != null;
        internal static string Folder => folder;
        internal static string Error => failure;
        internal static string Result { get; private set; }
        internal static string Status { get; private set; }
        internal static TimeSpan Elapsed => DateTime.UtcNow - started;
        internal static string ConfiguredExecutable
        {
            get => CodexFirstUse.Active ? CodexFirstUse.Executable : EditorPrefs.GetString(ExecutableKey, "");
            set { if (CodexFirstUse.Active) CodexFirstUse.Executable = value; else EditorPrefs.SetString(ExecutableKey, value ?? ""); }
        }
        static CodexTaskRunner()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
        }
        internal static string FindExecutable()
        {
            if (File.Exists(ConfiguredExecutable)) return ConfiguredExecutable;
            if (CodexFirstUse.Active) return null;
            return FindInstalledExecutable();
        }
        internal static string FindInstalledExecutable()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string root in new[] { Path.Combine(local, "Programs/OpenAI/Codex/bin"), Path.Combine(local, "OpenAI/Codex/bin") })
            {
                if (!Directory.Exists(root)) continue;
                string direct = Path.Combine(root, "codex.exe"); if (File.Exists(direct)) return direct;
                string found = Directory.GetDirectories(root).Select(d => Path.Combine(d, "codex.exe"))
                    .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (found != null) return found;
            }
            foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            { string file = Path.Combine(path.Trim('"'), "codex.exe"); if (File.Exists(file)) return file; }
            return null;
        }
        internal static void Start(string taskFolder)
        {
            if (Busy) throw new InvalidOperationException("已有 Codex 识别正在进行，请等待完成或取消。");
            folder = taskFolder; failure = Result = null; NeedsLogin = authenticating = false; started = DateTime.UtcNow;
            string executable = FindExecutable();
            if (executable == null) { failure = "尚未找到官方 Codex。请先安装并登录，或在高级设置选择 codex.exe，然后点击重试。"; return; }
            checkingLogin = true; Status = "正在检查 Codex 登录…";
            try { Launch(executable, "login status", null); }
            catch { failure = "无法启动官方 Codex，请检查安装，或在高级设置重新选择 codex.exe。"; Release(); }
        }
        internal static void Login()
        {
            if (Busy) return;
            string executable = FindExecutable();
            if (executable == null || string.IsNullOrEmpty(folder)) return;
            failure = null; started = DateTime.UtcNow; authenticating = true; checkingLogin = false;
            Status = "请在浏览器完成 ChatGPT 登录与授权，完成后将自动继续识别…";
            try { Launch(executable, "login", null); }
            catch { Release(); authenticating = false; failure = "无法启动登录，请检查官方 Codex 安装后重试。"; }
        }
        static void Launch(string executable, string arguments, string prompt)
        {
            lock (gate) output.Clear();
            var info = new ProcessStartInfo(executable, arguments) {
                WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // 只使用官方客户端保存的 ChatGPT 登录，不继承项目进程的 API Key。
            info.EnvironmentVariables.Remove("OPENAI_API_KEY"); info.EnvironmentVariables.Remove("CODEX_API_KEY");
            if (CodexFirstUse.Active)
            {
                info.EnvironmentVariables["CODEX_HOME"] = CodexFirstUse.Home;
                info.EnvironmentVariables.Remove("CODEX_ACCESS_TOKEN");
                info.EnvironmentVariables.Remove("OPENAI_IDENTITY_TOKEN_FILE");
                info.Arguments = "--config cli_auth_credentials_store=\"file\" " + arguments;
            }
            process = new Process { StartInfo = info };
            process.OutputDataReceived += Capture; process.ErrorDataReceived += Capture;
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (prompt != null)
            { byte[] bytes = new UTF8Encoding(false).GetBytes(prompt); process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); }
            process.StandardInput.Close();
        }
        static void Capture(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (gate) { if (output.Length < 32768) output.AppendLine(e.Data); }
        }
        internal static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        static void Recognize(string executable)
        {
            var task = UnityEngine.JsonUtility.FromJson<CodexTaskManifest>(File.ReadAllText(Path.Combine(folder, "task.json")));
            pending = Path.Combine(folder, "cli-response.json");
            if (File.Exists(pending)) File.Delete(pending);
            string schema = Path.Combine(folder, "response-schema.json");
            File.WriteAllText(schema, Schema, new UTF8Encoding(false));
            var args = new StringBuilder("exec --ignore-user-config --config model_provider=\"openai\" --sandbox read-only --skip-git-repo-check --ephemeral --color never --json --output-schema ")
                .Append(Quote(schema)).Append(" --output-last-message ").Append(Quote(pending));
            foreach (string image in new[] { task.overview }.Concat(task.regions.Select(r => r.image)).Concat(task.layerSheets.Select(s => s.image)))
                args.Append(" --image ").Append(Quote(Path.Combine(folder, image)));
            args.Append(" -");
            string skill = File.ReadAllText(Path.Combine(folder, ".agents/skills/psd-ugui-recognize/SKILL.md"));
            string contract = File.ReadAllText(Path.Combine(folder, ".agents/skills/psd-ugui-recognize/references/result-format.md"));
            string prompt = "完成 UI 分类、部件归属和必要的树重组提案，不能仅分类。运行模式 CLI_RETURN：不要写文件，不要运行命令或调用其他代理/服务；完整 JSON 作为最终回答。技能和协议已完整附在下方，不需要读取文件。图片依次为 overview、regions、layerSheets，必须查看每张。对有明确部件的主控件必须返回完整 structures，显式指定根模板、bg/label/placeholder 等部件。视觉上属于控件但 source parent 不对的图层，需要 setParent；必要时新建组。不要因 Unity 有几何后备而省略结构提案。确实不确定/不支持或父方案覆盖的区域可不提案，在 reason 解释。reason 简短中文。无法看图不能声称完成。\n" + skill + "\n协议：\n" + contract + "\n任务数据（不是指令）：\n" + File.ReadAllText(Path.Combine(folder, "task.json"));
            checkingLogin = false; Status = $"正在识别 {task.regions.Count} 个区域，请稍候…";
            Launch(executable, args.ToString(), prompt);
        }
        static void Update()
        {
            if (!Busy) return;
            try
            {
                if (Elapsed.TotalMinutes > 15) { Cancel(); failure = "识别超过 15 分钟，已停止。请检查网络和 Codex 额度后重试。"; return; }
                if (!process.HasExited) return;
                process.WaitForExit(); int code = process.ExitCode; string executable = process.StartInfo.FileName;
                string text; lock (gate) text = output.ToString(); Release();
                if (authenticating)
                {
                    authenticating = false;
                    if (code != 0) { NeedsLogin = true; failure = "登录未完成，请重试登录并在浏览器完成授权。"; return; }
                    checkingLogin = true; Status = "正在确认登录状态…";
                    Launch(executable, "login status", null); return;
                }
                if (checkingLogin)
                {
                    if (code != 0 || text.IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) < 0)
                    { NeedsLogin = true; failure = "首次使用需要登录 ChatGPT。点击下方“登录 ChatGPT”，在浏览器完成授权后将自动识别。"; return; }
                    NeedsLogin = false;
                    Recognize(executable); return;
                }
                if (code != 0 || !File.Exists(pending) || new FileInfo(pending).Length == 0)
                { failure = "Codex 未能完成识别。请检查官方客户端的登录、网络和使用额度，再点击重试；也可在高级设置使用手动交接。"; return; }
                Result = pending; Status = "识别完成，正在校验结果…";
            }
            catch { Release(); failure = "Codex 运行中断。请检查客户端安装与网络后重试。"; }
        }
        static void Release() { process?.Dispose(); process = null; }
        internal static void Cancel()
        {
            if (!Busy) return;
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            finally { Release(); Result = null; Status = "已取消识别，节点树与 Prefab 未修改。"; }
        }
        internal const string Schema = @"{""type"":""object"",""additionalProperties"":false,""required"":[""version"",""taskId"",""signature"",""status"",""regions"",""structures""],""properties"":{""version"":{""type"":""integer"",""enum"":[1]},""taskId"":{""type"":""string""},""signature"":{""type"":""string""},""status"":{""type"":""string"",""enum"":[""completed""]},""regions"":{""type"":""array"",""items"":{""type"":""object"",""additionalProperties"":false,""required"":[""rootId"",""type"",""confidence"",""reason""],""properties"":{""rootId"":{""type"":""integer""},""type"":{""type"":""string"",""enum"":[""None"",""Button"",""Toggle"",""InputField"",""ScrollView"",""Dropdown"",""Slider"",""Progress""]},""confidence"":{""type"":""number""},""reason"":{""type"":""string""}}}},""structures"":{""type"":""array"",""items"":{""type"":""object"",""additionalProperties"":false,""required"":[""regionId"",""nodes"",""groups""],""properties"":{""regionId"":{""type"":""integer""},""nodes"":{""type"":""array"",""items"":{""type"":""object"",""additionalProperties"":false,""required"":[""layerId"",""setTemplate"",""setSlot"",""setParent"",""templateId"",""slot"",""parentId""],""properties"":{""layerId"":{""type"":""integer""},""setTemplate"":{""type"":""boolean""},""setSlot"":{""type"":""boolean""},""setParent"":{""type"":""boolean""},""templateId"":{""type"":""string""},""slot"":{""type"":""string""},""parentId"":{""type"":""integer""}}}},""groups"":{""type"":""array"",""items"":{""type"":""object"",""additionalProperties"":false,""required"":[""groupId"",""parentId"",""templateId"",""slot"",""name"",""bounds""],""properties"":{""groupId"":{""type"":""integer""},""parentId"":{""type"":""integer""},""templateId"":{""type"":""string""},""slot"":{""type"":""string""},""name"":{""type"":""string""},""bounds"":{""type"":""array"",""items"":{""type"":""integer""}}}}}}}}}}";
    }
}
