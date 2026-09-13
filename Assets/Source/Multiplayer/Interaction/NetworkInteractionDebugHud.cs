using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Canvas-free build HUD for the interaction vertical slice. It is local-only
    /// and safe to remove when production UI subscribes to the controller events.
    /// </summary>
    [RequireComponent(typeof(NetworkInteractionController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Interaction/Network Interaction Debug HUD")]
    public sealed class NetworkInteractionDebugHud : MonoBehaviour
    {
        [InfoBox(
            "Build-safe temporary HUD. E/Gamepad South interacts; F8 toggles the " +
            "diagnostic panel. This component owns no network state.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField] private NetworkInteractionController controller;

        [TitleGroup("Display")]
        [SerializeField] private bool showCrosshair = true;

        [TitleGroup("Display")]
        [SerializeField] private bool showInteractionPrompt = true;

        [TitleGroup("Display")]
        [SerializeField] private bool showDiagnostics = true;

        [TitleGroup("Display")]
        [SerializeField] private Key diagnosticsToggleKey = Key.F8;

        [TitleGroup("Display")]
        [MinValue(260f)]
        [SerializeField] private float diagnosticsWidth = 420f;

        [TitleGroup("Display")]
        [SerializeField] private Color accentColor =
            new Color(0.25f, 0.9f, 1f, 1f);

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Visible For Local Owner")]
        private bool RuntimeVisible => CanDraw;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Focused")]
        private string RuntimeFocused => controller != null &&
                                         controller.CurrentTarget != null
            ? controller.CurrentTarget.name
            : "None";

        private GUIStyle _crosshairStyle;
        private GUIStyle _targetTitleStyle;
        private GUIStyle _promptStyle;
        private GUIStyle _diagnosticStyle;
        private GUIStyle _diagnosticBoxStyle;

        private bool CanDraw =>
            controller != null &&
            controller.IsSpawned &&
            controller.IsOwner;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void OnValidate()
        {
            AutoAssignReferences();
            diagnosticsWidth = Mathf.Max(260f, diagnosticsWidth);
        }

        private void Update()
        {
            if (!CanDraw || Keyboard.current == null)
            {
                return;
            }

            var toggleControl = Keyboard.current[diagnosticsToggleKey];

            if (toggleControl != null && toggleControl.wasPressedThisFrame)
            {
                showDiagnostics = !showDiagnostics;
            }
        }

        private void OnGUI()
        {
            if (!CanDraw)
            {
                return;
            }

            EnsureStyles();

            if (showCrosshair)
            {
                const float crosshairSize = 32f;
                GUI.Label(
                    new Rect(
                        (Screen.width - crosshairSize) * 0.5f,
                        (Screen.height - crosshairSize) * 0.5f,
                        crosshairSize,
                        crosshairSize),
                    "+",
                    _crosshairStyle);
            }

            if (showInteractionPrompt && controller.CurrentTarget != null)
            {
                Rect promptBox = new Rect(
                    Screen.width * 0.5f - 250f,
                    Screen.height * 0.5f + 28f,
                    500f,
                    70f);

                GUI.Box(promptBox, GUIContent.none, _diagnosticBoxStyle);

                GUI.Label(
                    new Rect(
                        promptBox.x + 8f,
                        promptBox.y + 5f,
                        promptBox.width - 16f,
                        28f),
                    controller.CurrentTargetDisplayName,
                    _targetTitleStyle);

                string prompt =
                    $"[{controller.InteractionKeyDisplayName}] " +
                    controller.CurrentPrompt;

                GUI.Label(
                    new Rect(
                        promptBox.x + 8f,
                        promptBox.y + 32f,
                        promptBox.width - 16f,
                        30f),
                    prompt,
                    _promptStyle);
            }

            if (!showDiagnostics)
            {
                return;
            }

            float safeWidth = Mathf.Min(
                diagnosticsWidth,
                Mathf.Max(260f, Screen.width - 32f));
            Rect boxRect = new Rect(
                16f,
                Screen.height - 150f,
                safeWidth,
                134f);

            GUI.Box(boxRect, GUIContent.none, _diagnosticBoxStyle);

            string targetText = controller.CurrentTarget != null
                ? $"{controller.CurrentTargetDisplayName} " +
                  $"({controller.CurrentTargetDistance:0.00} m)"
                : "None";

            string text =
                "NETWORK INTERACTION DEBUG\n" +
                $"Target: {targetText}\n" +
                $"Prompt: {controller.CurrentPrompt}\n" +
                $"Result: {controller.LastResultSummary}\n" +
                $"Input: {controller.InteractionKeyDisplayName} / Gamepad South" +
                $"   Hide: {diagnosticsToggleKey}";

            GUI.Label(
                new Rect(
                    boxRect.x + 12f,
                    boxRect.y + 8f,
                    boxRect.width - 24f,
                    boxRect.height - 16f),
                text,
                _diagnosticStyle);
        }

        [Button("AUTO ASSIGN REFERENCES")]
        public void AutoAssignReferences()
        {
            if (controller == null)
            {
                controller = GetComponent<NetworkInteractionController>();
            }
        }

        private void EnsureStyles()
        {
            if (_crosshairStyle != null)
            {
                return;
            }

            _crosshairStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 25,
                fontStyle = FontStyle.Bold
            };
            _crosshairStyle.normal.textColor = Color.white;

            _targetTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            _targetTitleStyle.normal.textColor = Color.white;

            _promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Normal
            };
            _promptStyle.normal.textColor = accentColor;

            _diagnosticStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 14,
                wordWrap = true
            };
            _diagnosticStyle.normal.textColor = Color.white;

            _diagnosticBoxStyle = new GUIStyle(GUI.skin.box);
        }
    }
}
