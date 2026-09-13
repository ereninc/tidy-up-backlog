using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Scene-local pause UI adapter. Keep this component on an always-active
    /// GameplayCanvas object and assign a separate child as Pause Menu Root.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Session/Gameplay Pause Menu Controller")]
    public sealed class GameplayPauseMenuController : MonoBehaviour
    {
        [Header("Runtime Reference")]
        [SerializeField] private MultiplayerSessionCoordinator sessionCoordinator;

        [Header("UI References")]
        [SerializeField] private GameObject pauseMenuRoot;
        [Tooltip("Optional in-game button that opens the pause menu.")]
        [SerializeField] private Button openPauseButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button returnToMainMenuButton;
        [SerializeField] private TMP_Text statusText;

        [Header("Behaviour")]
        [SerializeField] private bool toggleWithEscape = true;
        [Tooltip("Time.timeScale is never changed in co-op. This option affects only singleplayer.")]
        [SerializeField] private bool pauseTimeInSinglePlayer = true;
        [SerializeField] private bool lockCursorWhenResuming = true;

        private bool _listenersBound;
        private bool _isOpen;
        private bool _isReturning;
        private bool _ownsTimeScale;
        private float _timeScaleBeforePause = 1f;

        public bool IsOpen => _isOpen;
        public bool IsReturningToMainMenu => _isReturning;

        private void Reset()
        {
            AutoWireKnownHierarchy();
        }

        private void Awake()
        {
            AutoWireKnownHierarchy();
            SetPauseRootVisible(false);
            GameplayInputGate.Clear(this);
        }

        private void OnEnable()
        {
            ResolveCoordinator();
            BindListeners();
            RefreshButtons();
        }

        private void Update()
        {
            ResolveCoordinator();

            if (!toggleWithEscape || _isReturning)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (_isOpen)
            {
                ClosePauseMenu();
            }
            else
            {
                OpenPauseMenu();
            }
        }

        private void OnDisable()
        {
            UnbindListeners();
            RestoreTimeScale();
            GameplayInputGate.Clear(this);
        }

        private void OnDestroy()
        {
            UnbindListeners();
            RestoreTimeScale();
            GameplayInputGate.Clear(this);
        }

        public void OpenPauseMenu()
        {
            ResolveCoordinator();

            if (_isReturning || !CanOpenPauseMenu())
            {
                return;
            }

            _isOpen = true;
            SetPauseRootVisible(true);
            GameplayInputGate.SetBlocked(this, true);
            ReleaseCursor();
            PauseSinglePlayerTimeIfNeeded();
            SetStatus(string.Empty);
            RefreshButtons();

            if (EventSystem.current != null && backButton != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    backButton.gameObject);
            }
        }

        public void ClosePauseMenu()
        {
            if (_isReturning)
            {
                return;
            }

            _isOpen = false;
            RestoreTimeScale();
            SetPauseRootVisible(false);
            GameplayInputGate.Clear(this);
            RefreshButtons();

            if (lockCursorWhenResuming && IsGameplayReady())
            {
                LockCursor();
            }
        }

        public void TogglePauseMenu()
        {
            if (_isOpen)
            {
                ClosePauseMenu();
            }
            else
            {
                OpenPauseMenu();
            }
        }

        public void ReturnToMainMenu()
        {
            if (_isReturning)
            {
                return;
            }

            ResolveCoordinator();

            if (sessionCoordinator == null)
            {
                SetStatus("Session coordinator is unavailable.");
                Debug.LogError(
                    "[PauseMenu] MultiplayerSessionCoordinator is unavailable.",
                    this);
                return;
            }

            _isOpen = true;
            _isReturning = true;
            SetPauseRootVisible(true);
            GameplayInputGate.SetBlocked(this, true);
            ReleaseCursor();
            RestoreTimeScale();
            SetStatus("Returning to main menu...");
            RefreshButtons();

            if (sessionCoordinator.LeaveSessionAndReturnToMainMenu())
            {
                return;
            }

            _isReturning = false;
            SetStatus(
                string.IsNullOrWhiteSpace(sessionCoordinator.LastError)
                    ? "Could not return to main menu."
                    : sessionCoordinator.LastError);
            RefreshButtons();
        }

        public void AutoWireKnownHierarchy()
        {
            ResolveCoordinator();
            Transform searchRoot = transform.root;

            pauseMenuRoot = FindNamedGameObject(
                pauseMenuRoot,
                searchRoot,
                "PauseMenu",
                "PauseMenuPanel",
                "PausePanel");

            openPauseButton = FindNamedComponent(
                openPauseButton,
                searchRoot,
                "Btn_Pause",
                "Btn_OpenPause",
                "PauseButton");

            backButton = FindNamedComponent(
                backButton,
                searchRoot,
                "Btn_Back",
                "Btn_Resume",
                "BackButton",
                "ResumeButton");

            returnToMainMenuButton = FindNamedComponent(
                returnToMainMenuButton,
                searchRoot,
                "Btn_ReturnToMainMenu",
                "Btn_MainMenu",
                "ReturnToMainMenuButton",
                "MainMenuButton");

            statusText = FindNamedComponent(
                statusText,
                searchRoot,
                "StatusText",
                "PauseStatusText",
                "Txt_Status");
        }

        public bool ValidateSetup(bool logResult = true)
        {
            AutoWireKnownHierarchy();
            List<string> problems = new List<string>();

            if (pauseMenuRoot == null)
            {
                problems.Add("Pause Menu Root is missing.");
            }
            else if (pauseMenuRoot == gameObject)
            {
                problems.Add(
                    "Pause Menu Root cannot be the controller GameObject. " +
                    "Put the controller on an always-active parent.");
            }

            if (backButton == null)
            {
                problems.Add("Back/Resume Button is missing.");
            }

            if (returnToMainMenuButton == null)
            {
                problems.Add("Return To Main Menu Button is missing.");
            }

            bool valid = problems.Count == 0;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log(
                        "[PauseMenu] PASS: required UI references are configured.",
                        this);
                }
                else
                {
                    Debug.LogError(
                        "[PauseMenu] Setup problems:\n- " +
                        string.Join("\n- ", problems),
                        this);
                }
            }

            return valid;
        }

        private void ResolveCoordinator()
        {
            if (sessionCoordinator == null)
            {
                sessionCoordinator = MultiplayerSessionCoordinator.Instance;
            }
        }

        private bool CanOpenPauseMenu()
        {
            if (pauseMenuRoot == null || pauseMenuRoot == gameObject)
            {
                return false;
            }

            return sessionCoordinator == null || IsGameplayReady();
        }

        private bool IsGameplayReady()
        {
            return sessionCoordinator != null &&
                   sessionCoordinator.State == GameSessionState.Gameplay;
        }

        private void PauseSinglePlayerTimeIfNeeded()
        {
            if (!pauseTimeInSinglePlayer || !IsSinglePlayer())
            {
                return;
            }

            if (!_ownsTimeScale)
            {
                _timeScaleBeforePause = Time.timeScale;
                _ownsTimeScale = true;
            }

            Time.timeScale = 0f;
        }

        private bool IsSinglePlayer()
        {
            if (sessionCoordinator != null &&
                sessionCoordinator.Mode == GameSessionMode.SinglePlayer)
            {
                return true;
            }

            MultiplayerFlowController flow =
                MultiplayerFlowController.Instance;
            return flow != null && flow.IsSinglePlayer;
        }

        private void RestoreTimeScale()
        {
            if (!_ownsTimeScale)
            {
                return;
            }

            Time.timeScale = _timeScaleBeforePause;
            _ownsTimeScale = false;
        }

        private void BindListeners()
        {
            if (_listenersBound)
            {
                return;
            }

            if (openPauseButton != null)
            {
                openPauseButton.onClick.AddListener(OpenPauseMenu);
            }

            if (backButton != null)
            {
                backButton.onClick.AddListener(ClosePauseMenu);
            }

            if (returnToMainMenuButton != null)
            {
                returnToMainMenuButton.onClick.AddListener(ReturnToMainMenu);
            }

            _listenersBound = true;
        }

        private void UnbindListeners()
        {
            if (!_listenersBound)
            {
                return;
            }

            if (openPauseButton != null)
            {
                openPauseButton.onClick.RemoveListener(OpenPauseMenu);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(ClosePauseMenu);
            }

            if (returnToMainMenuButton != null)
            {
                returnToMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            }

            _listenersBound = false;
        }

        private void RefreshButtons()
        {
            if (openPauseButton != null)
            {
                openPauseButton.interactable = !_isOpen && !_isReturning;
            }

            if (backButton != null)
            {
                backButton.interactable = !_isReturning;
            }

            if (returnToMainMenuButton != null)
            {
                returnToMainMenuButton.interactable = !_isReturning;
            }
        }

        private void SetPauseRootVisible(bool visible)
        {
            if (pauseMenuRoot != null && pauseMenuRoot != gameObject)
            {
                pauseMenuRoot.SetActive(visible);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = message ?? string.Empty;
            statusText.gameObject.SetActive(
                !string.IsNullOrWhiteSpace(statusText.text));
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static GameObject FindNamedGameObject(
            GameObject current,
            Transform root,
            params string[] names)
        {
            if (current != null || root == null)
            {
                return current;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < transforms.Length; i++)
            {
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (string.Equals(
                            transforms[i].name,
                            names[nameIndex],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return transforms[i].gameObject;
                    }
                }
            }

            return null;
        }

        private static T FindNamedComponent<T>(
            T current,
            Transform root,
            params string[] names)
            where T : Component
        {
            if (current != null || root == null)
            {
                return current;
            }

            T[] components = root.GetComponentsInChildren<T>(true);

            for (int i = 0; i < components.Length; i++)
            {
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (string.Equals(
                            components[i].name,
                            names[nameIndex],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return components[i];
                    }
                }
            }

            return null;
        }
    }
}
