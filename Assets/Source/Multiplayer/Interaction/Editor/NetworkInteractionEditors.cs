#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace EXW.Multiplayer.Editor
{
    /// <summary>
    /// Explicit Odin editors are intentional: this project has shown that Odin's
    /// global MonoBehaviour editor mapping is not reliable under every Unity 6
    /// inspector configuration.
    /// </summary>
    [CustomEditor(typeof(NetworkInteractionController))]
    [CanEditMultipleObjects]
    public sealed class NetworkInteractionControllerEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkInteractable), true)]
    [CanEditMultipleObjects]
    public sealed class NetworkInteractableEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkInteractionDebugHud))]
    [CanEditMultipleObjects]
    public sealed class NetworkInteractionDebugHudEditor : OdinEditor
    {
    }
}
#endif
