using System;
using System.Collections.Generic;
using UnityEngine;

namespace PsdTool
{
    [Serializable]
    public sealed class ImportedProperty
    {
        public string path;
        public string value;
        public PropGroup group;
    }

    [Serializable]
    public sealed class ImportedComponent
    {
        public string type;
        public bool removed;
        public List<ImportedProperty> properties = new List<ImportedProperty>();
    }

    [Serializable]
    public sealed class ImportedNode
    {
        public string key;
        public bool removed;
        public string parent;
        public List<ImportedComponent> components = new List<ImportedComponent>();
    }

    /// <summary>上次生成器的原始输出，和最终 Prefab 上保留的人工修改分开保存。</summary>
    public sealed class PsdImportState : ScriptableObject
    {
        public string sourceGuid;
        public string prefabGuid;
        public List<ImportedNode> nodes = new List<ImportedNode>();
    }
}
