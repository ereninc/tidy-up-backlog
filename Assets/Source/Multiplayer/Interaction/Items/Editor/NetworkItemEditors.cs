#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace EXW.Multiplayer.Editor
{
    [CustomEditor(typeof(NetworkWorldItem))]
    [CanEditMultipleObjects]
    public sealed class NetworkWorldItemEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemPresentation))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemPresentationEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkCarryable))]
    [CanEditMultipleObjects]
    public sealed class NetworkCarryableEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkHeldItemPoseFollower))]
    [CanEditMultipleObjects]
    public sealed class NetworkHeldItemPoseFollowerEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemInteractable))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemInteractableEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemCarrier))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemCarrierEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemCarrierDebugHud))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemCarrierDebugHudEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemReceiver))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemReceiverEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemDestination), true)]
    [CanEditMultipleObjects]
    public sealed class NetworkItemDestinationEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemDefinition))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemDefinitionEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemTagDefinition))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemTagDefinitionEditor : OdinEditor
    {
    }

    [CustomEditor(typeof(NetworkItemAcceptanceFilter))]
    [CanEditMultipleObjects]
    public sealed class NetworkItemAcceptanceFilterEditor : OdinEditor
    {
    }
}
#endif
