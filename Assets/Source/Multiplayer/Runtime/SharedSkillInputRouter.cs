using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local input adapter. UI clicks and number keys enter the exact same
    /// request path; no skill rules live here.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Progression/Shared Skill Input Router")]
    public sealed class SharedSkillInputRouter : MonoBehaviour
    {
        [SerializeField] private bool enableNumberKeys = true;
        [SerializeField] private bool requireLockedCursorForKeys;

        public static SharedSkillInputRouter Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "Only one SharedSkillInputRouter may exist locally.",
                    this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!enableNumberKeys ||
                GameplayInputGate.IsBlocked ||
                NetworkItemCarrier.Local == null)
            {
                return;
            }

            if (requireLockedCursorForKeys &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame ||
                keyboard.numpad1Key.wasPressedThisFrame)
            {
                UseSkillSlot(0);
            }
            else if (keyboard.digit2Key.wasPressedThisFrame ||
                     keyboard.numpad2Key.wasPressedThisFrame)
            {
                UseSkillSlot(1);
            }
            else if (keyboard.digit3Key.wasPressedThisFrame ||
                     keyboard.numpad3Key.wasPressedThisFrame)
            {
                UseSkillSlot(2);
            }
        }

        public void UseSkillSlot(int slotIndex)
        {
            NetworkSharedSkillService service =
                NetworkSharedSkillService.Instance;

            if (service == null || !service.IsSpawned)
            {
                Debug.LogWarning(
                    "Shared skill service is not ready.",
                    this);
                return;
            }

            service.RequestUseSkillSlot(slotIndex);
        }

        public void UseSlot1() => UseSkillSlot(0);
        public void UseSlot2() => UseSkillSlot(1);
        public void UseSlot3() => UseSkillSlot(2);
    }
}
