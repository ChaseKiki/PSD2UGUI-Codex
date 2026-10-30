using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    // Task snapshots only. No AssetDatabase imports and no modification of the PSD.
    internal sealed class AiReviewImages : IDisposable
    {
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly Queue<string> order = new Queue<string>();
        readonly int maxSide;
        internal AiReviewImages(int maxSide = 1536) { this.maxSide = maxSide; }
        internal CodexTaskManifest manifest;
        internal string folder;
        internal void Load(string value)
        {
            if (folder == value) return;
            Dispose(); folder = value;
            try
            {
                var path = Path.Combine(folder ?? "", "task.json");
                if (File.Exists(path) && new FileInfo(path).Length <= 2 * 1024 * 1024)
                    manifest = JsonUtility.FromJson<CodexTaskManifest>(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) { manifest = null; }
        }
        internal Texture2D Region(int id) => Read("regions/" + id + ".png");
        internal Texture2D Overview() => Read("overview.png");
        Texture2D Read(string relative)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            if (textures.TryGetValue(relative, out var cached)) return cached;
            Texture2D texture = null;
            try
            {
                var path = Path.Combine(folder, relative);
                if (File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 * 1024)
                {
                    texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (!texture.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.DestroyImmediate(texture); texture = null; }
                    if (texture != null && Mathf.Max(texture.width, texture.height) > maxSide)
                    {
                        int originalWidth = texture.width, originalHeight = texture.height;
                        float scale = maxSide / (float)Mathf.Max(originalWidth, originalHeight);
                        int width = Mathf.Max(1, Mathf.RoundToInt(originalWidth * scale)), height = Mathf.Max(1, Mathf.RoundToInt(originalHeight * scale));
                        var pixels = texture.GetPixels32(); var small = new Color32[width * height];
                        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                            small[y * width + x] = pixels[(y * originalHeight / height) * originalWidth + x * originalWidth / width];
                        texture.Reinitialize(width, height); texture.SetPixels32(small); texture.Apply();
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); texture = null; }
            // Bound memory even for large tasks; missing images are cached too.
            while (order.Count >= (maxSide <= 256 ? 104 : 4))
            {
                string old = order.Dequeue();
                if (textures[old] != null) UnityEngine.Object.DestroyImmediate(textures[old]);
                textures.Remove(old);
            }
            textures[relative] = texture; order.Enqueue(relative); return texture;
        }
        internal List<CodexTaskLayer> Members(AiControlProposal proposal)
        {
            var ids = new HashSet<int>((proposal?.nodes ?? new List<AiNodeEdit>()).Where(n => n != null).Select(n => n.layerId));
            return (manifest?.layers ?? new List<CodexTaskLayer>()).Where(n => ids.Contains(n.id)).ToList();
        }
        public void Dispose()
        {
            foreach (var texture in textures.Values) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            textures.Clear(); order.Clear(); manifest = null; folder = null;
        }
    }

    internal sealed class AiRegionPreviewWindow : EditorWindow
    {
        readonly AiReviewImages images = new AiReviewImages();
        AiReviewState state;
        AiRegionResult region;
        AiControlProposal proposal;
        bool overview;
        internal static void Open(AiReviewState state, AiRegionResult region, AiControlProposal proposal)
        {
            var window = GetWindow<AiRegionPreviewWindow>("PSD 区域定位");
            window.state = state; window.region = region; window.proposal = proposal; window.overview = false;
            window.images.Load(state.reportFolder); window.minSize = new Vector2(480, 400); window.Show();
        }
        void OnDisable() => images.Dispose();
        void OnGUI()
        {
            if (state?.tree == null || region == null) { PsdEditorUI.Header("区域预览", "从识别结果点击缩略图打开。"); return; }
            PsdEditorUI.Header(state.tree.Find(region.rootId)?.goName ?? "未定位区域", "识别时的 PSD 快照；黄色框为候选区域，蓝色框为建议涉及的源图层。人工改动后快照不会更新。");
            using (new EditorGUILayout.HorizontalScope())
            {
                overview = GUILayout.Toolbar(overview ? 1 : 0, new[] { "区域放大", "整图定位 / 跨组部件" }) == 1;
                using (new EditorGUI.DisabledScope(state.tree.Find(region.rootId) == null))
                    if (GUILayout.Button("定位节点", GUILayout.Width(90))) PsdToolWindow.FocusNode(state.tree, state.settings, region.rootId);
            }
            EditorGUILayout.LabelField(state.NodePath(region.rootId), EditorStyles.wordWrappedMiniLabel);
            var texture = overview ? images.Overview() : images.Region(region.rootId);
            var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(.13f, .13f, .13f));
            if (texture == null) { GUI.Label(area, "未保存此区域图片，请定位节点手动检查。", EditorStyles.centeredGreyMiniLabel); return; }
            var fitted = Fit(area, texture.width, texture.height);
            GUI.DrawTexture(fitted, texture, ScaleMode.ScaleToFit);
            if (!overview) return;
            var crop = images.manifest?.regions?.Find(r => r.rootId == region.rootId)?.rect;
            // Canvas size is stored on the source tree, snapshot coordinates are PSD top-left pixels.
            float width = state.tree.docSize.x, height = state.tree.docSize.y;
            if (width <= 0 || height <= 0) return;
            foreach (var member in images.Members(proposal)) Box(fitted, member.rect, width, height, new Color(.2f, .75f, 1f));
            Box(fitted, crop, width, height, Color.yellow);
        }
        internal static Rect Fit(Rect area, float width, float height)
        {
            float scale = Mathf.Min(area.width / width, area.height / height);
            return new Rect(area.center.x - width * scale / 2, area.center.y - height * scale / 2, width * scale, height * scale);
        }
        static void Box(Rect area, int[] source, float width, float height, Color color)
        {
            if (source == null || source.Length < 4 || source[2] <= 0 || source[3] <= 0) return;
            float left = Mathf.Clamp01(source[0] / width), top = Mathf.Clamp01(source[1] / height);
            float right = Mathf.Clamp01((source[0] + source[2]) / width), bottom = Mathf.Clamp01((source[1] + source[3]) / height);
            var rect = Rect.MinMaxRect(area.x + left * area.width, area.y + top * area.height, area.x + right * area.width, area.y + bottom * area.height);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
        }
    }
}
