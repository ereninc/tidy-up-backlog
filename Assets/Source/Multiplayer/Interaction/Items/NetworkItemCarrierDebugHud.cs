using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Canvas-free temporary inventory HUD for editor and build smoke tests.
    /// Production UI can subscribe to NetworkItemCarrier.HeldItemChanged instead.
    /// </summary>
    [RequireComponent(typeof(NetworkItemCarrier))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Carrier Debug HUD")]
    public sealed class NetworkItemCarrierDebugHud : MonoBehaviour
    {
        [InfoBox(
            "Temporary build-safe HUD. It displays only for the local owning " +
            "player and contains no authoritative state.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField] private NetworkItemCarrier carrier;

        [TitleGroup("Display")]
        [SerializeField] private bool hideWhenHandsAreEmpty = true;

        [TitleGroup("Display")]
        [MinValue(240f)]
        [SerializeField] private float width = 340f;

        [TitleGroup("Display")]
        [SerializeField] private Color accentColor =
            new Color(0.35f, 0.95f, 0.6f, 1f);

        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _boxStyle;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void OnValidate()
        {
            AutoAssignReferences();
            width = Mathf.Max(240f, width);
        }

        private void OnGUI()
        {
            if (carrier == null || !carrier.IsSpawned || !carrier.IsOwner ||
                (hideWhenHandsAreEmpty && !carrier.HasHeldItem))
            {
                return;
            }

            EnsureStyles();

            float safeWidth = Mathf.Min(
                width,
                Mathf.Max(240f, Screen.width - 32f));
            Rect box = new Rect(
                Screen.width - safeWidth - 16f,
                16f,
                safeWidth,
                74f);

            GUI.Box(box, GUIContent.none, _boxStyle);
            GUI.Label(
                new Rect(box.x + 12f, box.y + 8f, box.width - 24f, 28f),
                $"HELD: {carrier.HeldItemName}",
                _titleStyle);
            GUI.Label(
                new Rect(box.x + 12f, box.y + 38f, box.width - 24f, 25f),
                $"[{carrier.DropKeyDisplayName}] Drop   [E] Place / Discard",
                _instructionStyle);
        }

        [Button("AUTO ASSIGN REFERENCES")]
        public void AutoAssignReferences()
        {
            if (carrier == null)
            {
                carrier = GetComponent<NetworkItemCarrier>();
            }
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null)
            {
                return;
            }

            _boxStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            _titleStyle.normal.textColor = Color.white;

            _instructionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14
            };
            _instructionStyle.normal.textColor = accentColor;
        }
    }
}
