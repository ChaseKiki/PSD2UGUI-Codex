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
        Vector2 scroll;
        bool details, showReady, showHistory;
        readonly AiReviewImages images = new AiReviewImages(256);
        readonly Dictionary<int, int> repairTypes = new Dictionary<int, int>();
        readonly Dictionary<int, PsdNode> progressOptions = new Dictionary<int, PsdNode>();
        readonly HashSet<AiProposalRow> attention = new HashSet<AiProposalRow>();
        static readonly string[] Types = { "Button", "Toggle", "InputField", "ScrollView", "Dropdown", "Slider", "Progress" };
        internal static void Open(AiReviewState state)
        {
            var window = GetWindow<AiRecognitionWindow>("AI 识别结果");
            if (window.state != state) { window.scroll = Vector2.zero; window.showReady = window.showHistory = false; window.repairTypes.Clear(); window.attention.Clear(); }
            window.state = state; window.images.Load(state.reportFolder);
            window.minSize = new Vector2(520, 460); window.Show();
        }
        void OnDisable() => images.Dispose();
        internal static bool NeedsAttention(AiProposalRow row)
            => row != null && !row.applied && (row.error != null || !row.selected || row.proposal == null || row.proposal.requiresReview || AiPlanEngine.Structural(row.proposal));
        internal static bool IsIssue(AiRegionResult region)
            => region.status == AiRegionStatus.Failed || region.status == AiRegionStatus.Unsupported;
        void OnGUI()
        {
            if (state?.tree == null) { PsdEditorUI.Header("识别结果", "先在 PSD 工具窗口点击 Codex 识别。"); return; }
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
                EditorGUILayout.LabelField(state.tree.panelName + " · 识别审核", PsdEditorUI.Title);
                EditorGUILayout.LabelField($"待处理 {pending.Length + other.Length}   ·   常规已选 {ready.Length}   ·   已应用 {state.rows.Count(r => r.applied)}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"本次将应用 {selected} 项建议；{errors} 项异常隔离，{state.generationIssues.Count(i => i.placeholder)} 项补建占位，{state.generationIssues.Count(i => !i.placeholder)} 项保留外观待绑定。", EditorStyles.wordWrappedMiniLabel);
                if (state.preflightError != null) EditorGUILayout.HelpBox("生成前需处理：" + state.preflightError, MessageType.Warning);
                if (!string.IsNullOrEmpty(state.error)) EditorGUILayout.HelpBox(state.error, MessageType.Error);
                using (new EditorGUI.DisabledScope(!state.externalTask && selected == 0))
                    if (PsdEditorUI.Action(state.externalTask ? selected > 0 ? $"应用已选 {selected} 项并生成 Prefab" : "按现有节点生成 Prefab" : $"应用已选 {selected} 项", true))
                        Execute(() => { if (state.externalTask) state.ApplyAndGenerate(); else state.ApplySelected(true); });
                details = EditorGUILayout.Foldout(details, "更多操作 / 绑定明细", true);
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
                    EditorGUILayout.LabelField(state.note ?? "", EditorStyles.wordWrappedMiniLabel);
                }
            }
            PsdEditorUI.Divider("识别结果 · 待处理项");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var issue in state.generationIssues) DrawGenerationIssue(issue);
            if (pending.Length + other.Length == 0)
                EditorGUILayout.HelpBox(state.generated ? state.generationIssues.Count > 0 ? "Prefab 已生成，交互待处理项请单独验收。" : "Prefab 已生成，请返回主窗口验收交互。" : "识别建议暂无待处理项。常规建议已折叠，可展开核对后生成。", MessageType.Info);
            foreach (var region in other) DrawCard(region, null);
            foreach (var entry in pending) DrawCard(entry.region, entry.row);
            showReady = EditorGUILayout.Foldout(showReady, $"常规已选建议（{ready.Length}）· 已参与本次生成", true);
            if (showReady) foreach (var entry in ready) DrawCard(entry.region, entry.row);
            showHistory = EditorGUILayout.Foldout(showHistory, $"已处理 / 非独立控件（{history.Length + done.Length}）", true);
            if (showHistory)
            {
                foreach (var region in history) DrawCard(region, null);
                foreach (var entry in done) DrawCard(entry.region, entry.row);
            }
            EditorGUILayout.EndScrollView();
        }
        void Execute(Action action)
        {
            try { action(); state.error = null; } catch (Exception e) { state.error = e.Message; }
            GUIUtility.ExitGUI();
        }
        void DrawCard(AiRegionResult region, AiProposalRow row)
        {
            int id = region?.rootId ?? row?.proposal?.nodes?.FirstOrDefault()?.layerId ?? -1;
            var proposal = row?.proposal;
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
                        using (new EditorGUI.DisabledScope(region == null))
                            if (GUI.Button(rect, new GUIContent("", "点击放大，并在整图中查看位置与涉及部件"), GUIStyle.none)) AiRegionPreviewWindow.Open(state, region, proposal);
                        EditorGUILayout.LabelField(new GUIContent("PSD 快照 · 点击定位", "快照可能包含下拉展开态遮挡；绑定以来源图层和完整控件方案为准。"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true }, GUILayout.Width(narrow ? 76 : 104), GUILayout.Height(28));
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(170, position.width - (narrow ? 76 : 258) - 64))))
                    {
                        string title = proposal?.title ?? state.tree.Find(id)?.goName ?? "未定位的返回项";
                        if (row != null)
                        {
                            using (new EditorGUI.DisabledScope(row.applied || row.error != null || state.IsIgnored(row)))
                            {
                                bool selected = EditorGUILayout.ToggleLeft(title, row.selected, EditorStyles.boldLabel);
                                if (selected != row.selected) { row.selected = selected; state.generationChecked = false; }
                            }
                        }
                        else EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(Status(region, row), EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.LabelField(state.NodePath(id), EditorStyles.wordWrappedMiniLabel);
                        string message = row?.error ?? region?.message;
                        if (!string.IsNullOrEmpty(message)) EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
                        if (!string.IsNullOrEmpty(proposal?.reason ?? region?.reason)) EditorGUILayout.LabelField(proposal?.reason ?? region.reason, EditorStyles.wordWrappedLabel);
                        if (proposal?.requiresReview == true) EditorGUILayout.LabelField("请核对部件和位置后勾选。", EditorStyles.wordWrappedMiniLabel);
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
                if (layoutNode == null) EditorGUILayout.LabelField("先应用结构方案，再配置新建列表的排列。", EditorStyles.wordWrappedMiniLabel);
            }
            if (result?.status == AiRegionStatus.Ignored)
            { if (PsdEditorUI.Action("恢复为待处理")) Execute(() => state.IgnoreRegion(result, true)); return; }
            if (result == null || !IsIssue(result)) return;
            var owner = state.StructuralOwner(result);
            if (owner != null)
            {
                EditorGUILayout.LabelField("已包含在父级结构方案中；将一起处理相关部件及独立子控件。覆盖仍保护属性锁。", EditorStyles.wordWrappedMiniLabel, GUILayout.Width(154));
                if (PsdEditorUI.Action("应用父级完整方案", true)) Execute(() => state.ApplyOwnerRegion(result));
            }
            if (AiProtocol.HasEdits(result.recoveryProposal))
            {
                EditorGUILayout.LabelField("仅覆盖本条人工/PSD 决定；仍保护属性锁与结构安全。", EditorStyles.wordWrappedMiniLabel, GUILayout.Width(154));
                if (PsdEditorUI.Action("AI 覆盖并应用此项")) Execute(() => state.OverrideRegion(result));
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
                EditorGUILayout.LabelField("指定类型后重校验", EditorStyles.miniLabel, GUILayout.Width(154));
                index = EditorGUILayout.Popup(index, Types, GUILayout.Width(154)); repairTypes[id] = index;
                if (PsdEditorUI.Action("重校验此项")) Execute(() => state.RetryRegion(result, Types[index]));
            }
            if (PsdEditorUI.Action("忽略建议，保留现状")) Execute(() => state.IgnoreRegion(result));
        }
        void DrawGenerationIssue(GenerationIssue issue)
        {
            using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card))
            {
                EditorGUILayout.LabelField((issue.placeholder ? state.generated ? "已补建占位 · " : "将补建占位 · " : state.generated ? "已生成外观 · " : "生成前预检 · ") + issue.nodePath, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(76, 70, GUILayout.Width(76));
                    var texture = images.Region(issue.layerId);
                    if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                    else GUI.Label(rect, "暂无区域图", EditorStyles.centeredGreyMiniLabel);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(issue.message, EditorStyles.wordWrappedLabel);
                        EditorGUILayout.LabelField(issue.resolution, EditorStyles.wordWrappedMiniLabel);
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(154)))
                        if (PsdEditorUI.Action("定位并修复部件")) PsdToolWindow.FocusNode(state.tree, state.settings, issue.layerId);
                }
            }
        }
        void DrawDetails(AiControlProposal proposal)
        {
            foreach (var group in proposal.groups ?? new List<AiGroupEdit>())
                if (group != null) EditorGUILayout.LabelField($"补组 {group.name} [{group.groupId}] → 父 {group.parentId} · {group.templateId}/{group.slot}", EditorStyles.wordWrappedMiniLabel);
            foreach (var edit in proposal.nodes ?? new List<AiNodeEdit>())
            {
                if (edit == null) continue;
                var node = state.tree.Find(edit.layerId);
                string label = $"[{edit.layerId}] {node?.goName}：";
                if (edit.setTemplate) label += $"类型 {node?.templateId} → {edit.templateId ?? "空"}  ";
                if (edit.setSlot) label += $"部件 {node?.slot} → {edit.slot ?? "空"}  ";
                if (edit.setParent) label += $"父 {node?.parentId} → {edit.parentId}";
                EditorGUILayout.LabelField(label, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
