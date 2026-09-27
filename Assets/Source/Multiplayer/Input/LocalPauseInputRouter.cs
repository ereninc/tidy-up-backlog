using UnityEngine;
using UnityEngine.Events;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local bridge between the owner input reader and the existing pause UI.
    /// Keep this component on an always-active object, not on the panel that
    /// gets disabled when the menu closes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Input/Local Pause Input Router")]
    public sealed class LocalPauseInputRouter : MonoBehaviour
    {
        [Tooltip(
            "Connect this to the existing pause panel's Toggle/Open method. " +
            "The same event is raised for keyboard and gamepad bindings.")]
        [SerializeField] private UnityEvent onPausePressed = new UnityEvent();

        private NetworkPlayerInputReader _inputReader;

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
                _inputReader.PausePressed -= HandlePausePressed;
            }

            _inputReader = reader;

            if (_inputReader != null)
            {
                _inputReader.PausePressed += HandlePausePressed;
            }
        }

        private void HandlePausePressed()
        {
            onPausePressed?.Invoke();
        }

        /// <summary>
        /// Optional public entry point for UI buttons and development tools.
        /// </summary>
        public void InvokePauseAction()
        {
            HandlePausePressed();
        }
    }
}
