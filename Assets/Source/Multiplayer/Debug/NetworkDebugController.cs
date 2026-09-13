using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(SteamNetworkingSocketsTransport))]
    public sealed class NetworkDebugController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private SteamNetworkingSocketsTransport steamTransport;

        [Header("Debug Client")]
        [Tooltip("Client olarak bağlanılacak host hesabının SteamID64 değeri.")]
        [SerializeField] private string hostSteamId = "";

        [Header("Runtime Panel")]
        [SerializeField] private bool showRuntimePanel = true;

        private string statusMessage = "Stopped";

        private void Reset()
        {
            FindRequiredComponents();
        }

        private void Awake()
        {
            FindRequiredComponents();

            if (networkManager == null || steamTransport == null)
            {
                Debug.LogError(
                    "[NetworkDebug] NetworkManager veya Steam transport bulunamadı.",
                    this);

                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (networkManager == null)
                return;

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void OnDisable()
        {
            if (networkManager == null)
                return;

            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        public void StartHost()
        {
            if (!PrepareForStart())
                return;

            statusMessage = "Host starting...";
            Debug.Log($"[NetworkDebug] {statusMessage}", this);

            bool started = networkManager.StartHost();

            if (!started)
            {
                statusMessage = "StartHost returned false.";
                Debug.LogError($"[NetworkDebug] {statusMessage}", this);
                return;
            }

            statusMessage = "Host running.";
            Debug.Log($"[NetworkDebug] {statusMessage}", this);
        }

        public void StartClient()
        {
            if (!PrepareForStart())
                return;

            if (!ulong.TryParse(hostSteamId.Trim(), out ulong parsedSteamId) ||
                parsedSteamId == 0)
            {
                statusMessage = "Enter a valid host SteamID64.";
                Debug.LogError($"[NetworkDebug] {statusMessage}", this);
                return;
            }

            steamTransport.SetTargetSteamId(parsedSteamId);

            statusMessage =
                $"Connecting to SteamID {parsedSteamId}...";

            Debug.Log($"[NetworkDebug] {statusMessage}", this);

            bool started = networkManager.StartClient();

            if (!started)
            {
                statusMessage = "StartClient returned false.";
                Debug.LogError($"[NetworkDebug] {statusMessage}", this);
            }
        }

        public void Disconnect()
        {
            if (!networkManager.IsListening)
            {
                statusMessage = "Already stopped.";
                return;
            }

            networkManager.Shutdown();
            statusMessage = "Stopped.";

            Debug.Log("[NetworkDebug] NetworkManager shut down.", this);
        }

        private bool PrepareForStart()
        {
            if (networkManager.IsListening)
            {
                statusMessage = "NetworkManager is already running.";
                Debug.LogWarning($"[NetworkDebug] {statusMessage}", this);
                return false;
            }

            networkManager.NetworkConfig.NetworkTransport = steamTransport;
            return true;
        }

        private void HandleClientConnected(ulong clientId)
        {
            statusMessage = $"Client connected. ClientId={clientId}";

            Debug.Log(
                $"[NetworkDebug] {statusMessage}, Role={GetCurrentRole()}",
                this);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            statusMessage = $"Client disconnected. ClientId={clientId}";

            Debug.LogWarning(
                $"[NetworkDebug] {statusMessage}, Role={GetCurrentRole()}",
                this);
        }

        private string GetCurrentRole()
        {
            if (networkManager.IsHost)
                return "Host";

            if (networkManager.IsServer)
                return "Server";

            if (networkManager.IsClient)
                return "Client";

            return "Stopped";
        }

        private void FindRequiredComponents()
        {
            if (networkManager == null)
                networkManager = GetComponent<NetworkManager>();

            if (steamTransport == null)
                steamTransport =
                    GetComponent<SteamNetworkingSocketsTransport>();
        }

        private void OnGUI()
        {
            if (!showRuntimePanel ||
                networkManager == null ||
                steamTransport == null)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(16f, 16f, 430f, 245f),
                "Steam NGO Debug",
                GUI.skin.window);

            GUILayout.Space(8f);
            GUILayout.Label($"Role: {GetCurrentRole()}");
            GUILayout.Label($"Status: {statusMessage}");

            GUILayout.Space(8f);
            GUILayout.Label("Host SteamID64:");

            hostSteamId = GUILayout.TextField(hostSteamId);

            GUILayout.Space(8f);

            GUI.enabled = !networkManager.IsListening;

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Start Host", GUILayout.Height(34f)))
                StartHost();

            if (GUILayout.Button("Start Client", GUILayout.Height(34f)))
                StartClient();

            GUILayout.EndHorizontal();

            GUI.enabled = networkManager.IsListening;

            if (GUILayout.Button("Disconnect", GUILayout.Height(30f)))
                Disconnect();

            GUI.enabled = true;

            GUILayout.Space(6f);
            GUILayout.Label("Steam testi için iki farklı Steam hesabı gerekir.");

            GUILayout.EndArea();
        }
    }
}