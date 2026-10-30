using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using PsdTool.Parser;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    /// <summary>通过 Photoshop 执行随工具分发的 JSX，读取临时 PNG 和效果边界。</summary>
    public static class PhotoshopFxBaker
    {
        public const string ExecutablePref = "PsdTool.PhotoshopExecutable";
        [Serializable] public sealed class Entry
        {
            public int layerId;
            public string path;
            public int x, y, width, height;
        }
        [Serializable] sealed class Result
        {
            public bool success;
            public string error;
            public List<Entry> entries = new List<Entry>();
        }
        [Serializable] sealed class Job
        {
            public string sourcePath, outputFolder, resultPath;
            public int[] layerIds;
        }

        public static Dictionary<int, Entry> Prepare(PsdDocument doc, List<PsdNode> targets, PsdToolSettings settings)
        {
            var ids = new List<int>();
            foreach (var node in targets)
            {
                var layer = doc.FindLayer(node.layerId);
                if (layer == null || !(layer.HasEffects || (Markers.Has(node.markers, "merge") && HasEffects(layer)))) continue;
                if (!settings.bakeImageEffects) continue;
                if (layer.Clipping || (layer.BlendKey != "norm" && layer.BlendKey != "pass"))
                    throw new InvalidOperationException("图片“" + node.goName + "”的 FX 依赖剪贴/混合背景。请将依赖的图层组织成 @merge 组后导出，不能按单层烘焙。");
                ids.Add(layer.Id);
            }
            if (ids.Count == 0) return new Dictionary<int, Entry>();
            ids.Sort();
            string jsx = FindScript();
            string signature;
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(doc.FilePath))
                signature = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            using (var sha = SHA256.Create())
                signature = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature + File.ReadAllText(jsx) + string.Join(",", ids)))).Replace("-", "");
            string cache = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/PsdTool/PhotoshopFx", signature));
            string resultPath = Path.Combine(cache, "result.json");
            if (File.Exists(resultPath))
            {
                var cached = ReadResult(resultPath);
                if (cached.success && cached.entries.Count == ids.Count && cached.entries.All(e => File.Exists(e.path)))
                    return cached.entries.ToDictionary(e => e.layerId);
                File.Delete(resultPath);
            }
            Directory.CreateDirectory(cache);
            var job = new Job { sourcePath = Path.GetFullPath(doc.FilePath).Replace('\\', '/'),
                outputFolder = cache.Replace('\\', '/'), resultPath = resultPath.Replace('\\', '/'), layerIds = ids.ToArray() };
            string launcher = Path.Combine(cache, "job.jsx");
            var scriptRef = new ScriptPath { path = jsx.Replace('\\', '/') };
            // JsonUtility 负责字符串转义，不将 PSD 名称拼成可执行代码。
            string quoted = JsonUtility.ToJson(scriptRef);
            File.WriteAllText(launcher, "#target photoshop\nvar psdToolScript=" + quoted + ";\n$.evalFile(new File(psdToolScript.path));\nPsdBakeEffects.run(" + JsonUtility.ToJson(job) + ");\n", new UTF8Encoding(false));
            string executable = ResolveExecutable();
            if (string.IsNullOrEmpty(executable) && !Application.isBatchMode)
            {
                executable = EditorUtility.OpenFilePanel("选择 Photoshop.exe（只需设置一次）", "", "exe");
                if (!string.IsNullOrEmpty(executable)) EditorPrefs.SetString(ExecutablePref, executable);
            }
            if (!File.Exists(executable)) throw new InvalidOperationException("未找到 Photoshop。请在工具设置底部选择 Photoshop.exe；图片 FX 烘焙未执行，未导出本次图片。");
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(executable, "-r \"" + launcher + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    var watch = Stopwatch.StartNew();
                    int timeout = Mathf.Max(30, settings.photoshopTimeoutSeconds);
                    while (!File.Exists(resultPath))
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Photoshop 图片 FX 烘焙", "仅操作临时副本，等待 Photoshop 完成…", (float)watch.Elapsed.TotalSeconds / timeout))
                            throw new OperationCanceledException("已取消等待；Photoshop 会完成自己的临时文档清理。");
                        if (watch.Elapsed.TotalSeconds > timeout)
                            throw new TimeoutException("Photoshop 烘焙等待超时，请检查 Photoshop 是否被对话框阻塞。原 PSD 不会被工具保存。");
                        Thread.Sleep(100);
                    }
                }
                // 等待写入完成（文件存在并不表示 JSX 已关闭输出流）。
                Result result = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try { result = ReadResult(resultPath); if (result != null && (result.success || !string.IsNullOrEmpty(result.error))) break; }
                    catch (Exception e) when (e is IOException || e is ArgumentException) { }
                    Thread.Sleep(50);
                }
                if (result == null || !result.success) throw new InvalidOperationException("Photoshop FX 烘焙失败：" + result?.error);
                if (result.entries.Count != ids.Count || result.entries.Any(e => !ids.Contains(e.layerId) || !File.Exists(e.path)))
                    throw new InvalidOperationException("Photoshop 烘焙结果缺少图层或 PNG，已停止导出。");
                return result.entries.ToDictionary(e => e.layerId);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [Serializable] sealed class ScriptPath { public string path; }
        static Result ReadResult(string path) => JsonUtility.FromJson<Result>(File.ReadAllText(path, Encoding.UTF8));
        static bool HasEffects(PsdLayer layer) => layer.HasEffects || layer.Children.Any(HasEffects);
        static string FindScript()
        {
            foreach (string guid in AssetDatabase.FindAssets("PsdBakeEffects"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)) return PathUtil.ToFullPath(path);
            }
            throw new FileNotFoundException("缺少 PsdTool/Photoshop/PsdBakeEffects.jsx，请完整复制工具目录。");
        }

        public static string ResolveExecutable()
        {
            string path = EditorPrefs.GetString(ExecutablePref, "");
            if (File.Exists(path)) return path;
            path = Environment.GetEnvironmentVariable("PSD_TOOL_PHOTOSHOP_EXE");
            if (File.Exists(path)) return path;
            foreach (var process in Process.GetProcessesByName("Photoshop"))
                using (process)
                    try { path = process.MainModule.FileName; if (File.Exists(path)) return path; }
                    catch (Exception) { }
            try
            {
                path = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Photoshop.exe", "", null) as string;
                if (File.Exists(path)) return path;
            }
            catch (Exception) { }
            string adobe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe");
            if (Directory.Exists(adobe))
                foreach (string dir in Directory.GetDirectories(adobe, "*Photoshop*"))
                    if (File.Exists(Path.Combine(dir, "Photoshop.exe"))) return Path.Combine(dir, "Photoshop.exe");
            return string.Empty;
        }
    }
}
