using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class AiRecognitionWindow : EditorWindow
    {
        AiReviewState state;
        [SerializeField] PsdTree workspaceTree;
        [SerializeField] PsdToolSettings workspaceSettings;
        bool showNaming;
        string workspaceFeedback,workspaceError;
        Action generateNext;
        readonly NodeNamingPanel naming=new NodeNamingPanel();
        readonly NineShrinkReviewPanel shrinkReview=new NineShrinkReviewPanel();
        Vector2 scroll;
        bool details, showReady, showHistory;
        string nodeSearch = "";
        int searchId = -1;
        readonly PsdNodePreview sourcePreview = new PsdNodePreview();
        readonly AiReviewImages images = new AiReviewImages(256);
        readonly Dictionary<int, int> repairTypes = new Dictionary<int, int>();
        readonly Dictionary<int, PsdNode> progressOptions = new Dictionary<int, PsdNode>();
        readonly HashSet<AiProposalRow> attention = new HashSet<AiProposalRow>();
        static readonly string[] Types = { "Button", "Toggle", "InputField", "ScrollView", "Dropdown", "Slider", "Progress" };
        internal static void Open(AiReviewState state)
        {
            var w=GetWindow<AiRecognitionWindow>("PSD 识别工作台");
            if(w.state!=state){w.scroll=Vector2.zero;w.showReady=w.showHistory=false;w.repairTypes.Clear();w.attention.Clear();}
            if(w.workspaceTree!=state.tree)w.generateNext=null;
            w.state=state;w.workspaceTree=state.tree;w.workspaceSettings=state.settings;
            w.images.Load(state.reportFolder);w.naming.Sync(state.tree);
            w.minSize=new Vector2(700,480);w.Show();
        }
        internal static void OpenWorkspace(PsdTree tree,PsdToolSettings settings)
        {
            var w=GetWindow<AiRecognitionWindow>("PSD 识别工作台");
            if(w.workspaceTree!=tree){w.generateNext=null;w.state=null;w.workspaceFeedback=w.workspaceError=null;}
            w.workspaceTree=tree;w.workspaceSettings=settings;
            if(AiMarker.Last?.tree==tree)w.state=AiMarker.Last;
            w.naming.Sync(tree);w.minSize=new Vector2(700,480);w.Show();
        }
        internal static void OpenNaming(PsdTree tree,Action next,int focus,PsdToolSettings settings=null)
        {
            OpenWorkspace(tree,settings??(AiMarker.Last?.tree==tree?AiMarker.Last.settings:PsdToolSettings.TryGet()));
            var w=GetWindow<AiRecognitionWindow>();
            if(next!=null)w.generateNext=next;
            w.showNaming=true;w.naming.Sync(tree);
            if(focus!=-1)w.naming.Focus(focus);
            w.scroll=Vector2.zero;w.Repaint();
        }
        internal static void RepaintActive(){foreach(var w in Resources.FindObjectsOfTypeAll<AiRecognitionWindow>())w.Repaint();}
        void OnDisable(){images.Dispose();sourcePreview.Dispose();naming.Dispose();shrinkReview.Dispose();}
        void OnGUI()
        {
            if(workspaceTree==null&&state!=null){workspaceTree=state.tree;workspaceSettings=state.settings;}
            PsdEditorUI.Header("PSD 识别工作台","Codex 识别 → 审核控件与补查 → 确认命名 → 生成 / 更新 Prefab");
            if(workspaceTree==null){PsdEditorUI.Notice("先在主窗口解析 PSD。",MessageType.Info);return;}
            CodexTaskWindow.Session.Bind(workspaceTree,workspaceSettings);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            try
            {
                CodexTaskWindow.Session.DrawEmbedded();
                if(CodexTaskRunner.Busy){Repaint();return;}
                if(!string.IsNullOrEmpty(workspaceFeedback))PsdEditorUI.Notice(workspaceFeedback,MessageType.Info);
                if(!string.IsNullOrEmpty(workspaceError))PsdEditorUI.Notice(workspaceError,MessageType.Warning);
                if(state?.tree==workspaceTree)DrawReview();
                else
                {
                    PsdEditorUI.Notice("识别结果会在这里显示；节点清单与命名可提前查看。",MessageType.Info);
                    DrawNaming();shrinkReview.Draw(workspaceTree,workspaceSettings);
                }
            }
            finally{EditorGUILayout.EndScrollView();}
        }
        void DrawNaming()
        {
            naming.Sync(workspaceTree);
            showNaming=PsdEditorUI.CopyFoldout(showNaming,"Prefab 命名与漏识别复核（待确认 "+naming.PendingCount+" 项）",true);
            if(showNaming)naming.Draw(workspaceTree,StartBatch,()=>Execute(()=>GenerateFromWorkbench(true)),workspaceSettings);
        }
        void StartBatch(int[] ids)
        {
            try{
                state?.PrepareFocusedRecognition();
                CodexTaskWindow.ExportAndOpen(workspaceTree,workspaceSettings,null,ids);
                naming.MarkSubmitted(ids);workspaceError=null;
            }catch(ExitGUIException){throw;}catch(Exception e){workspaceError=e.Message;}
            GUIUtility.ExitGUI();
        }
        void GenerateFromWorkbench(bool applyNames)
        {
            if(state!=null){state.PrepareFocusedRecognition();state.ApplySelected(true);}
            naming.Sync(workspaceTree);
            if(applyNames)naming.ApplyPending();
            if(naming.PendingCount>0)
            {
                showNaming=true;workspaceFeedback="还有 "+naming.PendingCount+" 个节点的命名待确认。可逐项应用，或点击“应用待确认名称并生成 / 更新 Prefab”。";
                return;
            }
            var expectedTree=workspaceTree;var expectedSettings=workspaceSettings;var expectedState=state;string plans=FeedbackReviewStamp();
            bool waiting=ButtonFeedbackWindow.Request(workspaceTree,workspaceSettings,()=>{
                try{CompleteGeneration();Repaint();}
                catch(Exception e){workspaceError=e.Message;if(state!=null)state.error=e.Message;Repaint();throw;}
                finally{EditorUtility.ClearProgressBar();}
            },()=>this!=null&&workspaceTree==expectedTree&&workspaceSettings==expectedSettings&&state==expectedState&&FeedbackReviewStamp()==plans);
            if(waiting)workspaceFeedback="请在按钮交互反馈面板确认后生成。";
        }
        string FeedbackReviewStamp()=>state==null?"":AiProtocol.Hash(string.Join("|",state.rows.Select(r=>r.selected+":"+r.applied+":"+r.error+":"+JsonUtility.ToJson(r.proposal))));
        void CompleteGeneration()
        {
            if(generateNext!=null){var callback=generateNext;generateNext=null;callback();if(state!=null){state.generated=true;state.SaveReport();}}
            else
            {
                var review=state??new AiReviewState{tree=workspaceTree,settings=workspaceSettings,externalTask=true,
                    signature=AiVisualInput.TaskSignature(workspaceTree,workspaceSettings),sourcePsdHash=CodexTaskBridge.HashFile(workspaceTree.psdPath)};
                review.AcceptNamingEdits();review.ApplyAndGenerate();
            }
            workspaceFeedback="Prefab 已生成 / 更新；请在 Unity 验收交互。";
        }
        internal static bool NeedsAttention(AiProposalRow row)
            => row != null && !row.applied && (row.error != null || !row.selected || row.proposal == null || row.proposal.requiresReview || AiPlanEngine.Structural(row.proposal));
        internal static bool IsIssue(AiRegionResult region)
            => region.status == AiRegionStatus.Failed || region.status == AiRegionStatus.Unsupported;
        void DrawReview()
        {
            images.Load(state.reportFolder); state.RefreshStatuses();
            if (!state.generationChecked) state.RefreshGenerationCheck();
            foreach (var row in state.rows.Where(NeedsAttention)) attention.Add(row);
            var indexed = state.rows.Select((row, index) => new { row, region = state.regions.Find(r => r.proposalIndex == index) }).ToArray();
            var pending = indexed.Where(x => !state.IsIgnored(x.row) && !x.row.applied && attention.Contains(x.row)).ToArray();
            var ready = indexed.Where(x => !state.IsIgnored(x.row) && !x.row.applied && !attention.Contains(x.row)).ToArray();
            var other = state.regions.Where(r => r.proposalIndex < 0 && IsIssue(r)).ToArray();
            var history = state.regions.Where(r => r.proposalIndex < 0 && !IsIssue(r)).ToArray();
            var done = indexed.Where(x => x.row.applied || state.IsIgnored(x.row)).ToArray();
            int selected = state.rows.Count(r => r.selected && !r.applied && r.error == null && !state.IsIgnored(r));
            int errors = state.regions.Count(IsIssue);
            using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card))
            {
                PsdEditorUI.Text(state.tree.panelName + " · 识别审核", PsdEditorUI.Title);
                PsdEditorUI.Text($"待处理 {pending.Length + other.Length}   ·   常规已选 {ready.Length}   ·   已应用 {state.rows.Count(r => r.applied)}", EditorStyles.wordWrappedLabel);
                PsdEditorUI.Text($"本次将应用 {selected} 项建议；{errors} 项异常隔离，{state.generationIssues.Count(i => i.placeholder)} 项补建占位，{state.generationIssues.Count(i => !i.placeholder)} 项保留外观待绑定。", EditorStyles.wordWrappedMiniLabel);
                if (state.preflightError != null) PsdEditorUI.Notice("生成前需处理：" + state.preflightError, MessageType.Warning);
                if (!string.IsNullOrEmpty(state.error)) PsdEditorUI.Notice(state.error, MessageType.Error);
                if (!string.IsNullOrEmpty(state.feedback)) PsdEditorUI.Notice(state.feedback, MessageType.Info);
                using(new EditorGUI.DisabledScope(selected==0))
                    if(PsdEditorUI.Action("应用已选 "+selected+" 项识别建议",true))Execute(()=>{state.ApplySelected(true);naming.Sync(workspaceTree);});
                if(PsdEditorUI.Action("生成 / 更新 Prefab"))Execute(()=>GenerateFromWorkbench(false));
                details = PsdEditorUI.CopyFoldout(details, "更多操作 / 绑定明细", true);
                if (details)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (PsdEditorUI.Action("全选可用项")) { foreach (var row in state.rows.Where(r => !r.applied && r.error == null && !state.IsIgnored(r))) row.selected = true; state.generationChecked = false; }
                        if (PsdEditorUI.Action("取消全选")) { foreach (var row in state.rows.Where(r => !r.applied)) row.selected = false; state.generationChecked = false; }
                    }
                    if (state.externalTask && PsdEditorUI.Action("人工调整后更新校验")) Execute(state.RecheckAfterManualEdits);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!File.Exists(Path.Combine(state.reportFolder ?? "", "校验报告.txt")) ))
                            if (PsdEditorUI.Action("校验报告")) EditorUtility.RevealInFinder(Path.Combine(state.reportFolder, "校验报告.txt"));
                        using (new EditorGUI.DisabledScope(!Directory.Exists(state.reportFolder)))
                            if (PsdEditorUI.Action("诊断目录")) EditorUtility.RevealInFinder(state.reportFolder);
                        if (state.externalTask && PsdEditorUI.Action("仅应用标记")) Execute(() => state.ApplySelected(true));
                    }
                    PsdEditorUI.Text(state.note ?? "", EditorStyles.wordWrappedMiniLabel);
                }
            }
            DrawNaming();
            shrinkReview.Draw(workspaceTree,workspaceSettings);
            PsdEditorUI.Divider("识别结果 · 待处理项");
            DrawNodeSearch();
            foreach (var issue in state.generationIssues) DrawGenerationIssue(issue);
            if (pending.Length + other.Length == 0)
                PsdEditorUI.Notice(state.generated ? state.generationIssues.Count > 0 ? "Prefab 已生成，交互待处理项请单独验收。" : "Prefab 已生成，请返回主窗口验收交互。" : "识别建议暂无待处理项。常规建议已折叠，可展开核对后生成。", MessageType.Info);
            foreach (var region in other) DrawCard(region, null);
            foreach (var entry in pending) DrawCard(entry.region, entry.row);
            showReady = PsdEditorUI.CopyFoldout(showReady, $"常规已选建议（{ready.Length}）· 已参与本次生成", true);
            if (showReady) foreach (var entry in ready) DrawCard(entry.region, entry.row);
            showHistory = PsdEditorUI.CopyFoldout(showHistory, $"已处理 / 非独立控件（{history.Length + done.Length}）", true);
            if (showHistory)
            {
                foreach (var region in history) DrawCard(region, null);
                foreach (var entry in done) DrawCard(entry.region, entry.row);
            }
        }
        void Execute(Action action)
        {
            try { action(); workspaceError = null; if(state!=null)state.error=null; } catch(ExitGUIException){throw;} catch (Exception e) { workspaceError=e.Message; if(state!=null)state.error=e.Message; }
            finally{EditorUtility.ClearProgressBar();}
            GUIUtility.ExitGUI();
        }
        void DrawCard(AiRegionResult region, AiProposalRow row)
        {
            int id = region?.rootId ?? row?.proposal?.nodes?.FirstOrDefault()?.layerId ?? -1;
            var proposal = row?.proposal;
            images.Load(region?.snapshotFolder ?? state.reportFolder);
            bool narrow = position.width < 690;
            using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Real task crop, never a generated mock-up.
                    var texture = images.Region(id);
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(narrow ? 76 : 104)))
                    {
                        var rect = GUILayoutUtility.GetRect(narrow ? 76 : 104, 78, GUILayout.ExpandWidth(false));
                        EditorGUI.DrawRect(rect, new Color(.16f, .16f, .16f));
                        if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                        else GUI.Label(rect, "暂无区域图", EditorStyles.centeredGreyMiniLabel);
                        using (new EditorGUI.DisabledScope(region == null&&state.tree.Find(id)==null))
                            if (GUI.Button(rect, new GUIContent("", "点击放大，并定位节点"), GUIStyle.none))
                            {
                                if(region!=null)AiRegionPreviewWindow.Open(state,region,proposal);
                                else PsdNodePreviewWindow.Open(state.tree,id,state.settings);
                            }
                        PsdEditorUI.Text(new GUIContent("PSD 快照 · 点击定位", "快照可能包含下拉展开态遮挡；绑定以来源图层和完整控件方案为准。"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true }, GUILayout.Width(narrow ? 76 : 104), GUILayout.Height(28));
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(170, position.width - (narrow ? 76 : 258) - 64))))
                    {
                        string title = proposal?.title ?? state.tree.Find(id)?.goName ?? "未定位的返回项";
                        if (row != null)
                        {
                            using (new EditorGUI.DisabledScope(row.applied || row.error != null || state.IsIgnored(row)))
                            {
                                bool selected = PsdEditorUI.CopyToggleLeft(title, row.selected, EditorStyles.boldLabel);
                                if (selected != row.selected) { row.selected = selected; state.generationChecked = false; }
                            }
                        }
                        else PsdEditorUI.Text(title, EditorStyles.boldLabel);
                        PsdEditorUI.Text(Status(region, row), EditorStyles.wordWrappedMiniLabel);
                        PsdEditorUI.Text(state.NodePath(id), EditorStyles.wordWrappedMiniLabel);
                        string message = row?.error ?? region?.message;
                        if (!string.IsNullOrEmpty(message)) PsdEditorUI.Text(message, EditorStyles.wordWrappedLabel);
                        if (!string.IsNullOrEmpty(proposal?.reason ?? region?.reason)) PsdEditorUI.Text(proposal?.reason ?? region.reason, EditorStyles.wordWrappedLabel);
                        if (proposal?.requiresReview == true) PsdEditorUI.Text("请核对部件和位置后勾选。", EditorStyles.wordWrappedMiniLabel);
                        if (details && proposal != null) DrawDetails(proposal);
                    }
                    if (!narrow)
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(154))) DrawActions(region, row, id);
                    }
                }
                if (narrow) using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(154))) DrawActions(region, row, id);
                }
            }
        }
        static string Status(AiRegionResult region, AiProposalRow row)
        {
            if (region?.status == AiRegionStatus.Ignored) return "已忽略 · 保留现状";
            if (row?.applied == true) return region?.status == AiRegionStatus.Generated ? "已生成 · 待验收" : "已应用标记";
            if (row?.error != null || (region != null && IsIssue(region))) return "需要人工处理 · 不阻断其余项";
            if (row != null) return $"{(row.selected ? "已勾选" : "待勾选确认")} · 置信度 {row.proposal?.confidence:0.00}" + (row.proposal != null && AiPlanEngine.Structural(row.proposal) ? " · 涉及结构调整" : "");
            return region?.status == AiRegionStatus.Covered ? "由父控件统一处理" : "非独立控件";
        }
        void DrawActions(AiRegionResult result, AiProposalRow row, int id)
        {
            using (new EditorGUI.DisabledScope(state.tree.Find(id) == null || CodexTaskRunner.Busy))
                if (PsdEditorUI.Action("重新请求 AI 识别")) Execute(() => StartFocused(id));
            if (row != null && !row.applied && row.error == null && !state.IsIgnored(row))
                if (PsdEditorUI.Action("应用此方案", true)) Execute(() => state.ApplySelected(true, row));
            using (new EditorGUI.DisabledScope(state.tree.Find(id) == null))
                if (PsdEditorUI.Action("定位节点")) PsdToolWindow.FocusNode(state.tree, state.settings, id);
            if (result?.type == "ScrollView" && state.tree.Find(id) != null)
            {
                var layoutNode = state.ScrollLayoutNode(result);
                using (new EditorGUI.DisabledScope(layoutNode == null))
                    if (PsdEditorUI.Action("排列与自适应…"))
                        ScrollLayoutWindow.Open(layoutNode, options => state.ConfigureScrollLayout(result, options));
                if (layoutNode == null) PsdEditorUI.Text("先应用结构方案，再配置新建列表的排列。", EditorStyles.wordWrappedMiniLabel);
            }
            if (result?.status == AiRegionStatus.Ignored)
            { if (PsdEditorUI.Action("恢复为待处理")) Execute(() => state.IgnoreRegion(result, true)); return; }
            if (result == null || !IsIssue(result)) return;
            var owner = state.StructuralOwner(result);
            if (owner != null)
            {
                PsdEditorUI.Text("已包含在父级结构方案中；将一起处理相关部件及独立子控件。覆盖仍保护属性锁。", EditorStyles.wordWrappedMiniLabel, GUILayout.Width(154));
                if (PsdEditorUI.Action("应用父级完整方案", true)) Execute(() => state.ApplyOwnerRegion(result));
            }
            if (AiProtocol.HasEdits(result.recoveryProposal))
            {
                PsdEditorUI.Text("仅覆盖本条人工/PSD 决定；仍保护属性锁与结构安全。", EditorStyles.wordWrappedMiniLabel, GUILayout.Width(154));
                if (PsdEditorUI.Action("AI 覆盖并应用此项"))
                {
                    var conflicts = state.PendingConflicts(result);
                    bool replace = conflicts.Count > 0;
                    if (!replace || EditorUtility.DisplayDialog("采用本条完整方案", "将停用以下尚未应用的重叠建议，改用本条完整方案：\n" + string.Join("\n", conflicts.Select(r => r.proposal.title)) + "\n已应用节点和属性锁仍受保护。", "替换并应用", "取消"))
                        Execute(() => state.OverrideRegion(result, replace));
                }
            }
            if (result.type == "Progress" && state.tree.Find(id) != null)
            {
                if (PsdEditorUI.Action("配置填充方式 / 方向")) progressOptions[id] = new PsdNode { progressFill = ProgressFillMode.Radial360 };
                if (progressOptions.TryGetValue(id, out var options))
                {
                    float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 58;
                    PsdProgressUI.Draw(options); EditorGUIUtility.labelWidth = labelWidth;
                    if (PsdEditorUI.Action("按此配置生成建议")) Execute(() => state.ConfigureProgress(result, options));
                }
            }
            if (state.tree.Find(id) != null)
            {
                int index = repairTypes.TryGetValue(id, out int saved) ? saved : Math.Max(0, Array.IndexOf(Types, result.type));
                PsdEditorUI.Text("指定类型 · 本地校验", EditorStyles.miniLabel, GUILayout.Width(154));
                index = EditorGUILayout.Popup(index, Types, GUILayout.Width(154)); repairTypes[id] = index;
                if (PsdEditorUI.Action("本地重新校验（非 AI）")) Execute(() => state.RetryRegion(result, Types[index]));
            }
            if (PsdEditorUI.Action("忽略建议，保留现状")) Execute(() => state.IgnoreRegion(result));
        }
        void StartFocused(int id)
        {
            state.PrepareFocusedRecognition();
            CodexTaskWindow.ExportAndOpen(state.tree, state.settings, id);
        }
        void DrawNodeSearch()
        {
            using (PsdEditorUI.Section("漏识别补查 · 搜索全部来源节点"))
            {
                nodeSearch = EditorGUILayout.TextField(new GUIContent("名称 / 路径 / ID", "包含未被 AI 列出的 Image、文字和组；输入名称片段或完整路径。"), nodeSearch);
                if (string.IsNullOrWhiteSpace(nodeSearch))
                { PsdEditorUI.Text("找不到控件？搜索原稿中的节点，直接提交单项 AI 补识别。", EditorStyles.wordWrappedMiniLabel); return; }
                var matches = state.tree.nodes.Where(n => n.status != NodeStatus.PendingDelete &&
                    (state.NodePath(n.layerId).IndexOf(nodeSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (n.psdName ?? "").IndexOf(nodeSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 || n.layerId.ToString() == nodeSearch.Trim())).ToList();
                PsdEditorUI.Text($"找到 {matches.Count} 项" + (matches.Count > 12 ? "，显示前 12 项，请细化关键词。" : ""), EditorStyles.miniLabel);
                foreach (var node in matches.Take(12))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Toggle(searchId == node.layerId, state.NodePath(node.layerId) + " [" + node.layerId + "]", "Button")) searchId = node.layerId;
                        if (PsdEditorUI.CopyButton("定位", GUILayout.Width(48))) PsdToolWindow.FocusNode(state.tree, state.settings, node.layerId);
                    }
                var selected = matches.FirstOrDefault(n => n.layerId == searchId);
                if (selected != null)
                {
                    sourcePreview.Draw(state.tree, selected, 110,state.settings);
                    using (new EditorGUI.DisabledScope(CodexTaskRunner.Busy))
                        if (PsdEditorUI.Action("让 AI 重新识别此节点及相关部件", true)) Execute(() => StartFocused(selected.layerId));
                }
            }
        }
        void DrawGenerationIssue(GenerationIssue issue)
        {
            using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card))
            {
                PsdEditorUI.Text((issue.placeholder ? state.generated ? "已补建占位 · " : "将补建占位 · " : state.generated ? "已生成外观 · " : "生成前预检 · ") + issue.nodePath, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(76, 70, GUILayout.Width(76));
                    var texture = images.Region(issue.layerId);
                    if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                    else GUI.Label(rect, "暂无区域图", EditorStyles.centeredGreyMiniLabel);
                    if(state.tree.Find(issue.layerId)!=null&&GUI.Button(rect,new GUIContent("","放大原稿并定位节点"),GUIStyle.none))
                        PsdNodePreviewWindow.Open(state.tree,issue.layerId,state.settings);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        PsdEditorUI.Text(issue.message, EditorStyles.wordWrappedLabel);
                        PsdEditorUI.Text(issue.resolution, EditorStyles.wordWrappedMiniLabel);
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(154)))
                        if (PsdEditorUI.Action("定位并修复部件")) PsdToolWindow.FocusNode(state.tree, state.settings, issue.layerId);
                }
            }
        }
        void DrawDetails(AiControlProposal proposal)
        {
            foreach (var group in proposal.groups ?? new List<AiGroupEdit>())
                if (group != null) PsdEditorUI.Text($"补组 {group.name} [{group.groupId}] → 父 {group.parentId} · {group.templateId}/{group.slot}", EditorStyles.wordWrappedMiniLabel);
            foreach (var edit in proposal.nodes ?? new List<AiNodeEdit>())
            {
                if (edit == null) continue;
                var node = state.tree.Find(edit.layerId);
                string label = $"[{edit.layerId}] {node?.goName}：";
                if (edit.setTemplate) label += $"类型 {node?.templateId} → {edit.templateId ?? "空"}  ";
                if (edit.setSlot) label += $"部件 {node?.slot} → {edit.slot ?? "空"}  ";
                if (edit.setParent) label += $"父 {node?.parentId} → {edit.parentId}";
                PsdEditorUI.Text(label, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
