using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace PsdTool
{
    internal sealed class PsdBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (string type in new[] { "ResourceCatalog", "PsdTree", "PsdImportState", "SpriteIndex", "PsdToolSettings" })
                foreach (string guid in AssetDatabase.FindAssets("t:" + type))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Split('/').Any(p => p == "Editor")) continue;
                    if (path.Split('/').Any(p => p == "Resources" || p == "StreamingAssets"))
                        throw new BuildFailedException("PSD 编辑数据进入构建目录：" + path + "。请在 PSD 资源管理中预览并确认数据隔离迁移。");
                }
        }
    }
}
