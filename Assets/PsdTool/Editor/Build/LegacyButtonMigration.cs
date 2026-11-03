using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class LegacyButtonMigration
    {
        static readonly Dictionary<string, HashSet<long>> references = new Dictionary<string, HashSet<long>>();
        static readonly HashSet<string> uncertain = new HashSet<string>();
        internal static void BeginPass() { references.Clear(); uncertain.Clear(); }
        internal static bool Referenced(Component legacy, GameObject root, string path)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component == legacy) continue;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == legacy)
                        {
                            if (legacy is UnityEngine.UI.Image && component is UnityEngine.UI.Button && component.gameObject == legacy.gameObject && property.propertyPath == "m_TargetGraphic") continue;
                            return true;
                        }
                }
            }
            // 引用 Prefab 本身不代表引用每个组件；只保护外部指向这个组件的 FileID。
            // 只读序列化资源，不打开或改写用户场景。无法可靠读取时保守保留。
            if (string.IsNullOrEmpty(path)) path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(legacy);
            if (string.IsNullOrEmpty(path)) return false;
            string guid=AssetDatabase.AssetPathToGUID(path);
            if(!references.TryGetValue(path,out var ids))
            {
                ids=new HashSet<long>();references[path]=ids;
                foreach(string p in AssetDatabase.GetAllAssetPaths().Where(p=>p!=path&&!AssetDatabase.IsValidFolder(p)&&
                    (p.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".unity",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".asset",StringComparison.OrdinalIgnoreCase))))
                {
                    if(!AssetDatabase.GetDependencies(p,false).Contains(path))continue;
                    try
                    {
                        string full=PathUtil.ToFullPath(p);
                        if(new FileInfo(full).Length>64*1024*1024){uncertain.Add(path);continue;}
                        string yaml=File.ReadAllText(full);
                        if(!yaml.StartsWith("%YAML",StringComparison.Ordinal)){uncertain.Add(path);continue;}
                        foreach(Match match in Regex.Matches(yaml,@"\{\s*fileID:\s*(-?\d+),\s*guid:\s*"+Regex.Escape(guid)+@"\b[^}]*\}"))
                            if(long.TryParse(match.Groups[1].Value,out long id))ids.Add(id);
                    }
                    catch(IOException){uncertain.Add(path);}catch(UnauthorizedAccessException){uncertain.Add(path);}
                }
            }
            if(uncertain.Contains(path))return true;
            var stamp=legacy.GetComponent<PsdGeneratedNode>();
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var source=stamp==null||asset==null?null:asset.GetComponentsInChildren<PsdGeneratedNode>(true).FirstOrDefault(n=>n.sourceGuid==stamp.sourceGuid&&n.key==stamp.key);
            if(source==null)return ids.Count>0;
            var persistentComponent=source.GetComponent(legacy.GetType());
            if(persistentComponent==null)return false;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(persistentComponent,out string _,out long fileId)&&ids.Contains(fileId);
        }
    }
}
