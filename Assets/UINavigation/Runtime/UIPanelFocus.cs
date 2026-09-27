using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EXW.UI.Navigation
{
    /// <summary>Observes a panel owned by the game's UI code. Never changes its visibility.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EXW/UI Navigation/Panel Focus")]
    public sealed class UIPanelFocus : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private UIFocusCoordinator coordinator;
        [SerializeField] private Selectable defaultSelection;
        [SerializeField] private Button backButton;

        [Header("Asset")]
        [SerializeField] private UINavigationPanelProfileSO profile;

        [Header("Back Callback (InvokeEvent / legacy Close)")]
        [SerializeField] private UnityEvent onBackRequested = new UnityEvent();

        private readonly List<Selectable> _candidates = new List<Selectable>();
        private readonly List<TMP_Dropdown> _dropdowns = new List<TMP_Dropdown>();
        private Selectable _lastSelection;
        private string _lastKey;
        private int _lastIndex = -1;
        private bool _visible;
        private bool _ready;

        public UIFocusCoordinator Coordinator => coordinator;
        public UINavigationPanelProfileSO Profile => profile;
        public bool IsVisible => _visible && isActiveAndEnabled;
        public bool IsReady => IsVisible && _ready;
        public bool BlocksGameplay => !profile || profile.BlocksGameplay;

        private void OnEnable()
        {
            ResolveCoordinator();
            CollectCandidates();
            _ready = !profile || !profile.WaitForReadySignal;
            _visible = !profile || profile.FocusOnEnable;
            if (_visible && coordinator) coordinator.Register(this);
        }

        private void OnDisable()
        {
            _visible = false;
            if (coordinator) coordinator.Unregister(this);
        }

        /// <summary>Assign the scene's owner when a prefab is generated outside its hierarchy.</summary>
        public void Bind(UIFocusCoordinator owner)
        {
            if (coordinator == owner) return;
            if (coordinator) coordinator.Unregister(this);
            coordinator = owner;
            if (IsVisible && coordinator) coordinator.Register(this);
        }

        /// <summary>Signal an externally animated panel that stays active while hidden.</summary>
        public void NotifyShown()
        {
            if (!isActiveAndEnabled) return;
            ResolveCoordinator();
            if (!_visible)
            {
                CollectCandidates();
                _ready = !profile || !profile.WaitForReadySignal;
            }
            _visible = true;
            if (coordinator) coordinator.Register(this);
        }

        /// <summary>Signal an externally animated panel that stays active while hidden.</summary>
        public void NotifyHidden()
        {
            _visible = false;
            if (coordinator) coordinator.Unregister(this);
        }

        /// <summary>Signal that the existing controller has completed its opening animation.</summary>
        public void NotifyReady()
        {
            _ready = true;
            if (coordinator) coordinator.OnPanelReady(this);
        }

        /// <summary>Call after generating, removing, or reordering Selectables at runtime.</summary>
        public void Refresh()
        {
            CollectCandidates();
            if (coordinator) coordinator.OnPanelRefreshed(this);
        }

        public void Refresh(Selectable preferred)
        {
            CollectCandidates();
            if (coordinator) coordinator.OnPanelRefreshed(this, preferred);
        }

        public void SetDefaultSelection(Selectable selectable)
        {
            defaultSelection = selectable;
            Refresh();
        }

        [ContextMenu("Validate Panel Focus")]
        public void ValidateSetup()
        {
            ResolveCoordinator();
            if (!coordinator)
                Debug.LogError("[UI Navigation] Assign the scene's coordinator.", this);
            if (!defaultSelection)
                Debug.LogWarning("[UI Navigation] No default selection; the first usable Selectable will be used.", this);
            else if (defaultSelection.GetComponentInParent<UIPanelFocus>(true) != this)
                Debug.LogError("[UI Navigation] Default Selection must belong to this panel.", this);
            if (profile && profile.BackMode == UIPanelBackMode.ClickBackButton && !backButton)
                Debug.LogError("[UI Navigation] ClickBackButton requires a Back Button reference.", this);
        }

        internal bool Contains(Selectable selectable) => selectable && _candidates.Contains(selectable);

        internal bool IsUsable(Selectable selectable)
        {
            return Contains(selectable) && selectable.gameObject.activeInHierarchy &&
                   selectable.IsActive() && selectable.IsInteractable();
        }

        internal void Remember(Selectable selectable)
        {
            // OnDisable can arrive after the hierarchy is already inactive.
            if (!Contains(selectable)) return;
            _lastSelection = selectable;
            _lastIndex = _candidates.IndexOf(selectable);
            UIFocusKey key = selectable.GetComponentInParent<UIFocusKey>(true);
            _lastKey = key ? key.Key : null;
        }

        internal Selectable ResolveSelection(Selectable preferred = null)
        {
            if (IsUsable(preferred)) return preferred;

            if (!profile || profile.RememberLastSelection)
            {
                if (IsUsable(_lastSelection)) return _lastSelection;
                if (!string.IsNullOrEmpty(_lastKey))
                {
                    foreach (Selectable candidate in _candidates)
                    {
                        if (!IsUsable(candidate)) continue;
                        UIFocusKey key = candidate.GetComponentInParent<UIFocusKey>(true);
                        if (key && key.Key == _lastKey) return candidate;
                    }
                }
                if (_lastIndex >= 0 && _candidates.Count > 0)
                {
                    int index = Mathf.Min(_lastIndex, _candidates.Count - 1);
                    for (int distance = 0; distance < _candidates.Count; distance++)
                    {
                        int forward = index + distance;
                        int backward = index - distance;
                        if (forward < _candidates.Count && IsUsable(_candidates[forward]))
                            return _candidates[forward];
                        if (backward >= 0 && IsUsable(_candidates[backward]))
                            return _candidates[backward];
                    }
                }
            }

            if (IsUsable(defaultSelection)) return defaultSelection;
            foreach (Selectable candidate in _candidates)
                if (IsUsable(candidate)) return candidate;
            return null;
        }

        internal bool HasExpandedDropdown()
        {
            foreach (TMP_Dropdown dropdown in _dropdowns)
                if (dropdown && dropdown.isActiveAndEnabled && dropdown.IsExpanded) return true;
            return false;
        }

        internal bool TryCloseExpandedDropdown()
        {
            foreach (TMP_Dropdown dropdown in _dropdowns)
            {
                if (!dropdown || !dropdown.isActiveAndEnabled || !dropdown.IsExpanded) continue;
                dropdown.Hide();
                return true;
            }
            return false;
        }

        internal bool RequestBack()
        {
            UIPanelBackMode mode = profile ? profile.BackMode : UIPanelBackMode.Ignore;
            switch (mode)
            {
                case UIPanelBackMode.Close: // Serialized legacy profile: delegate closure to game code.
                    if (backButton && backButton.IsActive() && backButton.IsInteractable())
                    {
                        backButton.onClick.Invoke();
                        return true;
                    }
                    onBackRequested.Invoke();
                    return true;
                case UIPanelBackMode.ClickBackButton:
                    if (!backButton || !backButton.IsActive() || !backButton.IsInteractable()) return false;
                    backButton.onClick.Invoke();
                    return true;
                case UIPanelBackMode.InvokeEvent:
                    onBackRequested.Invoke();
                    return true;
                default:
                    return false;
            }
        }

        private void CollectCandidates()
        {
            _candidates.Clear();
            _dropdowns.Clear();
            foreach (Selectable selectable in GetComponentsInChildren<Selectable>(true))
            {
                if (selectable.GetComponentInParent<UIPanelFocus>(true) != this) continue;
                _candidates.Add(selectable);
                if (selectable is TMP_Dropdown dropdown) _dropdowns.Add(dropdown);
            }
        }

        private void ResolveCoordinator()
        {
            if (!coordinator) coordinator = GetComponentInParent<UIFocusCoordinator>(true);
        }

#if UNITY_EDITOR
        private void OnValidate() => ResolveCoordinator();
#endif
    }
}
