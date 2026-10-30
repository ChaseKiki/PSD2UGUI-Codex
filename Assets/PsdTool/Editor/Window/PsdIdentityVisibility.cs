using UnityEditor;
using UnityEngine;

namespace PsdTool
{
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
