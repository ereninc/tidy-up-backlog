using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Optional local presenter for the three-button skill bar. It reads the
    /// shared server timestamp; it never runs its own authoritative cooldown.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Progression/UI/Shared Skill Bar Presenter")]
    public sealed class SharedSkillBarPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class SlotBinding
        {
            public Button Button;
            public Image CooldownFilled;
            public TMP_Text SkillIndex;
        }

        [SerializeField] private SharedSkillInputRouter inputRouter;
        [SerializeField] private SlotBinding[] slots =
            Array.Empty<SlotBinding>();
        [SerializeField] private bool fullMeansReady;

        private UnityAction[] _clickActions = Array.Empty<UnityAction>();
        private NetworkSharedSkillService _service;

        private void Awake()
        {
            if (inputRouter == null)
            {
                inputRouter = GetComponent<SharedSkillInputRouter>();
            }

            ConfigureButtons();
        }

        private void OnEnable()
        {
            BindService();
        }

        private void Update()
        {
            if (_service != NetworkSharedSkillService.Instance)
            {
                BindService();
            }

            RefreshCooldowns();
        }

        private void OnDestroy()
        {
            RemoveButtonListeners();
            UnbindService();
        }

        private void ConfigureButtons()
        {
            RemoveButtonListeners();
            _clickActions = new UnityAction[slots?.Length ?? 0];

            for (int i = 0; i < _clickActions.Length; i++)
            {
                SlotBinding slot = slots[i];
                int capturedIndex = i;

                if (slot?.SkillIndex != null)
                {
                    slot.SkillIndex.text = (i + 1).ToString();
                }

                if (slot?.CooldownFilled != null)
                {
                    slot.CooldownFilled.raycastTarget = false;
                }

                if (slot?.Button == null)
                {
                    continue;
                }

                _clickActions[i] = () => UseSlot(capturedIndex);
                slot.Button.onClick.AddListener(_clickActions[i]);
            }
        }

        private void RemoveButtonListeners()
        {
            if (slots == null || _clickActions == null)
            {
                return;
            }

            int count = Mathf.Min(slots.Length, _clickActions.Length);

            for (int i = 0; i < count; i++)
            {
                if (slots[i]?.Button != null &&
                    _clickActions[i] != null)
                {
                    slots[i].Button.onClick.RemoveListener(
                        _clickActions[i]);
                }
            }
        }

        private void UseSlot(int slotIndex)
        {
            if (inputRouter != null)
            {
                inputRouter.UseSkillSlot(slotIndex);
                return;
            }

            NetworkSharedSkillService.Instance?.RequestUseSkillSlot(slotIndex);
        }

        private void BindService()
        {
            UnbindService();
            _service = NetworkSharedSkillService.Instance;

            if (_service != null)
            {
                _service.SkillsChanged += RefreshCooldowns;
            }

            RefreshCooldowns();
        }

        private void UnbindService()
        {
            if (_service != null)
            {
                _service.SkillsChanged -= RefreshCooldowns;
                _service = null;
            }
        }

        private void RefreshCooldowns()
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                SlotBinding slot = slots[i];
                SharedSkillDefinition definition = null;
                SharedSkillRuntimeState state = default;

                bool hasDefinition =
                    _service != null &&
                    _service.Catalog != null &&
                    _service.Catalog.TryGetSkillAtSlot(i, out definition);

                bool hasState = hasDefinition &&
                    _service.TryGetRuntimeState(
                        definition.SkillId,
                        out state);

                float remaining = hasState
                    ? _service.GetRemainingCooldownSeconds(
                        definition.SkillId)
                    : 0f;

                if (slot?.Button != null)
                {
                    slot.Button.interactable =
                        hasState && state.IsUnlocked && remaining <= 0f;
                }

                if (slot?.CooldownFilled == null)
                {
                    continue;
                }

                float normalizedReady = 0f;

                if (hasState)
                {
                    float duration = Mathf.Max(
                        0.0001f,
                        definition.CooldownSeconds);
                    normalizedReady = 1f - Mathf.Clamp01(
                        remaining / duration);
                }

                slot.CooldownFilled.fillAmount = fullMeansReady
                    ? normalizedReady
                    : 1f - normalizedReady;
            }
        }
    }
}
