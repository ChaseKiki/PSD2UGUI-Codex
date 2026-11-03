using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class LegacyButtonMigration
    {
        internal static bool Referenced(Component legacy, GameObject root, string path)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component == legacy) continue;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == legacy) return true;
                }
            }
            // 对外部 Prefab/场景/资产依赖保守处理；不打开或改写用户场景。
            if (string.IsNullOrEmpty(path)) path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(legacy);
            if (string.IsNullOrEmpty(path)) return false;
            return AssetDatabase.GetAllAssetPaths().Any(p => p != path && !AssetDatabase.IsValidFolder(p) &&
                (p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) &&
                AssetDatabase.GetDependencies(p, false).Contains(path));
        }
    }
}
