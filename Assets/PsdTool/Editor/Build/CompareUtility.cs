using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PsdTool
{
    /// <summary>在当前场景中放一份生成结果，并把 PSD 合成图半透明叠在上面。</summary>
    public static class CompareUtility
    {
        public const string RootName = "__PsdCompare";

        public static void ShowInScene(PsdTree tree, PsdToolSettings settings)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(tree.prefabGuid));
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("无法对比", "还没有生成 prefab。", "确定");
                return;
            }

            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var canvasGo = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.tag = "EditorOnly";
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = settings.designResolution;
            scaler.matchWidthOrHeight = settings.canvasMatch;

            PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);

            var preview = string.IsNullOrEmpty(tree.previewPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(tree.previewPath);
            if (preview != null)
            {
                var overlay = new GameObject("__PsdOverlay", typeof(RectTransform), typeof(RawImage));
                overlay.transform.SetParent(canvasGo.transform, false);
                var rt = (RectTransform)overlay.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = tree.docSize;
                var raw = overlay.GetComponent<RawImage>();
                raw.texture = preview;
                raw.color = new Color(1f, 1f, 1f, 0.5f);
                raw.raycastTarget = false;
                Selection.activeGameObject = overlay;
            }
            else
            {
                Selection.activeGameObject = canvasGo;
            }

            Undo.RegisterCreatedObjectUndo(canvasGo, "PSD 对比");
        }
    }
}
