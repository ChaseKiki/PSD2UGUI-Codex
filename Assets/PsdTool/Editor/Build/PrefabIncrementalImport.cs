using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PsdTool
{
    /// <summary>在 LoadPrefabContents 得到的原树上修改，保留已有物体和组件的 FileID。</summary>
    public static class PrefabIncrementalImport
    {
        public sealed class Report
        {
            public readonly List<string> lines = new List<string>();
            public bool changed;
            public bool initializing;
            public readonly List<Removal> removals = new List<Removal>();
        }

        public sealed class Removal { public string key; public string component; public string label; }

        public static string StatePath(PsdTree tree, PsdToolSettings settings) =>
            settings.toolDataRoot + "/ImportState/" + Source(tree) + ".asset";

        static string Source(PsdTree tree)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tree));
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("请先保存节点树资产，再进行增量导入。");
            return guid;
        }

        public static Report Preview(GameObject draft, PsdTree tree, PsdToolSettings settings) =>
            Run(draft, tree, settings, false, out _);

        public static GameObject Apply(GameObject draft, PsdTree tree, PsdToolSettings settings)
        {
            var report = Run(draft, tree, settings, true, out var prefab);
            tree.warnings.AddRange(report.lines);
            return prefab;
        }

        static Report Run(GameObject draft, PsdTree tree, PsdToolSettings settings, bool save, out GameObject prefab)
        {
            var report = new Report();
            string source = Source(tree);
            string statePath = StatePath(tree, settings);
            var state = AssetDatabase.LoadAssetAtPath<PsdImportState>(statePath);
            string path = tree.PrefabPath(settings);
            // 已有资源优先跟随 GUID；移动目录后不会凭旧路径重新建一份。
            string priorPath = AssetDatabase.GUIDToAssetPath(tree.prefabGuid ?? string.Empty);
            if (!string.IsNullOrEmpty(priorPath)) path = priorPath;
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Regular)
                throw new InvalidOperationException("当前增量导入仅支持普通 Prefab；Variant 和模型 Prefab 请先使用独立输出路径。");
            var baseline = state != null ? state.nodes : new List<ImportedNode>();
            report.initializing = state == null || baseline.Count == 0;
            var candidate = Map(draft, source);
            var next = Capture(candidate, source);
            // 已从 PSD 消失的生成节点/组件仍保留基线，直到程序显式确认删除。
            foreach (var prior in baseline)
            {
                var nextNode = next.Find(n => n.key == prior.key);
                if (nextNode == null)
                {
                    var retained = JsonUtility.FromJson<ImportedNode>(JsonUtility.ToJson(prior));
                    retained.removed = true; next.Add(retained);
                }
                else foreach (var c in prior.components)
                    if (!nextNode.components.Exists(x => x.type == c.type))
                    {
                        var retained = JsonUtility.FromJson<ImportedComponent>(JsonUtility.ToJson(c));
                        retained.removed = true; nextNode.components.Add(retained);
                    }
            }
            var pendingLocks = new Dictionary<PsdNode, PropGroup>();
            GameObject live = prefab != null ? PrefabUtility.LoadPrefabContents(path) : null;
            bool loadedContents = live != null;
            byte[] oldPrefab = prefab != null ? File.ReadAllBytes(PathUtil.ToFullPath(path)) : null;
            string oldState = state != null ? EditorJsonUtility.ToJson(state) : null;
            try
            {
                if (live == null)
                {
                    live = Object.Instantiate(draft);
                    live.name = draft.name;
                    report.changed = true;
                    report.lines.Add("新增整个界面，并记录首次生成基线。");
                }
                else
                {
                    AdoptLegacy(live, draft, source, report);
                    Merge(candidate, Map(live, source), baseline, tree, source, report, pendingLocks);
                }
                if (!save) return report;

                foreach (var error in ControlReferenceValidator.Validate(live))
                    report.lines.Add("合并后需复核（可能来自已锁定的人工设置）：" + error);

                PathUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                if (report.changed)
                {
                    prefab = PrefabUtility.SaveAsPrefabAsset(live, path, out bool success);
                    if (!success) throw new IOException("保存 Prefab 失败：" + path);
                }
                bool newState = state == null;
                if (newState)
                {
                    state = ScriptableObject.CreateInstance<PsdImportState>();
                    PathUtil.EnsureFolder(Path.GetDirectoryName(statePath).Replace('\\', '/'));
                    AssetDatabase.CreateAsset(state, statePath);
                }
                state.sourceGuid = source;
                state.prefabGuid = AssetDatabase.AssetPathToGUID(path);
                state.nodes = next;
                if (newState || EditorJsonUtility.ToJson(state) != oldState)
                {
                    EditorUtility.SetDirty(state);
                    AssetDatabase.SaveAssetIfDirty(state);
                }
                foreach (var pair in pendingLocks) pair.Key.locks |= pair.Value;
                tree.prefabGuid = state.prefabGuid;
                foreach (var n in tree.nodes)
                {
                    if (n.status == NodeStatus.New) n.status = NodeStatus.Synced;
                    n.forceUpdateGroups = PropGroup.None;
                }
                EditorUtility.SetDirty(tree);
                if (report.lines.Count == 0) report.lines.Add("无 Prefab 变更。");
                return report;
            }
            catch
            {
                if (save && oldPrefab != null)
                {
                    File.WriteAllBytes(PathUtil.ToFullPath(path), oldPrefab);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                if (state != null && oldState != null)
                {
                    EditorJsonUtility.FromJsonOverwrite(oldState, state);
                    EditorUtility.SetDirty(state);
                    AssetDatabase.SaveAssetIfDirty(state);
                }
                throw;
            }
            finally
            {
                if (live != null)
                {
                    if (loadedContents) PrefabUtility.UnloadPrefabContents(live);
                    else Object.DestroyImmediate(live);
                }
            }
        }

        static Dictionary<string, GameObject> Map(GameObject root, string source)
        {
            var map = new Dictionary<string, GameObject>();
            foreach (var id in root.GetComponentsInChildren<PsdGeneratedNode>(true))
            {
                if (id.sourceGuid != source) continue;
                if (string.IsNullOrEmpty(id.key) || map.ContainsKey(id.key))
                    throw new InvalidOperationException("存在重复或空的 PSD 节点标识：" + id.key + "。请先重新关联，不能复制生成节点的标识组件。");
                map.Add(id.key, id.gameObject);
            }
            return map;
        }

        static void AdoptLegacy(GameObject live, GameObject draft, string source, Report report)
        {
            var rootId = live.GetComponent<PsdGeneratedNode>();
            if (rootId != null)
            {
                if (rootId.sourceGuid != source) throw new InvalidOperationException("目标 Prefab 属于另一份节点树，请更换输出路径。");
                return;
            }
            // 旧版没有基线：只对路径唯一的同名节点建立标识，保留当前属性。
            foreach (var id in draft.GetComponentsInChildren<PsdGeneratedNode>(true))
            {
                Transform cursor = live.transform;
                var names = new Stack<string>();
                for (var t = id.transform; t != draft.transform; t = t.parent) names.Push(t.name);
                foreach (var name in names)
                {
                    var matches = new List<Transform>();
                    foreach (Transform c in cursor) if (c.name == name) matches.Add(c);
                    if (matches.Count > 1) throw new InvalidOperationException("旧版迁移遇到重名节点：" + name + "，请先将同级节点改为唯一名称。");
                    cursor = matches.Count == 1 ? matches[0] : null;
                    if (cursor == null) break;
                }
                if (cursor == null) continue;
                if (cursor.GetComponent<PsdGeneratedNode>() != null) continue;
                var attached = cursor.gameObject.AddComponent<PsdGeneratedNode>();
                attached.sourceGuid = source;
                attached.key = id.key;
                report.changed = true;
            }
            report.lines.Add("首次接管旧 Prefab：按唯一层级路径建立映射，当前属性不同于新输出的部分将保留并锁定。");
        }

        static void Merge(Dictionary<string, GameObject> candidate, Dictionary<string, GameObject> current,
            List<ImportedNode> baseline, PsdTree tree, string source, Report report, Dictionary<PsdNode, PropGroup> locks)
        {
            var old = baseline.ToDictionary(n => n.key);
            var added = new HashSet<string>();
            var addedComponents = new HashSet<string>();
            foreach (var pair in candidate)
            {
                if (current.ContainsKey(pair.Key)) continue;
                // 用户删除过的节点不会默默复活。
                if (old.TryGetValue(pair.Key, out var deleted) && !deleted.removed) { report.lines.Add("保留人工删除：" + pair.Value.name); continue; }
                var go = new GameObject(pair.Value.name, typeof(RectTransform));
                var id = go.AddComponent<PsdGeneratedNode>();
                id.sourceGuid = source; id.key = pair.Key;
                current.Add(pair.Key, go); added.Add(pair.Key);
                report.changed = true;
                report.lines.Add("新增节点：" + pair.Value.name);
            }
            foreach (var pair in current)
                if (!candidate.ContainsKey(pair.Key))
                {
                    report.lines.Add("待删除（已保留物体及引用）：" + pair.Value.name);
                    report.removals.Add(new Removal { key = pair.Key, label = pair.Value.name });
                }

            // 先确定父子关系、再建立全部组件，最后才能映射组件间引用。
            foreach (var pair in candidate)
            {
                if (!current.TryGetValue(pair.Key, out var target)) continue;
                string parentKey = ParentKey(pair.Value, source);
                var owner = Owner(tree, pair.Key);
                string existingParent = ParentKey(target, source);
                old.TryGetValue(pair.Key, out var before);
                bool parentLocked = IsLocked(owner, PropGroup.Parent) ||
                    (!Force(owner, PropGroup.Parent) && !added.Contains(pair.Key) && before != null && existingParent != before.parent);
                if (parentLocked)
                {
                    if (existingParent != parentKey) Preserve(owner, PropGroup.Parent, target.name + " / Parent", locks, report);
                    continue;
                }
                if (pair.Key != "root" && current.TryGetValue(parentKey ?? string.Empty, out var parent) && target.transform.parent != parent.transform)
                {
                    if (parent.transform.IsChildOf(target.transform)) throw new InvalidOperationException("父子关系会产生循环：" + target.name);
                    target.transform.SetParent(parent.transform, false); report.changed = true;
                    report.lines.Add("更新父节点：" + target.name);
                }
            }
            foreach (var pair in candidate)
            {
                if (!current.TryGetValue(pair.Key, out var target)) continue;
                old.TryGetValue(pair.Key, out var before);
                foreach (var c in Components(pair.Value))
                {
                    if (FindComponent(target, c.Key) != null) continue;
                    if (!added.Contains(pair.Key) && before != null && before.components.Exists(x => x.type == c.Key && !x.removed))
                    { report.lines.Add("保留人工删除组件：" + target.name + "/" + c.Value.GetType().Name); continue; }
                    if (IsLocked(Owner(tree, pair.Key), PropGroup.Component)) continue;
                    if (!(c.Value is GameObject) && !(c.Value is Transform))
                    {
                        target.AddComponent(c.Value.GetType()); report.changed = true;
                        addedComponents.Add(pair.Key + "|" + c.Key);
                        report.lines.Add("新增组件：" + target.name + "/" + c.Value.GetType().Name);
                    }
                }
            }
            foreach (var pair in candidate)
            {
                if (!current.TryGetValue(pair.Key, out var target)) continue;
                old.TryGetValue(pair.Key, out var before);
                var owner = Owner(tree, pair.Key);
                if (before != null)
                    foreach (var component in before.components)
                        if (!Components(pair.Value).ContainsKey(component.type) && FindComponent(target, component.type) != null)
                        {
                            report.lines.Add("待删除组件（已保留）：" + target.name + "/" + component.type.Split(',')[0]);
                            report.removals.Add(new Removal { key = pair.Key, component = component.type, label = target.name + "/" + component.type.Split(',')[0] });
                            var obsolete = FindComponent(target, component.type);
                            // 升级结构时，未手改过的旧布局/裁剪/图像先停用，避免覆盖新坐标或重复绘制 FX。
                            if ((obsolete is UnityEngine.UI.LayoutGroup || obsolete is UnityEngine.UI.ContentSizeFitter ||
                                 obsolete is UnityEngine.UI.RectMask2D || obsolete is UnityEngine.UI.Mask || obsolete is UnityEngine.UI.Graphic) &&
                                !IsLocked(owner, PropGroup.Component))
                            {
                                var serialized = new SerializedObject(obsolete);
                                bool unchanged = Properties(serialized).All(p => component.properties.Exists(v =>
                                    v.path == p.propertyPath && v.value == Fingerprint(p, source)));
                                var enabled = serialized.FindProperty("m_Enabled");
                                if (unchanged && enabled != null && enabled.boolValue)
                                {
                                    enabled.boolValue = false;
                                    serialized.ApplyModifiedPropertiesWithoutUndo(); report.changed = true;
                                    report.lines.Add("已停用旧版生成布局/裁剪/图像组件（可确认删除）：" + target.name);
                                }
                            }
                        }
                foreach (var c in Components(pair.Value))
                {
                    var destination = FindComponent(target, c.Key);
                    if (destination == null) continue;
                    var prior = before?.components.Find(x => x.type == c.Key);
                    var src = new SerializedObject(c.Value);
                    var dst = new SerializedObject(destination);
                    foreach (var p in Properties(src))
                    {
                        var q = dst.FindProperty(p.propertyPath);
                        if (q == null) continue;
                        string incoming = Fingerprint(p, source);
                        string actual = Fingerprint(q, source);
                        if (incoming == actual) continue;
                        var group = Group(c.Value, p.propertyPath);
                        var last = prior?.properties.Find(x => x.path == p.propertyPath);
                        bool newlyGenerated = added.Contains(pair.Key) || addedComponents.Contains(pair.Key + "|" + c.Key);
                        bool manuallyChanged = !newlyGenerated && (last != null ? actual != last.value : true);
                        if (IsLocked(owner, group) || (!Force(owner, group) && manuallyChanged))
                        { Preserve(owner, group, target.name + "/" + c.Value.GetType().Name + "/" + p.propertyPath, locks, report); continue; }
                        dst.CopyFromSerializedProperty(p);
                        Remap(dst.FindProperty(p.propertyPath), current, source);
                        report.changed = true;
                        report.lines.Add("修改：" + target.name + "/" + p.propertyPath);
                    }
                    dst.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        static void Preserve(PsdNode owner, PropGroup group, string label, Dictionary<PsdNode, PropGroup> locks, Report report)
        {
            if (owner != null) { locks.TryGetValue(owner, out var current); locks[owner] = current | group; }
            report.lines.Add("保留人工修改/锁定：" + label);
        }

        public static void ConfirmRemovals(GameObject draft, PsdTree tree, PsdToolSettings settings)
        {
            var report = Preview(draft, tree, settings);
            if (report.removals.Count == 0) return;
            if (!EditorUtility.DisplayDialog("确认删除生成内容", "将删除以下节点/组件，指向它们的引用可能变为 Missing：\n" +
                string.Join("\n", report.removals.Select(r => r.label)) + "\n人工添加的子物体和组件也会随被删除的父物体一起删除，请先移走需要保留的内容。", "删除列出的内容", "取消")) return;
            string path = AssetDatabase.GUIDToAssetPath(tree.prefabGuid);
            var live = PrefabUtility.LoadPrefabContents(path);
            byte[] backup = File.ReadAllBytes(PathUtil.ToFullPath(path));
            try
            {
                var map = Map(live, Source(tree));
                foreach (var removal in report.removals)
                {
                    if (!map.TryGetValue(removal.key, out var go) || go == null || go == live) continue;
                    if (string.IsNullOrEmpty(removal.component)) Object.DestroyImmediate(go);
                    else
                    {
                        var c = FindComponent(go, removal.component);
                        if (c != null && !(c is Transform) && !(c is GameObject)) Object.DestroyImmediate(c);
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(live, path, out bool ok);
                if (!ok) throw new IOException("删除确认保存失败。");
            }
            catch
            {
                File.WriteAllBytes(PathUtil.ToFullPath(path), backup);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                throw;
            }
            finally { PrefabUtility.UnloadPrefabContents(live); }
        }

        static bool IsLocked(PsdNode node, PropGroup group) => node != null && (node.locks & group) != 0;
        static bool Force(PsdNode node, PropGroup group) => node != null && (node.forceUpdateGroups & group) != 0;

        static PsdNode Owner(PsdTree tree, string key) => tree.nodes.Find(n =>
        {
            string k = string.IsNullOrEmpty(n.stableKey) ? "layer:" + n.layerId : n.stableKey;
            return key == k || key.StartsWith(k + "/", StringComparison.Ordinal);
        });

        static string ParentKey(GameObject go, string source)
        {
            if (go.transform.parent == null) return null;
            var id = go.transform.parent.GetComponent<PsdGeneratedNode>();
            return id != null && id.sourceGuid == source ? id.key : "manual-parent";
        }

        static List<ImportedNode> Capture(Dictionary<string, GameObject> map, string source)
        {
            var nodes = new List<ImportedNode>();
            foreach (var pair in map.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var node = new ImportedNode { key = pair.Key, parent = ParentKey(pair.Value, source) };
                foreach (var c in Components(pair.Value))
                {
                    var component = new ImportedComponent { type = c.Key };
                    foreach (var p in Properties(new SerializedObject(c.Value)))
                        component.properties.Add(new ImportedProperty { path = p.propertyPath, value = Fingerprint(p, source), group = Group(c.Value, p.propertyPath) });
                    node.components.Add(component);
                }
                nodes.Add(node);
            }
            return nodes;
        }

        static Dictionary<string, Object> Components(GameObject go)
        {
            var result = new Dictionary<string, Object> { ["GameObject"] = go };
            var counts = new Dictionary<Type, int>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null || c is PsdGeneratedNode || c is CanvasRenderer) continue;
                var type = c.GetType();
                counts.TryGetValue(type, out int index); counts[type] = index + 1;
                result.Add(type.AssemblyQualifiedName + "#" + index, c);
            }
            return result;
        }

        static Object FindComponent(GameObject go, string type) => Components(go).TryGetValue(type, out var obj) ? obj : null;

        static IEnumerable<SerializedProperty> Properties(SerializedObject obj)
        {
            var p = obj.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = false;
                string name = p.propertyPath;
                if (obj.targetObject is GameObject)
                { if (name == "m_Name" || name == "m_IsActive") yield return p.Copy(); continue; }
                if (name == "m_Script" || name == "m_ObjectHideFlags" || name == "m_GameObject" || name.StartsWith("m_Prefab") ||
                    name == "m_CorrespondingSourceObject" || name.StartsWith("m_Editor") ||
                    name.StartsWith("m_On") || name.StartsWith("m_on") || p.type.Contains("Event") ||
                    name == "m_textInfo" || name == "m_mesh" || name == "m_sharedMaterials" || name == "m_subTextObjects") continue;
                if (obj.targetObject is Transform && name != "m_LocalPosition" && name != "m_LocalRotation" && name != "m_LocalScale" &&
                    name != "m_AnchorMin" && name != "m_AnchorMax" && name != "m_AnchoredPosition" && name != "m_SizeDelta" && name != "m_Pivot") continue;
                if (Supported(p)) yield return p.Copy();
            }
        }

        static PropGroup Group(Object obj, string path)
        {
            if (path == "m_Name") return PropGroup.Name;
            if (path == "m_IsActive") return PropGroup.Active;
            if (obj is Transform) return path == "m_AnchorMin" || path == "m_AnchorMax" || path == "m_Pivot" ? PropGroup.Anchor : PropGroup.Rect;
            if (path == "m_Color" || path == "m_fontColor" || path == "m_Alpha") return PropGroup.Color;
            if (path == "m_text") return PropGroup.TextContent;
            if (obj is TMPro.TMP_Text) return PropGroup.TextStyle;
            if (path == "m_Sprite" || path == "m_Texture" || path == "m_Type" || path.StartsWith("m_Fill")) return PropGroup.Sprite;
            return PropGroup.Component;
        }

        static IEnumerable<SerializedProperty> Leaves(SerializedProperty property)
        {
            var p = property.Copy();
            if (p.propertyType != SerializedPropertyType.Generic) { yield return p; yield break; }
            int depth = p.depth;
            while (p.Next(true) && p.depth > depth)
                if (p.propertyType != SerializedPropertyType.Generic) yield return p.Copy();
        }

        static bool Supported(SerializedProperty property) => Leaves(property).All(p =>
            p.propertyType != SerializedPropertyType.ManagedReference && p.propertyType != SerializedPropertyType.Gradient &&
            p.propertyType != SerializedPropertyType.ExposedReference && p.propertyType != SerializedPropertyType.FixedBufferSize);

        static string Fingerprint(SerializedProperty property, string source)
        {
            var text = new StringBuilder();
            foreach (var p in Leaves(property))
            {
                string value;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.ObjectReference: value = Reference(p.objectReferenceValue, source); break;
                    case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.Character: case SerializedPropertyType.LayerMask: value = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: value = p.boolValue ? "1" : "0"; break;
                    case SerializedPropertyType.Float: value = p.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.String: value = p.stringValue ?? ""; break;
                    case SerializedPropertyType.AnimationCurve: value = JsonUtility.ToJson(new CurveValue { curve = p.animationCurveValue }); break;
                    default: value = JsonUtility.ToJson(p.boxedValue); break;
                }
                text.Append(p.propertyPath).Append(':').Append(value.Length).Append(':').Append(value).Append(';');
            }
            return text.ToString();
        }

        [Serializable] sealed class CurveValue { public AnimationCurve curve; }

        static string Reference(Object obj, string source)
        {
            if (obj == null) return "null";
            GameObject go = obj as GameObject ?? (obj as Component)?.gameObject;
            var id = go != null ? go.GetComponent<PsdGeneratedNode>() : null;
            if (id != null && id.sourceGuid == source)
            {
                if (obj is GameObject) return id.key + "|GameObject";
                foreach (var pair in Components(go)) if (pair.Value == obj) return id.key + "|" + pair.Key;
                throw new InvalidOperationException("引用了无法映射的生成组件：" + obj.name);
            }
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long local)) return guid + ":" + local;
            return "external:" + GlobalObjectId.GetGlobalObjectIdSlow(obj);
        }

        static void Remap(SerializedProperty p, Dictionary<string, GameObject> current, string source)
        {
            foreach (var leaf in Leaves(p))
            {
                if (leaf.propertyType != SerializedPropertyType.ObjectReference || leaf.objectReferenceValue == null) continue;
                var obj = leaf.objectReferenceValue;
                GameObject go = obj as GameObject ?? (obj as Component)?.gameObject;
                var id = go != null ? go.GetComponent<PsdGeneratedNode>() : null;
                if (id == null || id.sourceGuid != source) continue;
                if (!current.TryGetValue(id.key, out var target)) { leaf.objectReferenceValue = null; continue; }
                if (obj is GameObject) leaf.objectReferenceValue = target;
                else
                    foreach (var c in Components(go))
                        if (c.Value == obj) { leaf.objectReferenceValue = FindComponent(target, c.Key); break; }
            }
        }
    }
}
