using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal sealed class DataMove { public string from, to, guid; }
    internal sealed class DataMigrationPlan
    {
        public string editorRoot, materials, oldRoot, oldMaterials, oldRecycle;
        public readonly List<DataMove> moves = new List<DataMove>();
    }
    internal static class DataLayoutMigration
    {
        internal static void ValidatePath(string path, bool editor)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains('\\') ||
                path.Split('/').Any(p => p == "." || p == ".." || p.Length == 0) || Path.IsPathRooted(path) || path.Contains('{'))
                throw new InvalidOperationException("请使用 Assets 内不含占位符的规范目录：" + path);
            bool hasEditor = path.Split('/').Contains("Editor");
            if (editor != hasEditor || path.Split('/').Any(p => p == "Resources" || p == "StreamingAssets"))
                throw new InvalidOperationException(editor ? "编辑数据必须位于 Editor 内，不能位于 Resources/StreamingAssets。" : "运行材质不能放入 Editor/Resources/StreamingAssets。");
        }
        internal static DataMigrationPlan Preview(PsdToolSettings settings, string editorRoot, string materials)
        {
            ValidatePath(editorRoot, true); ValidatePath(materials, false);
            var plan = new DataMigrationPlan { editorRoot = editorRoot, materials = materials, oldRoot = settings.toolDataRoot,
                oldMaterials = settings.materialPresetFolder, oldRecycle = settings.resourceRecycleFolder };
            void Add(string from, string to)
            {
                if (from == to || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(from))) return;
                if (to.StartsWith(from + "/", StringComparison.Ordinal) || from.StartsWith(to + "/", StringComparison.Ordinal)) throw new InvalidOperationException("迁移目标不能与源目录互相嵌套。");
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(to))) throw new InvalidOperationException("迁移目标已存在，禁止覆盖或合并：" + to);
                plan.moves.Add(new DataMove { from = from, to = to, guid = AssetDatabase.AssetPathToGUID(from) });
            }
            Add(settings.materialPresetFolder, materials);
            foreach (string folder in new[] { "Trees", "ImportState", "Index" }) Add(settings.toolDataRoot + "/" + folder, editorRoot + "/" + folder);
            Add(settings.toolDataRoot + "/Resources/ResourceCatalog.asset", editorRoot + "/ResourceState/ResourceCatalog.asset");
            Add(settings.toolDataRoot + "/ResourceState/ResourceCatalog.asset", editorRoot + "/ResourceState/ResourceCatalog.asset");
            Add(settings.toolDataRoot + "/Previews", editorRoot + "/Cache/Previews");
            Add(settings.toolDataRoot + "/Cache/Previews", editorRoot + "/Cache/Previews");
            Add(settings.resourceRecycleFolder, editorRoot + "/ResourceRecycle");
            if (plan.moves.GroupBy(m => m.to).Any(g => g.Count() > 1)) throw new InvalidOperationException("旧/新数据目录同时存在，请先确认使用哪一份，禁止覆盖合并。");
            return plan;
        }
        internal static void Apply(PsdToolSettings settings, DataMigrationPlan plan)
        {
            if (settings.toolDataRoot != plan.oldRoot || settings.materialPresetFolder != plan.oldMaterials || settings.resourceRecycleFolder != plan.oldRecycle)
                throw new InvalidOperationException("配置在预览后已变化，请重新生成迁移清单。");
            foreach (var move in plan.moves)
                if (AssetDatabase.AssetPathToGUID(move.from) != move.guid || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(move.to)))
                    throw new InvalidOperationException("源或目标在预览后变化，未迁移：" + move.from);
            var previews = AssetDatabase.FindAssets("t:PsdTree").Select(g => AssetDatabase.LoadAssetAtPath<PsdTree>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).ToDictionary(t => t, t => t.previewPath);
            var moved = new List<DataMove>();
            try
            {
                foreach (var move in plan.moves)
                {
                    PathUtil.EnsureFolder(Path.GetDirectoryName(move.to).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(move.from, move.to);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                    moved.Add(move);
                    if (AssetDatabase.AssetPathToGUID(move.to) != move.guid) throw new InvalidOperationException("迁移 GUID 检查失败。");
                }
                foreach (var pair in previews)
                    foreach (var move in moved)
                        if (!string.IsNullOrEmpty(pair.Value) && (pair.Value == move.from || pair.Value.StartsWith(move.from + "/", StringComparison.Ordinal)))
                        { pair.Key.previewPath = move.to + pair.Value.Substring(move.from.Length); EditorUtility.SetDirty(pair.Key); break; }
                settings.toolDataRoot = plan.editorRoot; settings.materialPresetFolder = plan.materials; settings.resourceRecycleFolder = plan.editorRoot + "/ResourceRecycle";
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            }
            catch (Exception failure)
            {
                var errors = new List<string>();
                foreach (var move in moved.AsEnumerable().Reverse())
                { string error = AssetDatabase.MoveAsset(move.to, move.from); if (!string.IsNullOrEmpty(error)) errors.Add(error); }
                foreach (var pair in previews) { pair.Key.previewPath = pair.Value; EditorUtility.SetDirty(pair.Key); }
                settings.toolDataRoot = plan.oldRoot; settings.materialPresetFolder = plan.oldMaterials; settings.resourceRecycleFolder = plan.oldRecycle;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
                throw new InvalidOperationException("迁移失败，已尝试回退。" + failure.Message + (errors.Count == 0 ? "" : " 回退异常：" + string.Join("；", errors)), failure);
            }
        }
    }
}
