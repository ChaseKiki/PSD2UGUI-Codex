using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PsdTool
{
    internal static class PathUtil
    {
        public static void EnsureFolder(string assetPath)
        {
            assetPath = assetPath.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string name = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        public static string ToFullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
                sb.Append(System.Array.IndexOf(invalid, c) >= 0 || c == '@' || c == ' ' ? '_' : c);
            return sb.ToString();
        }
    }
}
