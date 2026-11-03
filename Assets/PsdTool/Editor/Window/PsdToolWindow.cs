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
        bool _revealSelected;
        readonly HashSet<int> _collapsed = new HashSet<int>();
        Dictionary<int, List<PsdNode>> _children;
        Vector2 _treeScroll, _detailScroll, _reportScroll;
        [SerializeField] bool _maintenance;
        bool _sourceDetails, _showReport;
        string _detailError;
        [SerializeField] bool _followPrefabSelection = true;
        readonly PsdNodePreview _nodePreview = new PsdNodePreview();
        void OnDisable() { Selection.selectionChanged -= FollowPrefabSelection; _nodePreview.Dispose(); }
        GUIStyle _rowStyle, _selectedRowStyle;

        [MenuItem("Tools/PSD 工具/打开工具", false, 0)]
        static void Open() => GetWindow<PsdToolWindow>("PSD 转 UGUI");
        internal static void FocusNode(PsdTree tree, PsdToolSettings settings, int id)
        {
            var window = GetWindow<PsdToolWindow>("PSD 转 UGUI");
            window._tree = tree; window._settings = settings; window._psdPath = tree.psdPath;
            window._moduleName = tree.moduleName; window._panelName = tree.panelName; window._selected = id; window._doc = null;
            window._revealSelected=true;
            var seen = new HashSet<int>();
            for (var node = tree.Find(id); node != null && seen.Add(node.layerId); node = tree.Find(node.parentId)) window._collapsed.Remove(node.layerId);
            window._treeScroll = Vector2.zero; window.Show(); window.Repaint();
        }

        void OnEnable()
        {
            _settings = PsdToolSettings.Load();
            Selection.selectionChanged += FollowPrefabSelection;
            FollowPrefabSelection();
        }
        void FollowPrefabSelection()
        {
            if (!_followPrefabSelection || CodexTaskRunner.Busy || !PsdNodeSelection.Resolve(Selection.activeGameObject, out var tree, out var node)) return;
            if (_tree != tree)
            {
                _tree = tree; _psdPath = tree.psdPath; _moduleName = tree.moduleName; _panelName = tree.panelName;
                _doc = null; _docStamp = null; _nodePreview.Dispose();
            }
            _selected = node.layerId; _detailError = null;
            _revealSelected=true;
            var seen = new HashSet<int>();
            for (var parent = node; parent != null && seen.Add(parent.layerId); parent = tree.Find(parent.parentId)) _collapsed.Remove(parent.layerId);
            Repaint();
        }

        void OnGUI()
        {
            minSize = new Vector2(760, 640);
            EnsureStyles();
            using (new EditorGUI.DisabledScope(CodexTaskRunner.Busy)) DrawHeader();
            EditorGUILayout.Space(4);
            if (CodexTaskRunner.Busy)
            {
                PsdEditorUI.Notice(CodexTaskRunner.Status, MessageType.Info);
                if (PsdEditorUI.CopyButton("查看识别进度")) CodexTaskWindow.Open(_tree, _settings);
                if (PsdEditorUI.CopyButton("取消识别")) CodexTaskRunner.Cancel();
                Repaint(); return;
            }

            if (_tree == null || _tree.nodes.Count == 0)
            {
                PsdEditorUI.Notice("选择 PSD 文件后点击“解析”。PSD 源文件建议放在 Assets 之外。", MessageType.Info);
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
            _sourceDetails = PsdEditorUI.CopyFoldout(_sourceDetails, "项目配置 / 接续已有节点树", true);
            if (_sourceDetails)
            {
            _settings = (PsdToolSettings)EditorGUILayout.ObjectField("工具设置", _settings, typeof(PsdToolSettings), false);
            if (_settings != null && PsdEditorUI.CopyButton("编辑项目配置")) Selection.activeObject = _settings;

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
            if (PsdEditorUI.CopyButton("浏览", GUILayout.Width(50)))
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
                PsdEditorUI.Text("Prefab", _settings.PrefabPath(_moduleName.Trim(), _panelName.Trim()), EditorStyles.miniLabel);

            }

            bool ready = _settings != null && File.Exists(_psdPath) && !string.IsNullOrWhiteSpace(_moduleName) && !string.IsNullOrWhiteSpace(_panelName);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (PsdEditorUI.CopyButton("1  解析 PSD", GUILayout.Height(28))) Run(Parse);
                if (PsdEditorUI.CopyButton("2  Codex 识别", GUILayout.Height(28))) Run(MarkWithCodex);
                bool exists = _tree != null && AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(_tree.prefabGuid)) != null;
                if (PsdEditorUI.CopyButton(exists ? "3  更新 Prefab" : "3  生成 Prefab", GUILayout.Height(28))) Run(Generate);
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUILayout.HorizontalScope())
            {
                bool follow = PsdEditorUI.CopyToggleLeft(new GUIContent("跟随 Prefab / Scene 选中节点", "在 Unity 层级或场景中选中工具生成的节点，自动显示对应详情；辅助节点定位到所属来源节点。"), _followPrefabSelection);
                if (follow != _followPrefabSelection) { _followPrefabSelection = follow; if (follow) FollowPrefabSelection(); }
                var prefab = _tree == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(_tree.prefabGuid));
                using (new EditorGUI.DisabledScope(prefab == null))
                    if (PsdEditorUI.CopyButton("打开 Prefab", GUILayout.Width(100))) AssetDatabase.OpenAsset(prefab);
            }
            if (AiMarker.Last?.tree == _tree && AiMarker.Last != null && PsdEditorUI.Action("查看识别结果 / 处理待办")) AiRecognitionWindow.Open(AiMarker.Last);
            EditorGUILayout.EndVertical();
            _maintenance = PsdEditorUI.CopyFoldout(_maintenance, "维护与审核", true);
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
                    if (PsdEditorUI.CopyButton("重建资源索引")) Run(() => SpriteIndex.Rebuild(_settings));
                using (new EditorGUI.DisabledScope(_tree == null || string.IsNullOrEmpty(_tree.prefabGuid)))
                {
                    if (PsdEditorUI.CopyButton("审核待删除节点 / 组件")) Run(ConfirmRemovals);
                }
                EditorGUILayout.EndHorizontal();
                PsdEditorUI.Text("改稿后的删除审核会保护已有人工引用；仅在确认清理时使用。", EditorStyles.wordWrappedMiniLabel);
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
            var requestedTree=_tree;var requestedSettings=_settings;
            NodeNamingWindow.Open(_tree, () => {
                if(_tree!=requestedTree||_settings!=requestedSettings)throw new InvalidOperationException("主窗口已切换来源或配置，请重新点击生成。");
                GenerateConfirmed();
            },-1,_settings);
        }

        void GenerateConfirmed()
        {
            if(_tree==null)throw new InvalidOperationException("节点树已丢失，请重新打开工作台。");
            string currentStamp=TreeBuilder.Stamp(_tree.psdPath);
            if(_doc==null||_doc.FilePath!=_tree.psdPath||_docStamp!=currentStamp)
            {_doc=PsdParser.Parse(_tree.psdPath);_docStamp=currentStamp;_psdPath=_tree.psdPath;}
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
            PsdEditorUI.Text($"节点树（{_tree.nodes.Count} 个节点，顺序与 PS 图层面板一致）", EditorStyles.boldLabel);
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

                if (PsdEditorUI.CopyButton(RowLabel(n), n.layerId == _selected ? _selectedRowStyle : _rowStyle))
                {
                    _selected = n.layerId;
                    GUI.FocusControl(null);
                }
                if(n.layerId==_selected&&_revealSelected&&Event.current.type==EventType.Repaint)
                {_treeScroll.y=Mathf.Max(0,GUILayoutUtility.GetLastRect().y-120);_revealSelected=false;Repaint();}
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
            PsdEditorUI.Text("节点详情", EditorStyles.boldLabel);
            PsdEditorUI.Text("在左侧节点树或已打开的 Prefab / Scene 选中节点。修改保存到工具数据，更新 Prefab 后生效。", EditorStyles.wordWrappedMiniLabel);
            _detailScroll=EditorGUILayout.BeginScrollView(_detailScroll,"box",GUILayout.ExpandHeight(true));
            var n=_selected!=-1?_tree.Find(_selected):null;
            if(n==null)PsdEditorUI.Text("在左侧选择节点，可查看组或图层的 PSD 区域。");
            else
            {
                using(PsdEditorUI.Section("命名与类型 · 生成前"))
                {
                using(new EditorGUILayout.HorizontalScope())
                {
                    string name=EditorGUILayout.DelayedTextField(new GUIContent("Prefab 节点名","仅修改 Unity 输出名，不改 PSD。回车保存；校验英文、m_ 前缀、部件角色和同层重名。"),n.goName);
                    if(name!=n.goName)try{NodeNaming.Rename(_tree,n,name);_detailError=null;}catch(Exception e){_detailError=e.Message;}
                    using(new EditorGUI.DisabledScope((n.locks&PropGroup.Name)!=0))
                        if(PsdEditorUI.CopyButton(new GUIContent("m_节点","保留当前英文名，一键加 m_ 并标为程序访问节点；下次生成生效。中文名使用英文建议，重名自动补编号。"),GUILayout.Width(65)))
                            try{NodeNaming.MarkProgramNode(_tree,n);_detailError=null;}catch(Exception e){_detailError=e.Message;}
                    if(PsdEditorUI.CopyButton("批量…",GUILayout.Width(55)))NodeNamingWindow.Open(_tree,null,-1,_settings);
                }
                if(_detailError!=null)PsdEditorUI.Notice(_detailError,MessageType.Warning);
                Undo.RecordObject(_tree,"编辑 PSD 节点");
                EditorGUI.BeginChangeCheck();
                string oldType=n.templateId;
                int ti=Mathf.Max(0,Array.IndexOf(Markers.TemplateIds,n.templateId??""));
                n.templateId=Markers.TemplateIds[EditorGUILayout.Popup("控件类型",ti,Markers.TemplateLabels)];
                if(oldType!=n.templateId&&!n.manualName)n.nameReviewed=false;
                using(new EditorGUI.DisabledScope(PrefabGenerator.IsControl(n.templateId)))
                {
                    bool program=EditorGUILayout.Toggle(new GUIContent("程序访问节点（m_）","控件默认启用。图片、文字或组可显式标记，命名预览会添加 m_ 前缀。当前只管理命名，不生成 Binder/C# 代码。"),NodeNaming.ProgramNode(n));
                    if(!PrefabGenerator.IsControl(n.templateId))n.generateBind=program;
                }
                }
                using(PsdEditorUI.Section(null))_nodePreview.Draw(_tree,n,140,_settings);
                using(PsdEditorUI.Section("控件配置 · 识别/生成时"))
                {
                    int si=Mathf.Max(0,Array.IndexOf(SlotChoices,n.slot??""));
                    n.slot=SlotChoices[EditorGUILayout.Popup(new GUIContent("部件插槽","指定该图层在所属控件中的作用，例如背景、文字、勾选图；影响组件引用与命名。"),si,SlotLabels)];
                    if(n.templateId=="Button")
                    {
                        PsdEditorUI.Text("按钮反馈",n.buttonColorTint?"ColorTint（生成前可改）":"None（生成前可改）");
                        if(PsdEditorUI.CopyButton("选择按钮热区来源…"))ButtonHitSourcesWindow.Open(_tree,n);
                        PsdEditorUI.Text("热区来源",n.buttonHitKeys==null||n.buttonHitKeys.Count==0?"全部正常视觉":n.buttonHitKeys.Count+" 个选定节点");
                    }
                    if(n.templateId=="Progress")PsdProgressUI.Draw(n);
                    if(n.templateId=="ScrollView"&&PsdEditorUI.CopyButton("排列与自适应…"))ScrollLayoutWindow.Open(n,options=>
                    {
                        if((n.locks&(PropGroup.Component|PropGroup.Rect))!=0)throw new InvalidOperationException("请先解除组件或矩形锁定。");
                        if(n.slot=="template"||_tree.Find(n.parentId)?.templateId=="Dropdown")throw new InvalidOperationException("Dropdown 模板由 TMP 管理排列。");
                        Undo.RegisterCompleteObjectUndo(_tree,"配置列表排列");ScrollLayoutOptions.Copy(options,n);EditorUtility.SetDirty(_tree);AssetDatabase.SaveAssetIfDirty(_tree);
                        if(AiMarker.Last?.tree==_tree){AiMarker.Last.generated=false;AiMarker.Last.generationChecked=false;}
                    });
                    if(n.aiGeneratedGroup)
                    {
                        PsdEditorUI.Text("AI 虚拟分组，不改写 PSD。",EditorStyles.wordWrappedMiniLabel);
                        if(n.aiBoundsOverride&&n.slot=="viewport")
                        {
                            var b=EditorGUILayout.RectIntField("AI 视口（PSD 坐标）",n.psdRect);
                            b.x=Mathf.Clamp(b.x,0,Mathf.Max(0,_tree.docSize.x-1));b.y=Mathf.Clamp(b.y,0,Mathf.Max(0,_tree.docSize.y-1));
                            b.width=Mathf.Clamp(b.width,1,Mathf.Max(1,_tree.docSize.x-b.x));b.height=Mathf.Clamp(b.height,1,Mathf.Max(1,_tree.docSize.y-b.y));n.psdRect=b;
                        }
                    }
                }
                if(n.kind!=LayerKind.Group&&n.kind!=LayerKind.Text)
                {
                    using(PsdEditorUI.Section("资源与九宫 · 切图导出时"))
                    {
                        n.nineOverride=(NineSliceOverride)EditorGUILayout.EnumPopup(new GUIContent("九宫覆盖","Auto 自动识别边框；Force 使用指定边框；Forbid 不做九宫。仅设置 Sprite Border，不缩图。重新导出切图后生效；外部引用资源不修改。"),n.nineOverride);
                        if(n.nineOverride==NineSliceOverride.Force)n.nineBorder=EditorGUILayout.Vector4Field("边框（左下右上）",n.nineBorder);
                        var resource=EditorGUILayout.ObjectField(new GUIContent("指定图片（只读）","直接引用已有 Sprite/Texture2D，不覆盖图片及其导入配置。"),n.resourceOverride,typeof(UnityEngine.Object),false);
                        if(resource==null||resource is Sprite||resource is Texture2D)
                        {if(resource!=n.resourceOverride){n.locks&=~PropGroup.Sprite;n.forceUpdateGroups|=PropGroup.Sprite;}n.resourceOverride=resource;}
                        else PsdEditorUI.Notice("只接受 Sprite 或 Texture2D。",MessageType.Warning);
                    }
                }
                using(PsdEditorUI.Section("适配 · 生成/运行时"))
                {
                    int ai=Mathf.Max(0,Array.IndexOf(AnchorUtil.Presets,n.anchorPreset??""));n.anchorPreset=AnchorUtil.Presets[EditorGUILayout.Popup("锚点",ai,AnchorUtil.PresetLabels)];
                    using(new EditorGUI.DisabledScope(n.parentId!=-1))n.underSafeArea=EditorGUILayout.Toggle(new GUIContent("放入 SafeArea","仅顶层节点生效：生成时放入安全区容器，运行时按 Screen.safeArea 避让刘海/系统区域；内部子节点继承所属顶层容器。"),n.underSafeArea);
                }
                using(PsdEditorUI.Section("增量保护 · 改稿/迁移时"))
                {
                    var locks=(PropGroup)EditorGUILayout.EnumFlagsField("保留人工修改（锁定组）",n.locks);n.forceUpdateGroups|=n.locks&~locks;n.locks=locks;
                    if(n.forceUpdateGroups!=PropGroup.None)PsdEditorUI.Notice("下次更新覆盖："+n.forceUpdateGroups,MessageType.Info);
                    DrawReassociate(n);
                }
                if(EditorGUI.EndChangeCheck())
                {n.source=DecisionSource.Manual;n.confidence=1;EditorUtility.SetDirty(_tree);if(AiMarker.Last?.tree==_tree){AiMarker.Last.generated=false;AiMarker.Last.generationChecked=false;}}
                using(PsdEditorUI.Section("来源信息 · 只读"))
                using(new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("PSD 图层名",n.psdName);EditorGUILayout.TextField("图层 ID",n.layerId.ToString());
                    EditorGUILayout.TextField("图层类型",KindName(n.kind));EditorGUILayout.TextField("判定来源",n.source+" ("+n.confidence.ToString("0.00")+")");
                    EditorGUILayout.RectIntField("PSD 矩形",n.psdRect);EditorGUILayout.FloatField("不透明度",n.opacity);
                    if(n.kind==LayerKind.Text){EditorGUILayout.TextField("字体",n.text.font);EditorGUILayout.FloatField("字号",n.text.fontSize);EditorGUILayout.ColorField("颜色",n.text.color);EditorGUILayout.TextArea(n.text.content,GUILayout.MinHeight(40));}
                    else if(n.kind!=LayerKind.Group){EditorGUILayout.TextField("资源",n.image.isSolid?"纯色（不切图）":n.image.assetPath);if(n.image.isSolid)EditorGUILayout.ColorField("颜色",n.image.solidColor);EditorGUILayout.Vector4Field("Sprite Border",n.image.border);}
                }
            }
            EditorGUILayout.EndScrollView();EditorGUILayout.EndVertical();
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
            _showReport = PsdEditorUI.CopyFoldout(_showReport, $"生成校验 / 资源建议（{_tree.warnings.Count + suggestions}）", true);
            if (!_showReport) return;
            EditorGUILayout.BeginHorizontal();
            PsdEditorUI.Text($"校验报告（{_tree.warnings.Count} 条）", EditorStyles.boldLabel);
            if (pending > 0 && PsdEditorUI.CopyButton($"确认删除 {pending} 个待删除节点", GUILayout.Width(200)))
            {
                _tree.nodes.RemoveAll(x => x.status == NodeStatus.PendingDelete);
                EditorUtility.SetDirty(_tree);
            }
            EditorGUILayout.EndHorizontal();

            int lines = Math.Max(1, _tree.warnings.Count + (_tree.suggestions == null ? 0 : _tree.suggestions.Count));
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.Height(Mathf.Min(200, 24 + lines * 24)));
            foreach (var w in _tree.warnings) PsdEditorUI.Notice(w, MessageType.Warning);
            if (_tree.suggestions != null)
            {
                foreach (var s in _tree.suggestions)
                {
                    if (s.done) continue;
                    EditorGUILayout.BeginHorizontal();
                    PsdEditorUI.Text(s.message);
                    string button = s.kind == SuggestionKind.MoveToCommon ? "移到 Common"
                        : s.kind == SuggestionKind.ApplyRule ? "应用识别"
                        : s.kind == SuggestionKind.ApplyAi ? "应用 AI"
                        : "改用此图";
                    if (PsdEditorUI.CopyButton(button, GUILayout.Width(110)))
                        Run(() => ResourceActions.Apply(s, _tree, _settings));
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
