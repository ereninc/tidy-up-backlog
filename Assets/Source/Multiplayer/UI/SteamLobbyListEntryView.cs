using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EXW.Multiplayer.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/UI/Steam Lobby List Entry View")]
    public sealed class SteamLobbyListEntryView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Button selectButton;
        [SerializeField] private TMP_Text lobbyNameText;
        [SerializeField] private TMP_Text memberCountText;

        private SteamLobbySummary _lobby;
        private Action<SteamLobbySummary> _selected;

        public SteamLobbySummary Lobby => _lobby;

        private void Reset()
        {
            selectButton = GetComponent<Button>();

            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);

            if (texts.Length > 0) lobbyNameText = texts[0];
            if (texts.Length > 1) memberCountText = texts[1];
        }

        private void Awake()
        {
            if (selectButton == null)
            {
                selectButton = GetComponent<Button>();
            }
        }

        private void OnEnable()
        {
            if (selectButton != null)
            {
                selectButton.onClick.AddListener(HandleSelected);
            }
        }

        private void OnDisable()
        {
            if (selectButton != null)
            {
                selectButton.onClick.RemoveListener(HandleSelected);
            }
        }

        public void Bind(
            SteamLobbySummary lobby,
            Action<SteamLobbySummary> selected)
        {
            _lobby = lobby;
            _selected = selected;

            if (lobbyNameText != null)
            {
                lobbyNameText.text = lobby != null
                    ? BuildLobbyName(lobby)
                    : string.Empty;
            }

            if (memberCountText != null)
            {
                memberCountText.text = lobby != null
                    ? BuildMemberCount(lobby)
                    : string.Empty;
            }

            if (selectButton != null)
            {
                selectButton.interactable = lobby != null &&
                                            lobby.LobbyId != 0 &&
                                            lobby.IsJoinableForLocalPlayer &&
                                            lobby.HasOpenSlot;
            }
        }

        private static string BuildLobbyName(SteamLobbySummary lobby)
        {
            if (lobby.State != SteamLobbyState.Playing)
            {
                return lobby.Name;
            }

            return lobby.LocalJoinKind == MultiplayerJoinKind.Reconnect
                ? $"{lobby.Name}  <color=#62C6FF>(Reconnect)</color>"
                : $"{lobby.Name}  <color=#F5C451>(In Progress)</color>";
        }

        private static string BuildMemberCount(SteamLobbySummary lobby)
        {
            if (lobby.State == SteamLobbyState.Playing &&
                lobby.HasSessionRosterMetadata)
            {
                string runningValue =
                    $"{lobby.MemberCount}/{lobby.MemberLimit} Online  •  " +
                    $"{lobby.AssignedSessionSlots}/{lobby.MemberLimit} Slots";

                if (lobby.ReservedReconnectSeats > 0)
                {
                    runningValue +=
                        $"  •  {lobby.ReservedReconnectSeats} Offline";
                }

                if (lobby.AllowsLateJoin &&
                    lobby.HasUnassignedSessionSlot)
                {
                    runningValue += "  •  Late Join";
                }

                return runningValue;
            }

            string value = $"{lobby.MemberCount}/{lobby.MemberLimit} Players";

            if (lobby.ReservedReconnectSeats > 0)
            {
                value += $"  •  {lobby.ReservedReconnectSeats} Reserved";
            }

            if (lobby.State == SteamLobbyState.Playing && lobby.AllowsLateJoin)
            {
                value += "  •  Late Join";
            }

            return value;
        }

        public bool ValidateReferences(bool logResult = true)
        {
            bool valid = selectButton != null &&
                         lobbyNameText != null &&
                         memberCountText != null;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log("[LobbyListEntryUI] References are valid.", this);
                }
                else
                {
                    Debug.LogError(
                        "[LobbyListEntryUI] Assign Button, Lobby Name Text and " +
                        "Member Count Text.",
                        this);
                }
            }

            return valid;
        }

        private void HandleSelected()
        {
            if (_lobby != null)
            {
                _selected?.Invoke(_lobby);
            }
        }
    }
}
