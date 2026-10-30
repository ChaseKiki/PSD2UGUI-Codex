using UnityEngine;

namespace PsdTool
{
    /// <summary>编辑器增量导入的持久标识；运行时无逻辑。不要复制或手改标识。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class PsdGeneratedNode : MonoBehaviour
    {
        [HideInInspector] public string sourceGuid;
        [HideInInspector] public string key;
    }
}
