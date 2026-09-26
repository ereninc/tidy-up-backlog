using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local presentation for GameSessionLoadingScene. It listens to the
    /// coordinator's real progress; it does not invent a timer or control
    /// network scene travel itself.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Game Session Loading UI")]
    public sealed class GameSessionLoadingUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private NetworkGameCaseLoadingCoordinator coordinator;

        [SerializeField]
        private TMP_Text statusText;

        [SerializeField]
        private Slider progressSlider;

        [Header("Presentation")]
        [SerializeField, Min(0.01f)]
        private float sliderFillSpeed = 1.5f;

        [SerializeField]
        private Color normalTextColor = Color.white;

        [SerializeField]
        private Color errorTextColor =
            new Color(1f, 0.35f, 0.35f, 1f);

        private float _targetProgress;
        private Coroutine _bindRoutine;
        private bool _subscribed;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void Awake()
        {
            AutoAssignReferences();

            if (progressSlider != null)
            {
                progressSlider.minValue = 0f;
                progressSlider.maxValue = 1f;
                progressSlider.wholeNumbers = false;
                progressSlider.transition =
                    Selectable.Transition.None;
                progressSlider.interactable = false;
                progressSlider.SetValueWithoutNotify(0f);
            }
        }

        private void OnEnable()
        {
            TryBind();

            if (!_subscribed)
            {
                _bindRoutine = StartCoroutine(BindWhenAvailable());
            }
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            Unbind();
        }

        private void Update()
        {
            if (progressSlider == null)
            {
                return;
            }

            float next = Mathf.MoveTowards(
                progressSlider.value,
                _targetProgress,
                sliderFillSpeed * Time.unscaledDeltaTime);

            progressSlider.SetValueWithoutNotify(next);
        }

        private IEnumerator BindWhenAvailable()
        {
            while (isActiveAndEnabled && !_subscribed)
            {
                TryBind();
                yield return null;
            }

            _bindRoutine = null;
        }

        private void TryBind()
        {
            if (_subscribed)
            {
                return;
            }

            if (coordinator == null)
            {
                coordinator =
                    FindFirstObjectByType<
                        NetworkGameCaseLoadingCoordinator>();
            }

            if (coordinator == null)
            {
                return;
            }

            coordinator.LocalProgressChanged +=
                HandleProgressChanged;
            coordinator.LocalLoadingFailed +=
                HandleLoadingFailed;
            _subscribed = true;

            HandleProgressChanged(
                coordinator.LocalProgress,
                coordinator.LocalStatus);
        }

        private void Unbind()
        {
            if (!_subscribed || coordinator == null)
            {
                _subscribed = false;
                return;
            }

            coordinator.LocalProgressChanged -=
                HandleProgressChanged;
            coordinator.LocalLoadingFailed -=
                HandleLoadingFailed;
            _subscribed = false;
        }

        private void HandleProgressChanged(
            float progress,
            string rawStatus)
        {
            _targetProgress = Mathf.Max(
                _targetProgress,
                Mathf.Clamp01(progress));

            // The coordinator intentionally holds the loading scene briefly
            // at 100%. Snap only this final step so the full bar is guaranteed
            // to render before NGO starts the gameplay scene transition.
            if (progressSlider != null && _targetProgress >= 1f)
            {
                progressSlider.SetValueWithoutNotify(1f);
            }

            if (statusText != null)
            {
                statusText.color = normalTextColor;
                statusText.SetText(
                    GetPlayerFacingStatus(rawStatus));
            }
        }

        private void HandleLoadingFailed(string error)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.color = errorTextColor;
            statusText.SetText(
                "The shelf goblins dropped something.\n" +
                (string.IsNullOrWhiteSpace(error)
                    ? "Loading failed."
                    : error));
        }

        private static string GetPlayerFacingStatus(string rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus) ||
                rawStatus == "Waiting")
            {
                return "Waking up the shelf goblins...";
            }

            string count = ExtractTrailingCount(rawStatus);

            if (rawStatus.StartsWith("Preparing game libraries"))
            {
                return "Dusting off everyone's Steam backlog...";
            }

            if (rawStatus.StartsWith("Reading Steam libraries"))
            {
                return "Politely rummaging through everyone's library" +
                       count;
            }

            if (rawStatus.StartsWith("Reading local Steam library"))
            {
                return "Politely rummaging through your Steam library...";
            }

            if (rawStatus.StartsWith("Found "))
            {
                return "Found the backlog. It was bigger than expected.";
            }

            if (rawStatus.StartsWith("Steam is offline"))
            {
                return "Steam wandered off. Opening the emergency box...";
            }

            if (rawStatus.StartsWith("Checking Steam cover artwork"))
            {
                return "Checking which boxes remembered their face" +
                       count;
            }

            if (rawStatus.StartsWith("Game manifest ready"))
            {
                return "Shuffling the pile. No peeking.";
            }

            if (rawStatus.StartsWith("Downloading covers"))
            {
                return "Borrowing cover art from the internet" +
                       count;
            }

            if (rawStatus.StartsWith("Building offline cover array"))
            {
                return "Drawing the covers from memory. Close enough.";
            }

            if (rawStatus.StartsWith("Building cover texture array"))
            {
                return "Laminating the box art. Very professional.";
            }

            if (rawStatus.StartsWith("Waiting for everyone"))
            {
                return "Waiting for the slowest librarian...";
            }

            if (rawStatus.StartsWith("Opening gameplay scene"))
            {
                return "Doors open. Mind the backlog.";
            }

            return rawStatus;
        }

        private static string ExtractTrailingCount(string status)
        {
            int openIndex = status.LastIndexOf('(');

            if (openIndex < 0 || !status.EndsWith(")"))
            {
                return "...";
            }

            return " " + status.Substring(openIndex);
        }

        private void AutoAssignReferences()
        {
            if (coordinator == null)
            {
                coordinator =
                    FindFirstObjectByType<
                        NetworkGameCaseLoadingCoordinator>();
            }

            if (statusText == null)
            {
                statusText = GetComponentInChildren<TMP_Text>(true);
            }

            if (progressSlider == null)
            {
                progressSlider = GetComponentInChildren<Slider>(true);
            }
        }
    }
}
