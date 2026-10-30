using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PsdTool
{
    /// <summary>
    /// 节点树 → uGUI prefab。第三阶段：控件模板、锚点、SafeArea、文字描边投影。
    /// 在临时场景生成候选树，再按持久标识增量合并到已有 Prefab。
    /// </summary>
    public static class PrefabGenerator
    {
        const string SafeAreaName = "SafeArea";

        public static GameObject Generate(PsdTree tree, PsdToolSettings settings)
            => Generate(tree, settings, null);

        internal static GameObject Generate(PsdTree tree, PsdToolSettings settings, List<GenerationIssue> issues)
        {
            issues?.Clear();
            tree.warnings.RemoveAll(w => w.StartsWith("[交互待处理]", System.StringComparison.Ordinal));
            using (var draft = GenerationPreflight.Build(tree, settings, issues))
                return PrefabIncrementalImport.Apply(draft.Root, tree, settings);
        }

        public sealed class Draft : System.IDisposable
        {
            public GameObject Root;
            public Scene Scene;
            internal readonly List<GenerationIssue> Repairs = new List<GenerationIssue>();
            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
            }
        }

        public static Draft BuildDraft(PsdTree tree, PsdToolSettings settings)
        {
            var ctx = new ControlBuildContext(tree, settings);
            var previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(tree.panelName, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var rt = (RectTransform)root.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;

                Transform contentParent = root.transform;
                var docRect = new Rect(0, 0, tree.docSize.x, tree.docSize.y);
                if (NeedsSafeArea(ctx))
                {
                    var safe = new GameObject(SafeAreaName, typeof(RectTransform), typeof(SafeAreaFitter));
                    safe.transform.SetParent(root.transform, false);
                    var srt = (RectTransform)safe.transform;
                    srt.anchorMin = Vector2.zero;
                    srt.anchorMax = Vector2.one;
                    srt.offsetMin = srt.offsetMax = Vector2.zero;
                    contentParent = safe.transform;
                }

                CreateChildren(ctx, -1, contentParent, root.transform, docRect);
                ControlReferenceValidator.EnsureValid(root, ctx);

                string sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tree));
                if (string.IsNullOrEmpty(sourceGuid)) sourceGuid = "unsaved-test-source";
                Stamp(root, sourceGuid, "root");
                foreach (var node in tree.nodes)
                    if (ctx.Map.TryGetValue(node.layerId, out var mapped))
                        Stamp(mapped, sourceGuid, string.IsNullOrEmpty(node.stableKey) ? "layer:" + node.layerId : node.stableKey);
                StampHelpers(root.transform, sourceGuid);
                var draft = new Draft { Root = root, Scene = previewScene };
                draft.Repairs.AddRange(ctx.Repairs);
                return draft;
            }
            catch
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
                throw;
            }
        }

        internal static void Stamp(GameObject go, string source, string key)
        {
            var id = go.GetComponent<PsdGeneratedNode>() ?? go.AddComponent<PsdGeneratedNode>();
            id.hideFlags |= HideFlags.HideInInspector;
            id.sourceGuid = source;
            id.key = key;
        }

        internal static void StampHelpers(Transform parent, string source)
        {
            var counts = new Dictionary<string, int>();
            foreach (Transform child in parent)
            {
                counts.TryGetValue(child.name, out int index);
                counts[child.name] = index + 1;
                if (child.GetComponent<PsdGeneratedNode>() == null)
                    Stamp(child.gameObject, source, parent.GetComponent<PsdGeneratedNode>().key + "/" + child.name + "#" + index);
                StampHelpers(child, source);
            }
        }

        internal static bool NeedsSafeArea(ControlBuildContext ctx)
        {
            foreach (var n in ctx.Tree.nodes)
            {
                if (n.status == NodeStatus.PendingDelete || n.parentId != -1) continue;
                if (n.underSafeArea) return true;
            }
            return false;
        }

        internal static Rect? RectOf(ControlBuildContext ctx, PsdNode node)
        {
            if (node == null) return null;
            if (ctx.Rects.TryGetValue(node.layerId, out var cached)) return cached;

            Rect? result = null;
            switch (node.kind)
            {
                case LayerKind.Text:
                    if (node.text != null && !string.IsNullOrEmpty(node.text.content) && node.text.rect.width > 0)
                        result = node.text.rect;
                    break;
                case LayerKind.Group:
                {
                    var children = ctx.ChildrenOf(node.layerId);
                    // Button hit area follows all normal source visuals, including labels
                    // outside the background and visuals inside otherwise empty wrappers.
                    if (node.templateId == "Button")
                    {
                        result = ButtonVisualRect(ctx, node);
                        break;
                    }
                    if (node.aiGeneratedGroup && node.aiBoundsOverride && node.slot == "viewport")
                    {
                        result = new Rect(node.psdRect.x, node.psdRect.y, node.psdRect.width, node.psdRect.height);
                        break;
                    }
                    // Toggle/Tab 的背景可能只是勾选框，点击范围应覆盖完整标签和选中态。
                    if (node.templateId == "Toggle" || node.templateId == "Tab" || ctx.DropdownItems.Contains(node.layerId))
                    {
                        result = DescendantRect(ctx, node);
                        break;
                    }
                    var rectChild = children.Find(c => c.slot == "rect" || c.slot == "hit");
                    if (rectChild != null)
                    {
                        result = RectOf(ctx, rectChild) ?? new Rect(rectChild.psdRect.x, rectChild.psdRect.y, rectChild.psdRect.width, rectChild.psdRect.height);
                        break;
                    }

                    if (node.templateId == "ScrollView" || ctx.DropdownTemplates.Contains(node.layerId))
                    {
                        result = ScrollViewBuilder.ScrollViewRootRect(ctx, children);
                        if (result != null) break;
                    }

                    if (IsControl(node.templateId))
                    {
                        var bg = children.Find(c => c.slot == "bg" || c.slot == "track" || c.slot == "viewport");
                        if (bg != null) result = RectOf(ctx, bg);
                    }

                    if (result == null)
                    {
                        foreach (var c in children)
                        {
                            if (IsStateSlot(c.slot)) continue;
                            var r = RectOf(ctx, c);
                            if (r == null) continue;
                            result = result == null ? r : Union(result.Value, r.Value);
                        }
                    }
                    break;
                }
                default:
                    if (node.image != null && !node.image.isEmpty && node.image.trimmedRect.width > 0)
                    {
                        var t = node.image.fxBaked && node.image.logicalRect.width > 0 ? node.image.logicalRect : node.image.trimmedRect;
                        result = new Rect(t.x, t.y, t.width, t.height);
                    }
                    break;
            }

            ctx.Rects[node.layerId] = result;
            return result;
        }

        internal static bool IsControl(string id) =>
            !string.IsNullOrEmpty(id) && id != "Group" && id != "Prefab";

        static Rect? ButtonVisualRect(ControlBuildContext ctx, PsdNode group)
        {
            Rect? result = null;
            foreach (var child in ctx.ChildrenOf(group.layerId))
            {
                if (!child.visible || IsStateSlot(child.slot) || child.slot == "rect" || child.slot == "hit") continue;
                var rect = child.kind == LayerKind.Group ? ButtonVisualRect(ctx, child) : RectOf(ctx, child);
                if (rect.HasValue) result = result.HasValue ? Union(result.Value, rect.Value) : rect;
            }
            return result;
        }

        static Rect? DescendantRect(ControlBuildContext ctx, PsdNode group)
        {
            Rect? result = null;
            foreach (var child in ctx.ChildrenOf(group.layerId))
            {
                var rect = child.kind == LayerKind.Group ? DescendantRect(ctx, child) : RectOf(ctx, child);
                if (rect.HasValue) result = result.HasValue ? Union(result.Value, rect.Value) : rect;
            }
            return result;
        }

        internal static bool IsStateSlot(string slot) =>
            slot == "pressed" || slot == "disabled";

        internal static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        internal static void CreateChildren(ControlBuildContext ctx, int parentId, Transform contentParent, Transform rootParent, Rect parentRect)
        {
            foreach (var node in ctx.ChildrenOf(parentId))
            {
                if (node.slot == "rect" || IsStateSlot(node.slot)) continue;

                var rect = RectOf(ctx, node);
                if (rect == null && node.kind != LayerKind.Group && !IsControl(node.templateId)) continue;

                Transform parent = contentParent;
                if (parentId < 0 && ShouldUnderSafe(ctx, node, rect))
                {
                    var safe = rootParent.Find(SafeAreaName);
                    if (safe != null) parent = safe;
                }

                var go = new GameObject(node.goName, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var r = rect ?? new Rect(parentRect.center, Vector2.zero);
                AnchorUtil.Apply((RectTransform)go.transform, r, parentRect, node.anchorPreset, ctx.Settings.anchorSnap);
                ctx.Map[node.layerId] = go;

                BuildNodeContent(ctx, node, go, r);

                if (!node.visible) go.SetActive(false);
            }

            ToggleBuilder.WireToggleGroups(ctx, parentId);
        }

        internal static void BuildNodeContent(ControlBuildContext ctx, PsdNode node, GameObject go, Rect bounds)
        {
            if (IsControl(node.templateId)) ctx.ControlOwners[go] = node.layerId;
            if (node.kind == LayerKind.Group && node.opacity < 0.999f)
            {
                // Unity 的缺失组件可能是托管非 null 的“假空”对象，不能使用 ?? 判断。
                var opacity = go.GetComponent<CanvasGroup>();
                if (opacity == null) opacity = go.AddComponent<CanvasGroup>();
                opacity.alpha = node.opacity;
            }
            try { if (ControlBuilderRegistry.TryBuild(ctx, node, go, bounds)) return; }
            catch (ControlBuildException) { throw; }
            catch (System.InvalidOperationException e) when (IsControl(node.templateId))
            { throw new ControlBuildException(node.layerId, e.Message); }
            if (node.kind == LayerKind.Group) CreateVisualChildren(ctx, node, go, bounds);
            else if (node.kind == LayerKind.Text) CreateText(ctx, node, go);
            else CreateImage(ctx, node, go);
        }

        internal static bool ShouldUnderSafe(ControlBuildContext ctx, PsdNode node, Rect? rect)
        {
            return node.underSafeArea;
        }

        internal static void CreateVisualChildren(ControlBuildContext ctx, PsdNode node, GameObject go, Rect parentRect)
        {
            CreateChildren(ctx, node.layerId, go.transform, go.transform, parentRect);
        }

        /// <summary>生成控件下除指定插槽外的装饰子节点（如 Slider 标题）。</summary>
        internal static void CreateDecorChildren(ControlBuildContext ctx, PsdNode node, GameObject go, Rect parentRect, int skipA, int skipB, params string[] skipSlots)
        {
            var skip = new HashSet<string>(skipSlots);
            foreach (var child in ctx.ChildrenOf(node.layerId))
            {
                if (child.layerId == skipA || child.layerId == skipB || skip.Contains(child.slot) || child.slot == "rect" || IsStateSlot(child.slot)) continue;
                var rect = RectOf(ctx, child);
                if (rect == null && child.kind != LayerKind.Group) continue;
                var childGo = new GameObject(child.goName, typeof(RectTransform));
                childGo.transform.SetParent(go.transform, false);
                var r = rect ?? new Rect(parentRect.center, Vector2.zero);
                AnchorUtil.Apply((RectTransform)childGo.transform, r, parentRect, child.anchorPreset, ctx.Settings.anchorSnap);
                ctx.Map[child.layerId] = childGo;
                BuildNodeContent(ctx, child, childGo, r);
                if (!child.visible) childGo.SetActive(false);
            }
        }

        internal static PsdNode Slot(ControlBuildContext ctx, PsdNode parent, string slot)
        {
            foreach (var c in ctx.ChildrenOf(parent.layerId))
                if (c.slot == slot) return c;
            return null;
        }

        internal static GameObject EnsureChild(Transform parent, string name, Rect r, Rect parentRect, string anchor = "c")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            AnchorUtil.Apply((RectTransform)go.transform, r, parentRect, anchor, 0f);
            return go;
        }

        internal static void CreateLooseChild(ControlBuildContext ctx, PsdNode child, Transform parent, Rect parentRect)
        {
            if (ctx.Map.ContainsKey(child.layerId) || child.slot == "rect" || IsStateSlot(child.slot)) return;
            var rect = RectOf(ctx, child);
            if (rect == null && child.kind != LayerKind.Group) return;
            var childGo = new GameObject(child.goName, typeof(RectTransform));
            childGo.transform.SetParent(parent, false);
            var r = rect ?? parentRect;
            AnchorUtil.Apply((RectTransform)childGo.transform, r, parentRect, child.anchorPreset, ctx.Settings.anchorSnap);
            ctx.Map[child.layerId] = childGo;
            BuildNodeContent(ctx, child, childGo, r);
            if (!child.visible) childGo.SetActive(false);
        }

        internal static void EnsureMinHit(GameObject go, Rect rootRect, int minSize)
        {
            if (rootRect.width >= minSize && rootRect.height >= minSize) return;
            var hit = go.GetComponent<NonDrawingGraphic>() ?? go.AddComponent<NonDrawingGraphic>();
            hit.raycastTarget = true;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, minSize), Mathf.Max(rt.sizeDelta.y, minSize));
        }

        internal static void CopyImageTo(ControlBuildContext ctx, PsdNode node, GameObject go)
        {
            if (node == null) return;
            if (ctx.Map.TryGetValue(node.layerId, out var existing) && existing != go)
            {
                // 已作为独立节点生成过：把它挪过来当子节点内容
                existing.transform.SetParent(go.transform, false);
                var ert = (RectTransform)existing.transform;
                ert.anchorMin = Vector2.zero;
                ert.anchorMax = Vector2.one;
                ert.offsetMin = ert.offsetMax = Vector2.zero;
                return;
            }
            ctx.Map[node.layerId] = go;
            if (node.kind == LayerKind.Group)
            {
                if (node.opacity < 0.999f) go.AddComponent<CanvasGroup>().alpha = node.opacity;
                CreateVisualChildren(ctx, node, go, RectOf(ctx, node) ?? new Rect());
            }
            else CreateImage(ctx, node, go);
        }

        internal static Sprite LoadSprite(PsdNode node)
        {
            if (node != null && node.resourceOverride is Sprite selected) return selected;
            if (node == null || string.IsNullOrEmpty(node.image.assetPath)) return null;
            if (!string.IsNullOrEmpty(node.image.assetGuid))
            {
                string p = AssetDatabase.GUIDToAssetPath(node.image.assetGuid);
                if (!string.IsNullOrEmpty(p)) return AssetDatabase.LoadAssetAtPath<Sprite>(p);
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(node.image.assetPath);
        }

        internal static void CreateImage(ControlBuildContext ctx, PsdNode node, GameObject go)
        {
            var info = node.image;
            if (info.fxBaked)
            {
                if (go.GetComponent<PsdVisualBoundsFilter>() == null) go.AddComponent<PsdVisualBoundsFilter>();
                var logical = info.logicalRect;
                var rendered = info.trimmedRect;
                // 布局仍用原矩形，FX 像素作为扩展的视觉子节点，阴影不撑大控件/热区。
                go = EnsureChild(go.transform, "FX Visual", new Rect(rendered.x, rendered.y, rendered.width, rendered.height),
                    new Rect(logical.x, logical.y, logical.width, logical.height), "stretch");
            }
            var tint = new Color(1f, 1f, 1f, info.fxBaked ? info.renderOpacity : node.opacity);

            if (info.isSolid)
            {
                var image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
                Color c = info.solidColor;
                c.a = node.opacity;
                image.color = c;
                image.raycastTarget = false;
                return;
            }

            if (info.useRawImage)
            {
                var raw = go.GetComponent<RawImage>() ?? go.AddComponent<RawImage>();
                raw.texture = node.resourceOverride as Texture2D ?? Load<Texture2D>(info.assetGuid, info.assetPath);
                raw.color = tint;
                raw.raycastTarget = false;
                if (raw.texture == null) ctx.Tree.warnings.Add($"“{node.goName}”的贴图加载失败：{info.assetPath}");
                return;
            }

            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.sprite = node.resourceOverride as Sprite ?? Load<Sprite>(info.assetGuid, info.assetPath);
            img.color = tint;
            img.raycastTarget = false;
            if (info.tiled) img.type = Image.Type.Tiled;
            else if (info.sliced || info.border != Vector4.zero) img.type = Image.Type.Sliced;
            if (img.sprite == null)
                ctx.Tree.warnings.Add($"“{node.goName}”的 Sprite 加载失败（检查导入类型是否为 Sprite）：{info.assetPath}");
        }

        internal static T Load<T>(string guid, string path) where T : Object
        {
            if (!string.IsNullOrEmpty(guid))
            {
                string byGuid = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(byGuid))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(byGuid);
                    if (asset != null) return asset;
                }
            }
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        internal static void CreateText(ControlBuildContext ctx, PsdNode node, GameObject go)
        {
            var info = node.text;
            var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();

            var font = ctx.Settings.FindFont(info.font, out bool mapped);
            if (!mapped && ctx.MissingFonts.Add(info.font))
                ctx.Tree.warnings.Add($"字体“{info.font}”没有映射到 TMP 字体资源，已使用默认字体。请在设置的字体映射表中添加。");
            if (font != null) tmp.font = font;

            tmp.fontSize = info.fontSize;
            Color color = info.color;
            color.a *= node.opacity;
            tmp.color = color;
            tmp.fontStyle = (info.bold ? FontStyles.Bold : 0) | (info.italic ? FontStyles.Italic : 0);
            tmp.characterSpacing = info.tracking / 10f;
            tmp.lineSpacing = LineSpacing(font, info);
            tmp.alignment = Alignment(info.justification, info.isBox);
            tmp.enableWordWrapping = info.isBox;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.text = BuildText(info);

            var mat = TmpMaterialFactory.Get(font, info, ctx.Settings);
            if (mat != null) tmp.fontSharedMaterial = mat;
        }

        internal static float LineSpacing(TMP_FontAsset font, TextInfo info)
        {
            if (info.autoLeading || info.leading <= 0 || font == null || info.fontSize <= 0) return 0f;
            var face = font.faceInfo;
            if (face.pointSize <= 0) return 0f;
            float natural = face.lineHeight / face.pointSize * info.fontSize;
            return (info.leading - natural) / info.fontSize * 100f;
        }

        internal static TextAlignmentOptions Alignment(int justification, bool box)
        {
            switch (justification)
            {
                case 1: return box ? TextAlignmentOptions.TopRight : TextAlignmentOptions.Right;
                case 2: return box ? TextAlignmentOptions.Top : TextAlignmentOptions.Center;
                case 3:
                case 4:
                case 5:
                case 6: return box ? TextAlignmentOptions.TopJustified : TextAlignmentOptions.Justified;
                default: return box ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Left;
            }
        }

        internal static string BuildText(TextInfo info)
        {
            string content = info.content ?? string.Empty;
            if (info.runs.Count <= 1) return Escape(content);

            var sb = new StringBuilder();
            foreach (var run in info.runs)
            {
                int start = Mathf.Clamp(run.start, 0, content.Length);
                int end = Mathf.Clamp(run.start + run.length, 0, content.Length);
                if (end <= start) continue;

                var open = new StringBuilder();
                var close = new StringBuilder();
                if (!SameColor(run.color, info.color))
                {
                    open.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(run.color)).Append('>');
                    close.Insert(0, "</color>");
                }
                if (Mathf.Abs(run.fontSize - info.fontSize) > 0.5f)
                {
                    open.Append("<size=").Append(run.fontSize.ToString("0.#", CultureInfo.InvariantCulture)).Append('>');
                    close.Insert(0, "</size>");
                }
                if (run.bold && !info.bold) { open.Append("<b>"); close.Insert(0, "</b>"); }
                if (run.italic && !info.italic) { open.Append("<i>"); close.Insert(0, "</i>"); }
                sb.Append(open).Append(Escape(content.Substring(start, end - start))).Append(close);
            }
            return sb.ToString();
        }

        internal static bool SameColor(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        internal static string Escape(string s) => s.Replace("<", "<noparse><</noparse>");
    }
}
