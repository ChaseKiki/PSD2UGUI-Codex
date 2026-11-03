using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class PsdNodeSelection
    {
        internal static bool Resolve(GameObject selected, out PsdTree tree, out PsdNode node)
        {
            tree = null; node = null;
            for (var current = selected == null ? null : selected.transform; current != null; current = current.parent)
            {
                var marker = current.GetComponent<PsdGeneratedNode>();
                if (marker == null) continue;
                tree = AssetDatabase.LoadAssetAtPath<PsdTree>(AssetDatabase.GUIDToAssetPath(marker.sourceGuid));
                if (tree == null) return false;
                int length = -1;
                foreach (var candidate in tree.nodes)
                {
                    if (candidate.status == NodeStatus.PendingDelete) continue;
                    string key = string.IsNullOrEmpty(candidate.stableKey) ? "layer:" + candidate.layerId : candidate.stableKey;
                    if ((marker.key == key || (marker.key ?? "").StartsWith(key + "/", System.StringComparison.Ordinal)) && key.Length > length)
                    { node = candidate; length = key.Length; }
                }
                if (node != null) return true;
            }
            return false;
        }
    }
    // Inspector presentation only: Unity can reset component hideFlags on prefab
    // load. Do not mark the asset changed or force an incremental save for this.
    [InitializeOnLoad]
    internal static class PsdIdentityVisibility
    {
        static PsdIdentityVisibility()
        {
            Selection.selectionChanged += HideSelectedMarkers;
            EditorApplication.projectChanged += HideSelectedMarkers;
            EditorApplication.delayCall += HideSelectedMarkers;
        }
        internal static void HideSelectedMarkers()
        {
            foreach (var go in Selection.gameObjects)
                foreach (var marker in go.GetComponentsInChildren<PsdGeneratedNode>(true))
                    marker.hideFlags |= HideFlags.HideInInspector;
        }
    }
}
