using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local build-test panel. Shift+X toggles it. Buttons only raise local
    /// actions; NetworkSharedDevelopmentService performs server transactions.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Development/Shared Development IMGUI")]
    public sealed class SharedDevelopmentIMGUI : MonoBehaviour
    {
        [Header("Availability")]
        [Tooltip(
            "Leave disabled for release. Editor and Development Builds are " +
            "always allowed.")]
        [SerializeField] private bool allowInNonDevelopmentBuild;

        [Header("Toggle")]
        [SerializeField] private Key toggleKey = Key.X;
        [SerializeField] private bool unlockCursorWhileOpen = true;

        [Header("Resolution Scaling")]
        [SerializeField, Min(320f)] private float referenceWidth = 1920f;
        [SerializeField, Min(240f)] private float referenceHeight = 1080f;
        [SerializeField, Range(0.5f, 1f)] private float minimumScale = 0.75f;
        [SerializeField, Range(1f, 3f)] private float maximumScale = 2f;

        [Header("Panel")]
        [SerializeField] private Font overrideFont;
        [SerializeField] private Vector2 initialPosition =
            new Vector2(28f, 80f);
        [SerializeField] private Vector2 panelSize =
            new Vector2(500f, 665f);

        public static bool IsOpen { get; private set; }

        private Rect _windowRect;
        private bool _windowInitialized;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;

        private string _moneyAmountText = "100";
        private string _statusMessage = "Ready.";
        private bool _lastCommandSucceeded = true;

        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _statusStyle;

        private bool CommandsAvailable =>
            Application.isEditor ||
            Debug.isDebugBuild ||
            allowInNonDevelopmentBuild;

        private void OnEnable()
        {
            SharedDevelopmentActions.CommandResult += HandleCommandResult;
        }

        private void Update()
        {
            if (!CommandsAvailable)
            {
                if (IsOpen)
                {
                    SetOpen(false);
                }

                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            bool shiftHeld = keyboard.leftShiftKey.isPressed ||
                             keyboard.rightShiftKey.isPressed;

            if (shiftHeld && keyboard[toggleKey].wasPressedThisFrame)
            {
                SetOpen(!IsOpen);
            }
        }
        
        private void LateUpdate()
        {
            if (!IsOpen || !unlockCursorWhileOpen)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable()
        {
            SharedDevelopmentActions.CommandResult -= HandleCommandResult;

            if (IsOpen)
            {
                SetOpen(false);
            }
        }

        private void OnGUI()
        {
            if (!IsOpen || !CommandsAvailable)
            {
                return;
            }

            EnsureStyles();

            float widthRatio = Screen.width / Mathf.Max(1f, referenceWidth);
            float heightRatio = Screen.height / Mathf.Max(1f, referenceHeight);
            float scale = Mathf.Clamp(
                Mathf.Min(widthRatio, heightRatio),
                minimumScale,
                maximumScale);

            if (!_windowInitialized)
            {
                _windowRect = new Rect(
                    initialPosition.x,
                    initialPosition.y,
                    Mathf.Max(400f, panelSize.x),
                    Mathf.Max(520f, panelSize.y));
                _windowInitialized = true;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            int previousDepth = GUI.depth;
            GUI.depth = -10000;
            GUI.matrix = Matrix4x4.Scale(
                new Vector3(scale, scale, 1f));

            float logicalWidth = Screen.width / scale;
            float logicalHeight = Screen.height / scale;
            _windowRect.x = Mathf.Clamp(
                _windowRect.x,
                0f,
                Mathf.Max(0f, logicalWidth - _windowRect.width));
            _windowRect.y = Mathf.Clamp(
                _windowRect.y,
                0f,
                Mathf.Max(0f, logicalHeight - 36f));

            _windowRect = GUI.Window(
                GetInstanceID(),
                _windowRect,
                DrawWindow,
                GUIContent.none);

            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Space(6f);
            GUILayout.Label("SHARED DEVELOPMENT TOOLS", _titleStyle);
            GUILayout.Label("Shift + X to close", _valueStyle);
            GUILayout.Space(8f);

            DrawConnectionSummary();
            DrawWalletSection();
            DrawCarrierSection();
            DrawSkillsSection();
            DrawStatus();

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 44f));
        }

        private void DrawConnectionSummary()
        {
            NetworkManager manager = NetworkManager.Singleton;
            string role = "Offline";

            if (manager != null && manager.IsListening)
            {
                role = manager.IsHost
                    ? "Host"
                    : manager.IsServer
                        ? "Server"
                        : "Client";
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Session", _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(role, _valueStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawWalletSection()
        {
            DrawSectionHeader("SHARED WALLET");

            NetworkSharedWallet wallet = NetworkSharedWallet.Instance;
            string balance = wallet != null && wallet.IsSpawned
                ? wallet.Balance.ToString("N0")
                : "Not Ready";
            DrawValueRow("Balance", balance);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+100", _buttonStyle))
            {
                SharedDevelopmentActions.RequestMoneyDelta(100);
            }

            if (GUILayout.Button("+1,000", _buttonStyle))
            {
                SharedDevelopmentActions.RequestMoneyDelta(1000);
            }

            if (GUILayout.Button("-100", _buttonStyle))
            {
                SharedDevelopmentActions.RequestMoneyDelta(-100);
            }

            if (GUILayout.Button("-1,000", _buttonStyle))
            {
                SharedDevelopmentActions.RequestMoneyDelta(-1000);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Custom", _labelStyle, GUILayout.Width(90f));
            _moneyAmountText = GUILayout.TextField(
                _moneyAmountText,
                12,
                _textFieldStyle,
                GUILayout.Width(130f));

            if (GUILayout.Button("Add", _buttonStyle))
            {
                RequestCustomMoney(true);
            }

            if (GUILayout.Button("Remove", _buttonStyle))
            {
                RequestCustomMoney(false);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawCarrierSection()
        {
            DrawSectionHeader("LOCAL PLAYER CARRY STACK");

            NetworkItemCarrier carrier = NetworkItemCarrier.Local;

            if (carrier == null || !carrier.IsSpawned)
            {
                DrawValueRow("Carrier", "Not Ready");
            }
            else
            {
                DrawValueRow(
                    "Held / Limit",
                    $"{carrier.HeldItemCount} / {carrier.CarryLimit}");
            }

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Limit -1", _buttonStyle))
            {
                SharedDevelopmentActions.RequestCarryLimitDelta(-1);
            }

            if (GUILayout.Button("Limit +1", _buttonStyle))
            {
                SharedDevelopmentActions.RequestCarryLimitDelta(1);
            }

            if (GUILayout.Button("Reset Default", _buttonStyle))
            {
                SharedDevelopmentActions.RequestCarryLimitReset();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSkillsSection()
        {
            DrawSectionHeader("SHARED SKILLS");

            NetworkSharedSkillService service =
                NetworkSharedSkillService.Instance;

            if (service == null || !service.IsSpawned ||
                service.Catalog == null)
            {
                DrawValueRow("Skill Service", "Not Ready");
            }
            else
            {
                for (int i = 0; i < service.Catalog.SkillCount; i++)
                {
                    if (!service.Catalog.TryGetSkillAtSlot(
                            i,
                            out SharedSkillDefinition definition))
                    {
                        continue;
                    }

                    bool hasState = service.TryGetRuntimeState(
                        definition.SkillId,
                        out SharedSkillRuntimeState state);
                    float remaining = hasState
                        ? service.GetRemainingCooldownSeconds(
                            definition.SkillId)
                        : 0f;
                    string value = !hasState
                        ? "No State"
                        : !state.IsUnlocked
                            ? "Locked"
                            : remaining > 0f
                                ? $"CD {remaining:0.0}s"
                                : "Ready";

                    DrawValueRow(
                        $"{i + 1}. {definition.DisplayName}",
                        value);
                }
            }

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset Cooldowns", _buttonStyle))
            {
                SharedDevelopmentActions.RequestSkillCooldownsReset();
            }

            if (GUILayout.Button("Unlock All Skills", _buttonStyle))
            {
                SharedDevelopmentActions.RequestUnlockAllSkills();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            GUILayout.Space(12f);
            _statusStyle.normal.textColor = _lastCommandSucceeded
                ? new Color(0.45f, 1f, 0.62f)
                : new Color(1f, 0.42f, 0.38f);
            GUILayout.Label(_statusMessage, _statusStyle);
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Space(10f);
            GUILayout.Label(title, _sectionStyle);
            GUILayout.Space(2f);
        }

        private void DrawValueRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, _valueStyle);
            GUILayout.EndHorizontal();
        }

        private void RequestCustomMoney(bool add)
        {
            if (!long.TryParse(_moneyAmountText, out long amount) ||
                amount <= 0)
            {
                HandleCommandResult(
                    false,
                    "Enter a positive whole-number money amount.");
                return;
            }

            SharedDevelopmentActions.RequestMoneyDelta(
                add ? amount : -amount);
        }

        private void HandleCommandResult(bool succeeded, string message)
        {
            _lastCommandSucceeded = succeeded;
            _statusMessage = string.IsNullOrWhiteSpace(message)
                ? succeeded ? "Command completed." : "Command failed."
                : message;
        }

        private void SetOpen(bool open)
        {
            if (IsOpen == open)
            {
                return;
            }

            IsOpen = open;

            if (!unlockCursorWhileOpen)
            {
                return;
            }

            if (open)
            {
                _previousCursorLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = _previousCursorLockMode;
                Cursor.visible = _previousCursorVisible;
            }
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = CreateStyle(
                GUI.skin.label,
                24,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            _sectionStyle = CreateStyle(
                GUI.skin.box,
                17,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Color(0.72f, 0.9f, 1f));
            _sectionStyle.padding = new RectOffset(10, 10, 6, 6);
            _labelStyle = CreateStyle(
                GUI.skin.label,
                16,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.84f, 0.86f, 0.9f));
            _valueStyle = CreateStyle(
                GUI.skin.label,
                16,
                FontStyle.Bold,
                TextAnchor.MiddleRight,
                Color.white);
            _buttonStyle = CreateStyle(
                GUI.skin.button,
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            _buttonStyle.fixedHeight = 34f;
            _textFieldStyle = CreateStyle(
                GUI.skin.textField,
                16,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white);
            _textFieldStyle.fixedHeight = 34f;
            _statusStyle = CreateStyle(
                GUI.skin.box,
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);
            _statusStyle.wordWrap = true;
            _statusStyle.padding = new RectOffset(8, 8, 8, 8);
        }

        private GUIStyle CreateStyle(
            GUIStyle source,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color textColor)
        {
            var style = new GUIStyle(source)
            {
                font = overrideFont,
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment
            };
            style.normal.textColor = textColor;
            return style;
        }
    }
}
