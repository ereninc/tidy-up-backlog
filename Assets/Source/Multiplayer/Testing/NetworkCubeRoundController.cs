using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCubeRoundController : NetworkBehaviour
    {
        [Header("Spawn")]
        [SerializeField]
        private NetworkObject cubePrefab;

        [SerializeField, Min(1)]
        private int cubeCount = 20;

        [SerializeField, Min(0.1f)]
        private float spawnRadius = 4.5f;

        [SerializeField, Min(0f)]
        private float minimumSpacing = 1f;

        [SerializeField, Min(1)]
        private int maxPlacementAttempts = 60;

        [Header("Local Input")]
        [SerializeField]
        private Camera inputCamera;

        [SerializeField]
        private LayerMask clickableLayers = ~0;

        [Header("Debug")]
        [SerializeField]
        private bool showRuntimePanel = true;

        private readonly HashSet<NetworkClickableCube> activeCubes =
            new HashSet<NetworkClickableCube>();

        private readonly NetworkVariable<int> remainingCubeCount =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> roundComplete =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public int RemainingCubeCount => remainingCubeCount.Value;
        public bool IsRoundComplete => roundComplete.Value;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            remainingCubeCount.OnValueChanged +=
                HandleRemainingCubeCountChanged;

            roundComplete.OnValueChanged +=
                HandleRoundCompleteChanged;

            Debug.Log(
                $"[CubeTest] Round controller spawned. " +
                $"Role={GetNetworkRole()}, " +
                $"Remaining={remainingCubeCount.Value}",
                this);

            if (IsServer)
            {
                StartRoundOnServer();
            }
        }

        public override void OnNetworkDespawn()
        {
            remainingCubeCount.OnValueChanged -=
                HandleRemainingCubeCountChanged;

            roundComplete.OnValueChanged -=
                HandleRoundCompleteChanged;

            activeCubes.Clear();

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            ReadLocalClickInput();
        }

        private void ReadLocalClickInput()
        {
            if (!IsSpawned ||
                !IsClient ||
                roundComplete.Value ||
                Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            Camera cameraToUse =
                inputCamera != null
                    ? inputCamera
                    : Camera.main;

            if (cameraToUse == null)
            {
                Debug.LogWarning(
                    "[CubeTest] No input camera was assigned " +
                    "and Camera.main could not be found.",
                    this);

                return;
            }

            Vector2 mousePosition =
                Mouse.current.position.ReadValue();

            Ray ray =
                cameraToUse.ScreenPointToRay(mousePosition);

            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    Mathf.Infinity,
                    clickableLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (hit.collider.TryGetComponent(
                    out NetworkClickableCube cube))
            {
                cube.RequestConsume();
            }
        }

        private void StartRoundOnServer()
        {
            if (!IsServer)
                return;

            if (cubePrefab == null)
            {
                Debug.LogError(
                    "[CubeTest] Cube Prefab is not assigned.",
                    this);

                return;
            }

            if (!cubePrefab.TryGetComponent(
                    out NetworkClickableCube _))
            {
                Debug.LogError(
                    "[CubeTest] Cube Prefab needs a " +
                    "NetworkClickableCube component.",
                    cubePrefab);

                return;
            }

            activeCubes.Clear();

            remainingCubeCount.Value = 0;
            roundComplete.Value = false;

            List<Vector3> usedPositions =
                new List<Vector3>(cubeCount);

            for (int i = 0; i < cubeCount; i++)
            {
                Vector3 spawnPosition =
                    FindSpawnPosition(usedPositions);

                Quaternion spawnRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        Random.Range(0f, 360f));

                NetworkObject spawnedObject =
                    Instantiate(
                        cubePrefab,
                        spawnPosition,
                        spawnRotation);

                NetworkClickableCube clickableCube =
                    spawnedObject.GetComponent<NetworkClickableCube>();

                clickableCube.InitializeOnServer(this);

                activeCubes.Add(clickableCube);
                usedPositions.Add(spawnPosition);

                // Cube gameplay scene değiştirilince yok edilsin.
                spawnedObject.Spawn(true);
            }

            remainingCubeCount.Value = activeCubes.Count;

            Debug.Log(
                $"[CubeTest] Server spawned " +
                $"{activeCubes.Count} cubes.",
                this);
        }

        private Vector3 FindSpawnPosition(
            List<Vector3> usedPositions)
        {
            float minimumSpacingSquared =
                minimumSpacing * minimumSpacing;

            Vector3 candidate = transform.position;

            for (int attempt = 0;
                 attempt < maxPlacementAttempts;
                 attempt++)
            {
                Vector2 point =
                    Random.insideUnitCircle * spawnRadius;

                candidate =
                    transform.position +
                    new Vector3(point.x, point.y, 0f);

                bool validPosition = true;

                for (int i = 0; i < usedPositions.Count; i++)
                {
                    if ((usedPositions[i] - candidate).sqrMagnitude <
                        minimumSpacingSquared)
                    {
                        validPosition = false;
                        break;
                    }
                }

                if (validPosition)
                {
                    return candidate;
                }
            }

            Debug.LogWarning(
                "[CubeTest] Could not satisfy minimum spacing. " +
                "Using the last generated position.",
                this);

            return candidate;
        }

        public bool TryConsumeCube(
            NetworkClickableCube cube,
            ulong senderClientId)
        {
            if (!IsServer ||
                cube == null ||
                !activeCubes.Remove(cube))
            {
                return false;
            }

            remainingCubeCount.Value = activeCubes.Count;

            Debug.Log(
                $"[CubeTest] ClientId={senderClientId} clicked a cube. " +
                $"Remaining={remainingCubeCount.Value}",
                this);

            if (remainingCubeCount.Value == 0 &&
                !roundComplete.Value)
            {
                CompleteRoundOnServer();
            }

            return true;
        }

        private void CompleteRoundOnServer()
        {
            if (!IsServer || roundComplete.Value)
                return;

            roundComplete.Value = true;
            RoundCompletedRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RoundCompletedRpc()
        {
            Debug.Log(
                $"[CubeTest] GAME COMPLETE RPC received. " +
                $"Role={GetNetworkRole()}",
                this);
        }

        private void HandleRemainingCubeCountChanged(
            int previousValue,
            int newValue)
        {
            Debug.Log(
                $"[CubeTest] Remaining cubes: " +
                $"{previousValue} -> {newValue}. " +
                $"Role={GetNetworkRole()}",
                this);
        }

        private void HandleRoundCompleteChanged(
            bool previousValue,
            bool newValue)
        {
            if (newValue)
            {
                Debug.Log(
                    $"[CubeTest] Round complete state synchronized. " +
                    $"Role={GetNetworkRole()}",
                    this);
            }
        }

        private string GetNetworkRole()
        {
            if (IsHost)
                return "Host";

            if (IsServer)
                return "Server";

            if (IsClient)
                return "Client";

            return "Offline";
        }

        private void OnGUI()
        {
            if (!showRuntimePanel)
                return;

            GUILayout.BeginArea(
                new Rect(
                    Screen.width - 286f,
                    16f,
                    270f,
                    135f),
                "Cube Network Test",
                GUI.skin.window);

            GUILayout.Space(6f);
            GUILayout.Label($"Role: {GetNetworkRole()}");

            if (!IsSpawned)
            {
                GUILayout.Label("Waiting for network spawn...");
            }
            else
            {
                GUILayout.Label(
                    $"Remaining Cubes: {remainingCubeCount.Value}");

                GUILayout.Label(
                    roundComplete.Value
                        ? "GAME COMPLETE"
                        : "Click the cubes!");
            }

            GUILayout.EndArea();
        }
    }
}