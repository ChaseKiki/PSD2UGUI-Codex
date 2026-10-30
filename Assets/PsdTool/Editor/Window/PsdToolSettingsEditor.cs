using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    [CustomEditor(typeof(PsdToolSettings))]
    public sealed class PsdToolSettingsEditor : UnityEditor.Editor
    {
        bool output = true, fonts = true, controls, textures, storage, recognition, photoshop;
        public override void OnInspectorGUI()
        {
            PsdEditorUI.Header("PSD 工具 · 项目配置", "通常只需设置输出目录与字体。识别客户端的安装、登录在 Codex 识别窗口完成。");
            serializedObject.Update();
            Group(ref output, "输出目录与设计尺寸", "panelSprites", "panelTextures", "panelPrefabs", "panelAtlas", "moduleSprites", "moduleAtlas", "commonSprites", "commonTextures", "commonAtlas", "designResolution", "canvasMatch");
            Group(ref fonts, "字体与文字材质", "defaultFont", "fontMappings", "materialPresetFolder");
            Group(ref recognition, "识别选项", "aiImageMaxSide", "aiAllowStructure", "aiAutoApplyMin");
            Group(ref controls, "控件与适配", "minHitSize", "addButtonPressScale", "buttonPressScale", "buttonPressDuration", "addButtonGray", "anchorSnap", "ruleAutoApplyMin");
            Group(ref textures, "贴图与图集", "largeImageThreshold", "textureMaxSize", "textureFilter", "textureCompression", "texturePlatforms", "atlasMaxSize", "atlasPadding", "atlasIncludeInBuild");
            Group(ref storage, "编辑数据与回收目录", "toolDataRoot", "resourceRecycleFolder");
            Group(ref photoshop, "Photoshop 与图片效果", "bakeImageEffects", "photoshopTimeoutSeconds");
            if (photoshop)
            {
                EditorGUILayout.LabelField("Photoshop（仅本机）", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(PhotoshopFxBaker.ResolveExecutable(), GUILayout.Height(18));
                if (PsdEditorUI.Action("选择 Photoshop.exe"))
                {
                    string executable = EditorUtility.OpenFilePanel("选择 Photoshop.exe", "", "exe");
                    if (!string.IsNullOrEmpty(executable)) EditorPrefs.SetString(PhotoshopFxBaker.ExecutablePref, executable);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
        void Group(ref bool open, string heading, params string[] names)
        {
            using (new EditorGUILayout.VerticalScope(PsdEditorUI.Card))
            {
                open = EditorGUILayout.Foldout(open, heading, true);
                if (!open) return;
                foreach (string name in names)
                {
                    var property = serializedObject.FindProperty(name);
                    if (property != null) EditorGUILayout.PropertyField(property, true);
                }
            }
        }
    }
}
