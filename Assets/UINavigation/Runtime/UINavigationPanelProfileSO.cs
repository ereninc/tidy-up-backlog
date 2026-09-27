using UnityEngine;
using UnityEngine.Serialization;

namespace EXW.UI.Navigation
{
    // Keep the serialized values of existing profile assets.
    public enum UIPanelBackMode
    {
        Ignore = 0,
        Close = 1, // Legacy value: requests a close through the button/event; never changes visibility.
        ClickBackButton = 2,
        InvokeEvent = 3
    }

    [CreateAssetMenu(menuName = "EXW/UI Navigation/Panel Profile", fileName = "UIPanelProfile")]
    public sealed class UINavigationPanelProfileSO : ScriptableObject
    {
        [Header("Focus")]
        [FormerlySerializedAs("autoOpenOnEnable")]
        [SerializeField] private bool focusOnEnable = true;
        [Tooltip("Call NotifyReady after an opening animation or async load has made the controls usable.")]
        [SerializeField] private bool waitForReadySignal;
        [SerializeField] private bool rememberLastSelection = true;

        [Header("Input")]
        [SerializeField] private bool blocksGameplay = true;
        [SerializeField] private UIPanelBackMode backMode = UIPanelBackMode.Ignore;

        public bool FocusOnEnable => focusOnEnable;
        public bool WaitForReadySignal => waitForReadySignal;
        public bool RememberLastSelection => rememberLastSelection;
        public bool BlocksGameplay => blocksGameplay;
        public UIPanelBackMode BackMode => backMode;
    }
}
