using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Small vertical-slice interactable used to verify host, client and late-join
    /// state replication. Replace it with game-specific derived interactables.
    /// </summary>
    [AddComponentMenu("Multiplayer/Interaction/Network Toggle Interactable")]
    public sealed class NetworkToggleInteractable : NetworkInteractable
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly NetworkVariable<bool> _isOn =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "Test target: every valid interaction toggles one server-owned bool. " +
            "Color/child state is rebuilt from that NetworkVariable, including " +
            "for late joiners.")]
        [TitleGroup("Toggle State")]
        [SerializeField] private bool startOn;

        [TitleGroup("Toggle State")]
        [SerializeField] private string turnOnPrompt = "Turn On";

        [TitleGroup("Toggle State")]
        [SerializeField] private string turnOffPrompt = "Turn Off";

        [TitleGroup("Visuals")]
        [Required]
        [SerializeField] private Renderer targetRenderer;

        [TitleGroup("Visuals")]
        [SerializeField] private Color offColor =
            new Color(0.18f, 0.2f, 0.24f, 1f);

        [TitleGroup("Visuals")]
        [SerializeField] private Color onColor =
            new Color(0.15f, 0.85f, 0.35f, 1f);

        [TitleGroup("Visuals")]
        [Tooltip("Optional child enabled only while the replicated state is on.")]
        [SerializeField] private GameObject activeWhileOn;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Toggle")]
        [LabelText("Replicated On")]
        public bool IsOn => IsSpawned ? _isOn.Value : startOn;

        private MaterialPropertyBlock _propertyBlock;

        private bool CanForceToggle =>
            Application.isPlaying && IsSpawned && IsServer;

        protected override void Reset()
        {
            base.Reset();
            AutoAssignReferences();
        }

        protected override void Awake()
        {
            base.Awake();
            AutoAssignReferences();
            ApplyVisual(startOn);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            AutoAssignReferences();

            if (!Application.isPlaying)
            {
                ApplyVisual(startOn);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _isOn.OnValueChanged += HandleStateChanged;

            if (IsServer)
            {
                _isOn.Value = startOn;
            }

            ApplyVisual(_isOn.Value);
        }

        public override void OnNetworkDespawn()
        {
            _isOn.OnValueChanged -= HandleStateChanged;
            base.OnNetworkDespawn();
        }

        public override string GetInteractionPrompt(
            NetworkInteractionController interactor)
        {
            string prompt = IsOn ? turnOffPrompt : turnOnPrompt;
            return string.IsNullOrWhiteSpace(prompt)
                ? base.GetInteractionPrompt(interactor)
                : prompt;
        }

        protected override bool ExecuteInteractionServer(
            NetworkInteractionContext context,
            out string resultMessage)
        {
            _isOn.Value = !_isOn.Value;
            resultMessage = _isOn.Value ? "Toggle turned on." : "Toggle turned off.";
            return true;
        }

        [Button("SERVER: TOGGLE NOW")]
        [EnableIf(nameof(CanForceToggle))]
        private void ForceToggleFromInspector()
        {
            if (!CanForceToggle)
            {
                return;
            }

            _isOn.Value = !_isOn.Value;
        }

        [Button("AUTO ASSIGN RENDERER")]
        private void AutoAssignReferences()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>(true);
            }
        }

        private void HandleStateChanged(bool previous, bool current)
        {
            ApplyVisual(current);
        }

        private void ApplyVisual(bool isOn)
        {
            if (activeWhileOn != null && activeWhileOn.activeSelf != isOn)
            {
                activeWhileOn.SetActive(isOn);
            }

            if (targetRenderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            targetRenderer.GetPropertyBlock(_propertyBlock);
            Color selectedColor = isOn ? onColor : offColor;
            Material sharedMaterial = targetRenderer.sharedMaterial;

            if (sharedMaterial == null ||
                sharedMaterial.HasProperty(BaseColorId))
            {
                _propertyBlock.SetColor(BaseColorId, selectedColor);
            }

            if (sharedMaterial == null || sharedMaterial.HasProperty(ColorId))
            {
                _propertyBlock.SetColor(ColorId, selectedColor);
            }

            targetRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
