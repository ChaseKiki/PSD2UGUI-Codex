using System;
using System.Collections.Generic;
using System.IO;
using PsdTool.Parser;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    public sealed class PsdToolWindow : EditorWindow
    {
        [SerializeField] PsdTree _tree;
        [SerializeField] string _psdPath = string.Empty;
        [SerializeField] string _moduleName = "Main";
        [SerializeField] string _panelName = string.Empty;

        PsdToolSettings _settings;
        PsdDocument _doc;
        string _docStamp;
        int _selected = -1;
        readonly HashSet<int> _collapsed = new HashSet<int>();
        Dictionary<int, List<PsdNode>> _children;
        Vector2 _treeScroll, _detailScroll, _reportScroll;
        [SerializeField] bool _maintenance;
        bool _sourceDetails, _showReport;
        GUIStyle _rowStyle, _selectedRowStyle;

        [MenuItem("Tools/PSD 工具/打开工具", false, 0)]
        static void Open() => GetWindow<PsdToolWindow>("PSD 转 UGUI");
        internal static void FocusNode(PsdTree tree, PsdToolSettings settings, int id)
        {
            var window = GetWindow<PsdToolWindow>("PSD 转 UGUI");
            window._tree = tree; window._settings = settings; window._psdPath = tree.psdPath;
            window._moduleName = tree.moduleName; window._panelName = tree.panelName; window._selected = id; window._doc = null;
            var seen = new HashSet<int>();
            for (var node = tree.Find(id); node != null && seen.Add(node.layerId); node = tree.Find(node.parentId)) window._collapsed.Remove(node.layerId);
            window._treeScroll = Vector2.zero; window.Show(); window.Repaint();
        }

        void OnEnable()
        {
            _settings = PsdToolSettings.Load();
        }

        void OnGUI()
        {
            minSize = new Vector2(620, 520);
            EnsureStyles();
            using (new EditorGUI.DisabledScope(CodexTaskRunner.Busy)) DrawHeader();
            EditorGUILayout.Space(4);
            if (CodexTaskRunner.Busy)
            {
                EditorGUILayout.HelpBox(CodexTaskRunner.Status, MessageType.Info);
                if (GUILayout.Button("查看识别进度")) CodexTaskWindow.Open(_tree, _settings);
                if (GUILayout.Button("取消识别")) CodexTaskRunner.Cancel();
                Repaint(); return;
            }

            if (_tree == null || _tree.nodes.Count == 0)
            {
                EditorGUILayout.HelpBox("选择 PSD 文件后点击“解析”。PSD 源文件建议放在 Assets 之外。", MessageType.Info);
                return;
            }

            RebuildChildren();
            EditorGUILayout.BeginHorizontal();
            DrawTree();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
            DrawReport();
        }

        void EnsureStyles()
        {
            if (_rowStyle != null) return;
            _rowStyle = new GUIStyle(EditorStyles.label) { richText = true };
            _selectedRowStyle = new GUIStyle(_rowStyle);
            _selectedRowStyle.normal.background = MakeTex(new Color(0.24f, 0.48f, 0.9f, 0.5f));
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void DrawHeader()
        {
            PsdEditorUI.Header("PSD 转 UGUI", "1 选择并解析 PSD   →   2 Codex 识别与审核   →   3 生成并验收");
            EditorGUILayout.BeginVertical(PsdEditorUI.Card);
            _sourceDetails = EditorGUILayout.Foldout(_sourceDetails, "项目配置 / 接续已有节点树", true);
            if (_sourceDetails)
            {
            _settings = (PsdToolSettings)EditorGUILayout.ObjectField("工具设置", _settings, typeof(PsdToolSettings), false);
            if (_settings != null && GUILayout.Button("编辑项目配置")) Selection.activeObject = _settings;

            var tree = (PsdTree)EditorGUILayout.ObjectField("节点树", _tree, typeof(PsdTree), false);
            if (tree != _tree)
            {
                _tree = tree;
                _doc = null;
                _selected = -1;
                if (tree != null)
                {
                    _psdPath = tree.psdPath;
                    _moduleName = tree.moduleName;
                    _panelName = tree.panelName;
                }
            }

            }
            EditorGUILayout.BeginHorizontal();
            _psdPath = EditorGUILayout.TextField("PSD 文件", _psdPath);
            if (GUILayout.Button("浏览", GUILayout.Width(50)))
            {
                string dir = string.IsNullOrEmpty(_psdPath) ? "" : Path.GetDirectoryName(_psdPath);
                string picked = EditorUtility.OpenFilePanelWithFilters("选择 PSD", dir, new[] { "Photoshop", "psd,psb" });
                if (!string.IsNullOrEmpty(picked))
                {
                    _psdPath = picked;
                    if (string.IsNullOrEmpty(_panelName)) _panelName = PathUtil.SanitizeFileName(Path.GetFileNameWithoutExtension(picked));
                }
            }
            EditorGUILayout.EndHorizontal();

            _moduleName = EditorGUILayout.TextField("模块", _moduleName);
            _panelName = EditorGUILayout.TextField("界面名", _panelName);

            if (_settings != null && !string.IsNullOrEmpty(_moduleName) && !string.IsNullOrEmpty(_panelName))
            {
                EditorGUILayout.LabelField("Prefab", _settings.PrefabPath(_moduleName.Trim(), _panelName.Trim()), EditorStyles.miniLabel);

            }

            bool ready = _settings != null && File.Exists(_psdPath) && !string.IsNullOrWhiteSpace(_moduleName) && !string.IsNullOrWhiteSpace(_panelName);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("1  解析 PSD", GUILayout.Height(28))) Run(Parse);
                if (GUILayout.Button("2  Codex 识别", GUILayout.Height(28))) Run(MarkWithCodex);
                if (GUILayout.Button("3  生成 Prefab", GUILayout.Height(28))) Run(Generate);
            }
            EditorGUILayout.EndHorizontal();
            if (AiMarker.Last?.tree == _tree && AiMarker.Last != null && PsdEditorUI.Action("查看识别结果 / 处理待办")) AiRecognitionWindow.Open(AiMarker.Last);
            EditorGUILayout.EndVertical();
            _maintenance = EditorGUILayout.Foldout(_maintenance, "维护与审核", true);
            if (_maintenance)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!ready))
                        if (PsdEditorUI.Action("预览变更")) Run(PreviewChanges);
                    using (new EditorGUI.DisabledScope(_tree == null || string.IsNullOrEmpty(_tree.prefabGuid)))
                        if (PsdEditorUI.Action("场景对比")) CompareUtility.ShowInScene(_tree, _settings);
                    using (new EditorGUI.DisabledScope(_settings == null))
                        if (PsdEditorUI.Action("资源管理")) ResourceManagerWindow.Open(_settings);
                }
                if (_tree != null && PsdEditorUI.Action("查看最近识别进度")) CodexTaskWindow.Open(_tree, _settings);
                using (new EditorGUI.DisabledScope(_tree == null || string.IsNullOrEmpty(_tree.prefabGuid)))
                    if (PsdEditorUI.Action("导出干净 Prefab 副本…"))
                        CleanPrefabExportWindow.Open(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(_tree.prefabGuid)));
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(_settings == null))
                    if (GUILayout.Button("重建资源索引")) Run(() => SpriteIndex.Rebuild(_settings));
                using (new EditorGUI.DisabledScope(_tree == null || string.IsNullOrEmpty(_tree.prefabGuid)))
                {
                    if (GUILayout.Button("审核待删除节点 / 组件")) Run(ConfirmRemovals);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("改稿后的删除审核会保护已有人工引用；仅在确认清理时使用。", EditorStyles.wordWrappedMiniLabel);
            }
        }

        void Run(Action action)
        {
            try
            {
                action();
            }
            catch (OperationCanceledException)
            {
            }
            catch (PsdException e)
            {
                EditorUtility.DisplayDialog("PSD 解析失败", e.Message, "确定");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("出错了", e.Message, "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            GUIUtility.ExitGUI();
        }

        void Parse()
        {
            EditorUtility.DisplayProgressBar("PSD 解析", "读取图层结构", 0.3f);
            _doc = PsdParser.Parse(_psdPath);
            _docStamp = TreeBuilder.Stamp(_psdPath);

            _tree = GetOrCreateTree();
            if (!string.IsNullOrEmpty(_tree.psdPath) &&
                !string.Equals(Path.GetFullPath(_tree.psdPath), Path.GetFullPath(_psdPath), StringComparison.OrdinalIgnoreCase) &&
                !EditorUtility.DisplayDialog("PSD 来源路径变更", "这份节点树原来关联：\n" + _tree.psdPath +
                    "\n\n确认新路径是同一界面的移动或替换稿后继续。不同界面请使用新的界面名，以免复用旧图层映射。", "确认为同一来源", "取消"))
                throw new OperationCanceledException();
            _tree.psdPath = _psdPath;
            _tree.moduleName = _moduleName.Trim();
            _tree.panelName = _panelName.Trim();
            TreeBuilder.Build(_doc, _tree, _settings);
            if (_tree.suggestions == null) _tree.suggestions = new List<ResourceSuggestion>();
            else _tree.suggestions.RemoveAll(s => s.kind == SuggestionKind.ApplyRule);
            RuleRecognizer.Recognize(_tree, _settings);

            EditorUtility.SetDirty(_tree);
            AssetDatabase.SaveAssets();
            _selected = -1;
        }

        void Generate()
        {
            if (_doc == null || _tree == null || _docStamp != TreeBuilder.Stamp(_psdPath) || _tree.psdPath != _psdPath)
                Parse();

            // Diagnose widget structure before exporting any resources.
            var preflight = GenerationPreflight.Inspect(_tree, _settings);
            foreach (var issue in preflight)
                Debug.LogWarning("生成预检：" + issue.nodePath + "：" + issue.message + "；" + issue.resolution);
            SpriteExporter.Export(_doc, _tree, _settings);
            EditorUtility.DisplayProgressBar("生成图集", _tree.panelName, 0.9f);
            AtlasBuilder.Update(_settings, _tree.moduleName, _tree.panelName);
            EditorUtility.DisplayProgressBar("生成 Prefab", _tree.panelName, 1f);
            var prefab = PrefabGenerator.Generate(_tree, _settings);
            _showReport |= _tree.warnings.Exists(w => w.StartsWith("[交互待处理]", StringComparison.Ordinal));

            EditorUtility.SetDirty(_tree);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
        }

        void PreviewChanges()
        {
            if (_doc == null || _tree == null || _docStamp != TreeBuilder.Stamp(_psdPath) || _tree.psdPath != _psdPath)
                Parse();
            // 预览复用已有切图，不导出或改写图片、图集与 Prefab。
            var issues = new List<GenerationIssue>();
            using (var draft = GenerationPreflight.Build(_tree, _settings, issues, true))
            {
                var report = PrefabIncrementalImport.Preview(draft.Root, _tree, _settings);
                foreach (var issue in issues) report.lines.Insert(0, "[交互待处理] " + issue.nodePath + "：" + issue.message + "；" + issue.resolution);
                PsdChangesWindow.Show(report);
            }
        }

        void ConfirmRemovals()
        {
            if (_tree == null) return;
            using (var draft = PrefabGenerator.BuildDraft(_tree, _settings))
                PrefabIncrementalImport.ConfirmRemovals(draft.Root, _tree, _settings);
        }

        void MarkWithCodex()
        {
            if (_doc == null || _tree == null || _docStamp != TreeBuilder.Stamp(_psdPath) || _tree.psdPath != _psdPath) Parse();
            CodexTaskWindow.ExportAndOpen(_tree, _settings);
        }

        PsdTree GetOrCreateTree()
        {
            string path = $"{_settings.TreesFolder}/{PathUtil.SanitizeFileName(_moduleName)}_{PathUtil.SanitizeFileName(_panelName)}.asset";
            if (_tree != null && AssetDatabase.GetAssetPath(_tree) == path) return _tree;

            var existing = AssetDatabase.LoadAssetAtPath<PsdTree>(path);
            if (existing != null) return existing;

            PathUtil.EnsureFolder(_settings.TreesFolder);
            var tree = CreateInstance<PsdTree>();
            AssetDatabase.CreateAsset(tree, path);
            return tree;
        }

        void RebuildChildren()
        {
            _children = new Dictionary<int, List<PsdNode>>();
            foreach (var n in _tree.nodes)
            {
                if (!_children.TryGetValue(n.parentId, out var list)) _children[n.parentId] = list = new List<PsdNode>();
                list.Add(n);
            }
            foreach (var list in _children.Values) list.Sort((a, b) => b.order.CompareTo(a.order));
        }

        void DrawTree()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.55f));
            EditorGUILayout.LabelField($"节点树（{_tree.nodes.Count} 个节点，顺序与 PS 图层面板一致）", EditorStyles.boldLabel);
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, "box", GUILayout.ExpandHeight(true));
            DrawNodes(-1, 0);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawNodes(int parentId, int depth)
        {
            if (!_children.TryGetValue(parentId, out var list)) return;
            foreach (var n in list)
            {
                bool hasChildren = _children.ContainsKey(n.layerId);
                bool expanded = !_collapsed.Contains(n.layerId);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 14);
                if (hasChildren)
                {
                    bool next = GUILayout.Toggle(expanded, GUIContent.none, EditorStyles.foldout, GUILayout.Width(14));
                    if (next != expanded)
                    {
                        if (next) _collapsed.Remove(n.layerId);
                        else _collapsed.Add(n.layerId);
                    }
                }
                else
                {
                    GUILayout.Space(16);
                }

                if (GUILayout.Button(RowLabel(n), n.layerId == _selected ? _selectedRowStyle : _rowStyle))
                {
                    _selected = n.layerId;
                    GUI.FocusControl(null);
                }
                EditorGUILayout.EndHorizontal();

                if (hasChildren && expanded) DrawNodes(n.layerId, depth + 1);
            }
        }

        static string RowLabel(PsdNode n)
        {
            string type = KindName(n.kind);
            if (!string.IsNullOrEmpty(n.templateId) && n.templateId != "Group") type += " · " + n.templateId;
            if (!string.IsNullOrEmpty(n.slot)) type += " · @" + n.slot;

            string label = $"{n.goName}  <color=#8a8a8a>[{type}]</color>";
            if (!n.visible) label += "  <color=#8a8a8a>(隐藏)</color>";
            if (n.image.reusedFromLayerId >= 0 || !string.IsNullOrEmpty(n.image.reusedFromPath)) label += "  <color=#5fa35f>(复用)</color>";
            if (n.image.sliced) label += "  <color=#5fa35f>(九宫)</color>";
            if (n.image.tiled) label += "  <color=#5fa35f>(平铺)</color>";
            if (n.status == NodeStatus.New) label += "  <color=#d9a400>(新)</color>";
            if (n.status == NodeStatus.PendingDelete) label += "  <color=#d05050>(待删除)</color>";
            return label;
        }

        static string KindName(LayerKind kind)
        {
            switch (kind)
            {
                case LayerKind.Group: return "组";
                case LayerKind.Text: return "文字";
                case LayerKind.Fill: return "填充";
                case LayerKind.SmartObject: return "智能对象";
                default: return "图片";
            }
        }

        static readonly string[] SlotChoices =
        {
            "", "bg", "label", "icon", "badge", "hit", "pressed", "disabled", "on", "labelOn",
            "track", "fill", "handle", "viewport", "content", "item", "scrollbar", "vbarbg", "vbar", "hbarbg", "hbar",
            "caption", "arrow", "template",
            "checkmark", "placeholder", "text", "rect",
        };

        static readonly string[] SlotLabels =
        {
            "（无）", "底图 bg", "文字 label", "图标 icon", "红点 badge", "热区 hit", "按下 pressed", "禁用 disabled",
            "选中 on", "选中文字 labelOn", "轨道 track", "填充 fill", "滑块 handle", "视口 viewport", "内容 content", "列表项 item", "滚动条 scrollbar",
            "竖条底 vbarbg", "竖条 vbar", "横条底 hbarbg", "横条 hbar",
            "当前值 caption", "箭头 arrow", "下拉列表 template", "勾选 checkmark", "占位 placeholder", "输入文字 text", "边界 rect",
        };

        void DrawDetail()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("节点标签（点选，勿手写图层名）", EditorStyles.boldLabel);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, "box", GUILayout.ExpandHeight(true));

            var n = _selected != -1 ? _tree.Find(_selected) : null;
            if (n == null)
            {
                EditorGUILayout.LabelField("在左侧选择一个节点");
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                n.goName = EditorGUILayout.TextField("名称", n.goName);

                int tIndex = Mathf.Max(0, Array.IndexOf(Markers.TemplateIds, n.templateId ?? string.Empty));
                tIndex = EditorGUILayout.Popup("控件类型", tIndex, Markers.TemplateLabels);
                n.templateId = Markers.TemplateIds[tIndex];
                if (n.templateId == "Progress") PsdProgressUI.Draw(n);
                if (n.templateId == "ScrollView" && GUILayout.Button("排列与自适应…"))
                    ScrollLayoutWindow.Open(n, options =>
                    {
                        if ((n.locks & (PropGroup.Component | PropGroup.Rect)) != 0) throw new InvalidOperationException("请先解除组件或矩形锁定。");
                        if (n.slot == "template" || _tree.Find(n.parentId)?.templateId == "Dropdown") throw new InvalidOperationException("Dropdown 模板由 TMP 管理排列。");
                        Undo.RegisterCompleteObjectUndo(_tree, "配置列表排列"); ScrollLayoutOptions.Copy(options, n);
                        EditorUtility.SetDirty(_tree); AssetDatabase.SaveAssetIfDirty(_tree);
                        if (AiMarker.Last?.tree == _tree) { AiMarker.Last.generated = false; AiMarker.Last.generationChecked = false; }
                    });

                int sIndex = Mathf.Max(0, Array.IndexOf(SlotChoices, n.slot ?? string.Empty));
                sIndex = EditorGUILayout.Popup("插槽", sIndex, SlotLabels);
                n.slot = SlotChoices[sIndex];
                if (n.aiGeneratedGroup)
                {
                    EditorGUILayout.HelpBox("AI 虚拟分组：只用于节点树和 Unity 输出，不改写 PSD。", MessageType.Info);
                    if (n.aiBoundsOverride && n.slot == "viewport")
                    {
                        var bounds = EditorGUILayout.RectIntField("AI 视口边界（PSD 坐标）", n.psdRect);
                        bounds.x = Mathf.Clamp(bounds.x, 0, Mathf.Max(0, _tree.docSize.x - 1));
                        bounds.y = Mathf.Clamp(bounds.y, 0, Mathf.Max(0, _tree.docSize.y - 1));
                        bounds.width = Mathf.Clamp(bounds.width, 1, Mathf.Max(1, _tree.docSize.x - bounds.x));
                        bounds.height = Mathf.Clamp(bounds.height, 1, Mathf.Max(1, _tree.docSize.y - bounds.y));
                        n.psdRect = bounds;
                    }
                }

                int aIndex = Mathf.Max(0, Array.IndexOf(AnchorUtil.Presets, n.anchorPreset ?? string.Empty));
                aIndex = EditorGUILayout.Popup("锚点", aIndex, AnchorUtil.PresetLabels);
                n.anchorPreset = AnchorUtil.Presets[aIndex];

                n.generateBind = EditorGUILayout.Toggle("生成绑定字段", n.generateBind);
                n.underSafeArea = EditorGUILayout.Toggle("放入 SafeArea", n.underSafeArea);
                var newLocks = (PropGroup)EditorGUILayout.EnumFlagsField("保留人工修改（锁定组）", n.locks);
                n.forceUpdateGroups |= n.locks & ~newLocks;
                n.locks = newLocks;
                if (n.forceUpdateGroups != PropGroup.None)
                    EditorGUILayout.HelpBox("下次应用将用 PSD 覆盖已解锁属性：" + n.forceUpdateGroups, MessageType.Info);
                n.nineOverride = (NineSliceOverride)EditorGUILayout.EnumPopup("九宫覆盖", n.nineOverride);
                if (n.kind != LayerKind.Group && n.kind != LayerKind.Text)
                {
                    var selected = EditorGUILayout.ObjectField("指定图片（只读引用）", n.resourceOverride, typeof(UnityEngine.Object), false);
                    if (selected == null || selected is Sprite || selected is Texture2D)
                    {
                        if (selected != n.resourceOverride) { n.locks &= ~PropGroup.Sprite; n.forceUpdateGroups |= PropGroup.Sprite; }
                        n.resourceOverride = selected;
                    }
                    else EditorGUILayout.HelpBox("这里只接受项目中的 Sprite 或 Texture2D。", MessageType.Warning);
                    if (n.resourceOverride != null) EditorGUILayout.HelpBox("该图片仅引用，不切图、不覆盖其内容或导入参数。", MessageType.Info);
                }
                if (n.nineOverride == NineSliceOverride.Force)
                    n.nineBorder = EditorGUILayout.Vector4Field("九宫边框(左下右上)", n.nineBorder);

                if (n.templateId == "Button")
                {
                    float scale = n.pressScale < 0f ? _settings.addButtonPressScale ? _settings.buttonPressScale : 1f : n.pressScale;
                    bool enabled = scale < .999f;
                    bool nextEnabled = EditorGUILayout.Toggle("启用按压缩放", enabled);
                    if (nextEnabled != enabled) { n.pressScale = scale = nextEnabled ? _settings.buttonPressScale : 1f; }
                    EditorGUI.BeginChangeCheck();
                    using (new EditorGUI.DisabledScope(!nextEnabled)) scale = EditorGUILayout.Slider("按下缩放", scale, 0.5f, 1f);
                    if (EditorGUI.EndChangeCheck()) n.pressScale = scale;
                    if (GUILayout.Button("恢复默认缩放", GUILayout.Width(120))) n.pressScale = -1f;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    n.source = DecisionSource.Manual;
                    n.confidence = 1f;
                    EditorUtility.SetDirty(_tree);
                }

                DrawReassociate(n);

                EditorGUILayout.Space(4);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("图层 ID", n.layerId.ToString());
                    EditorGUILayout.TextField("图层类型", KindName(n.kind));
                    EditorGUILayout.TextField("判定来源", $"{n.source} ({n.confidence:0.00})");
                    if (n.markers.Count > 0)
                        EditorGUILayout.TextField("图层名标记(兼容)", string.Join("  @", n.markers.ToArray()));
                    EditorGUILayout.RectIntField("PSD 矩形", n.psdRect);
                    EditorGUILayout.FloatField("不透明度", n.opacity);

                    if (n.kind == LayerKind.Text)
                    {
                        EditorGUILayout.TextField("字体", n.text.font);
                        EditorGUILayout.FloatField("字号", n.text.fontSize);
                        EditorGUILayout.ColorField("颜色", n.text.color);
                        EditorGUILayout.Toggle("描边", n.text.outline != null && n.text.outline.enabled);
                        EditorGUILayout.Toggle("投影", n.text.shadow != null && n.text.shadow.enabled);
                        EditorGUILayout.TextField("文本类型", n.text.isBox ? "段落文字" : "点文字");
                        EditorGUILayout.LabelField("内容");
                        EditorGUILayout.TextArea(n.text.content, GUILayout.MinHeight(40));
                    }
                    else if (n.kind != LayerKind.Group)
                    {
                        EditorGUILayout.TextField("资源", n.image.isSolid ? "纯色（不切图）" : n.image.assetPath);
                        if (n.image.isSolid) EditorGUILayout.ColorField("颜色", n.image.solidColor);
                        if (n.image.reusedFromLayerId >= 0) EditorGUILayout.TextField("复用自图层", n.image.reusedFromLayerId.ToString());
                        if (!string.IsNullOrEmpty(n.image.reusedFromPath)) EditorGUILayout.TextField("复用资源", n.image.reusedFromPath);
                        if (n.image.sliced || n.image.tiled) EditorGUILayout.Vector4Field(n.image.tiled ? "平铺边框" : "九宫边框", n.image.border);
                    }
                }

                if (n.kind != LayerKind.Group && !n.image.isSolid && !string.IsNullOrEmpty(n.image.assetPath))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(n.image.assetPath);
                    if (tex != null)
                    {
                        float size = Mathf.Min(160f, position.width * 0.4f);
                        var r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                        EditorGUI.DrawTextureTransparent(r, tex, ScaleMode.ScaleToFit);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawReassociate(PsdNode n)
        {
            if (n.status == NodeStatus.PendingDelete || n.aiGeneratedGroup) return;
            var missing = _tree.nodes.FindAll(x => x.status == NodeStatus.PendingDelete && !x.aiGeneratedGroup);
            if (missing.Count == 0) return;
            var labels = new List<string> { "选择旧图层（LayerID 变化时）" };
            labels.AddRange(missing.ConvertAll(x => x.goName + " [" + x.layerId + "]"));
            int pick = EditorGUILayout.Popup("重新关联", 0, labels.ToArray());
            if (pick <= 0) return;
            var old = missing[pick - 1];
            Undo.RecordObject(_tree, "重新关联 PSD 图层");
            n.stableKey = string.IsNullOrEmpty(old.stableKey) ? "layer:" + old.layerId : old.stableKey;
            n.locks = old.locks;
            _tree.nodes.Remove(old);
            EditorUtility.SetDirty(_tree);
            AssetDatabase.SaveAssets();
        }

        void DrawReport()
        {
            int pending = _tree.nodes.FindAll(x => x.status == NodeStatus.PendingDelete).Count;
            int suggestions = _tree.suggestions == null ? 0 : _tree.suggestions.FindAll(s => !s.done).Count;
            _showReport = EditorGUILayout.Foldout(_showReport, $"生成校验 / 资源建议（{_tree.warnings.Count + suggestions}）", true);
            if (!_showReport) return;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"校验报告（{_tree.warnings.Count} 条）", EditorStyles.boldLabel);
            if (pending > 0 && GUILayout.Button($"确认删除 {pending} 个待删除节点", GUILayout.Width(200)))
            {
                _tree.nodes.RemoveAll(x => x.status == NodeStatus.PendingDelete);
                EditorUtility.SetDirty(_tree);
            }
            EditorGUILayout.EndHorizontal();

            int lines = Math.Max(1, _tree.warnings.Count + (_tree.suggestions == null ? 0 : _tree.suggestions.Count));
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.Height(Mathf.Min(200, 24 + lines * 24)));
            foreach (var w in _tree.warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            if (_tree.suggestions != null)
            {
                foreach (var s in _tree.suggestions)
                {
                    if (s.done) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(s.message);
                    string button = s.kind == SuggestionKind.MoveToCommon ? "移到 Common"
                        : s.kind == SuggestionKind.ApplyRule ? "应用识别"
                        : s.kind == SuggestionKind.ApplyAi ? "应用 AI"
                        : "改用此图";
                    if (GUILayout.Button(button, GUILayout.Width(110)))
                        Run(() => ResourceActions.Apply(s, _tree, _settings));
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
