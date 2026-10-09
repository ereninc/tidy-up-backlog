using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Row Info Presenter")]
    public sealed class GameCaseShelfRowInfoPresenter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        [Required]
        private NetworkGameCaseShelfRow row;

        [SerializeField]
        [Required]
        private TMP_Text gameNameText;

        private NetworkGameCaseShelfRow _subscribedRow;

        private void Reset()
        {
            gameNameText = GetComponentInChildren<TMP_Text>(true);
        }

        private void OnEnable()
        {
            _subscribedRow = row;

            if (_subscribedRow != null)
            {
                _subscribedRow.LockedAppIdChanged += HandleLockedAppIdChanged;
            }

            GameCaseSessionPlan.Changed += HandleGameNamesChanged;
            RefreshLabel();
        }

        private void OnDisable()
        {
            if (_subscribedRow != null)
            {
                _subscribedRow.LockedAppIdChanged -= HandleLockedAppIdChanged;
                _subscribedRow = null;
            }

            GameCaseSessionPlan.Changed -= HandleGameNamesChanged;
        }

        private void HandleLockedAppIdChanged(uint previous, uint current)
        {
            RefreshLabel();
        }

        private void HandleGameNamesChanged()
        {
            if (row != null && row.LockedAppId != 0)
            {
                RefreshLabel();
            }
        }

        private void RefreshLabel()
        {
            if (gameNameText == null)
            {
                return;
            }

            uint appId = row != null ? row.LockedAppId : 0;
            string label = appId == 0
                ? "Empty"
                : GameCaseSessionPlan.GetGameName(appId);

            if (gameNameText.text != label)
            {
                gameNameText.text = label;
            }
        }
    }
}
