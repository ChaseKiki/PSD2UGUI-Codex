using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PsdTool
{
    internal static class CleanPrefabExporter
    {
        internal static GameObject Export(GameObject source, string target, bool removeOptional, out string summary)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请选择已生成的 Prefab 资产。");
            target = (target ?? "").Replace('\\', '/');
            if (!target.StartsWith("Assets/", StringComparison.Ordinal) || !target.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                target.Split('/').Any(p => p == ".." || p == ".") || string.Equals(path, target, StringComparison.OrdinalIgnoreCase) || File.Exists(target))
                throw new InvalidOperationException("请在 Assets 内选择新的 .prefab 路径，不覆盖原稿或已有文件。");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var all = root.GetComponentsInChildren<Component>(true).Where(c => c != null).ToArray();
                var removable = new HashSet<Component>(all.Where(c => c is PsdGeneratedNode || removeOptional && (c is ButtonPressScale || c is UIGray)));
                int requested = removable.Count;
                // Preserve nested prefab instances and anything targeted by a retained
                // component/event. Iterate to preserve transitive references as well.
                removable.RemoveWhere(c => PrefabUtility.IsPartOfPrefabInstance(c));
                bool changed;
                do
                {
                    changed = false;
                    foreach (var component in all.Where(c => !removable.Contains(c)))
                    {
                        using (var serialized = new SerializedObject(component))
                        {
                            var property = serialized.GetIterator();
                            while (property.Next(true))
                                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Component referenced && removable.Remove(referenced)) changed = true;
                        }
                    }
                } while (changed);
                int count = removable.Count;
                foreach (var component in removable) Object.DestroyImmediate(component);
                PathUtil.EnsureFolder(Path.GetDirectoryName(target).Replace('\\', '/'));
                var result = PrefabUtility.SaveAsPrefabAsset(root, target, out bool success);
                if (!success || result == null) throw new IOException("干净副本保存失败；原 Prefab 未修改。");
                summary = $"已另存干净副本，移除 {count} 个辅助组件；{requested - count} 个因事件/引用或嵌套关系保留。必要的热区、Toggle 视觉、安全区与 FX 边界组件保留。";
                return result;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
