using System.Collections.Generic;
using UnityEditor;

namespace PsdTool
{
    /// <summary>UI 目录里的图片变化时增量更新哈希索引。导入过程中不写资源，延后到下一帧。</summary>
    public sealed class SpriteIndexPostprocessor : AssetPostprocessor
    {
        static readonly List<string> Pending = new List<string>();
        static bool _scheduled;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var settings = PsdToolSettings.TryGet();
            if (settings == null) return;

            for (int i = 0; i < moved.Length; i++)
            {
                Pending.Add(moved[i]);
                if (i < movedFrom.Length) Pending.Add("-" + movedFrom[i]);
            }
            foreach (var path in imported) Pending.Add(path);
            foreach (var path in deleted) Pending.Add("-" + path);
            if (Pending.Count == 0 || _scheduled) return;

            _scheduled = true;
            EditorApplication.delayCall += Flush;
        }

        static void Flush()
        {
            _scheduled = false;
            var batch = new List<string>(Pending);
            Pending.Clear();

            var settings = PsdToolSettings.TryGet();
            if (settings == null || batch.Count == 0) return;
            var index = SpriteIndex.GetOrCreate(settings);
            bool changed = false;
            foreach (var item in batch)
            {
                if (item.StartsWith("-")) changed |= index.Remove(item.Substring(1));
                else if (SpriteIndex.IsIndexedTexture(item)) changed |= index.Refresh(settings, item);
            }
            if (changed) AssetDatabase.SaveAssets();
        }
    }
}
