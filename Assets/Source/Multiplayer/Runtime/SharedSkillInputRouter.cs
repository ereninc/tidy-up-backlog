using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("enableNumberKeys")]
        [SerializeField] private bool enableInputActions = true;

        [FormerlySerializedAs("requireLockedCursorForKeys")]
        [SerializeField] private bool requireLockedCursorForInput;

        public static SharedSkillInputRouter Instance { get; private set; }

        private NetworkPlayerInputReader _inputReader;

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

        private void OnEnable()
        {
            NetworkPlayerInputReader.LocalReaderChanged +=
                HandleLocalReaderChanged;

            BindInputReader(NetworkPlayerInputReader.Local);
        }

        private void OnDisable()
        {
            NetworkPlayerInputReader.LocalReaderChanged -=
                HandleLocalReaderChanged;

            BindInputReader(null);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void HandleLocalReaderChanged(
            NetworkPlayerInputReader reader)
        {
            BindInputReader(reader);
        }

        private void BindInputReader(NetworkPlayerInputReader reader)
        {
            if (_inputReader == reader)
            {
                return;
            }

            if (_inputReader != null)
            {
                _inputReader.SkillPressed -= HandleSkillPressed;
            }

            _inputReader = reader;

            if (_inputReader != null)
            {
                _inputReader.SkillPressed += HandleSkillPressed;
            }
        }

        private void HandleSkillPressed(int oneBasedSkillIndex)
        {
            if (!enableInputActions ||
                GameplayInputGate.IsBlocked ||
                NetworkItemCarrier.Local == null)
            {
                return;
            }

            if (requireLockedCursorForInput &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            int slotIndex = oneBasedSkillIndex - 1;

            if (slotIndex < 0)
            {
                return;
            }

            UseSkillSlot(slotIndex);
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
