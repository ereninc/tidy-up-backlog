#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    /// <summary>
    /// NGO supplies its own NetworkBehaviour inspector, which otherwise hides
    /// Odin's non-serialized ShowInInspector properties. This explicit editor
    /// lets Odin draw the input reader's runtime diagnostics.
    /// </summary>
    [CustomEditor(typeof(EXW.Multiplayer.NetworkPlayerInputReader))]
    [CanEditMultipleObjects]
    public sealed class NetworkPlayerInputReaderEditor : OdinEditor
    {
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }
    }
}
#endif
