using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Put this on NetworkRuntime or GameSessionLoadingScene. The host spawns
    /// exactly one persistent shared-progression network prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Progression/Shared Progression Bootstrap")]
    public sealed class SharedProgressionBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkObject sharedProgressionPrefab;

        private static bool _serverSpawnRequested;
        private Coroutine _monitorRoutine;

        private void OnEnable()
        {
            _monitorRoutine = StartCoroutine(MonitorNetworkSessions());
        }

        private void OnDisable()
        {
            if (_monitorRoutine != null)
            {
                StopCoroutine(_monitorRoutine);
                _monitorRoutine = null;
            }
        }

        private IEnumerator MonitorNetworkSessions()
        {
            while (enabled)
            {
                while (enabled &&
                       (NetworkManager.Singleton == null ||
                        !NetworkManager.Singleton.IsListening))
                {
                    _serverSpawnRequested = false;
                    yield return null;
                }

                if (!enabled)
                {
                    yield break;
                }

                NetworkManager manager = NetworkManager.Singleton;

                if (manager.IsServer &&
                    NetworkSharedSkillService.Instance == null &&
                    !_serverSpawnRequested)
                {
                    if (sharedProgressionPrefab == null)
                    {
                        Debug.LogError(
                            "Shared Progression Prefab is missing.",
                            this);
                    }
                    else
                    {
                        _serverSpawnRequested = true;
                        NetworkObject instance =
                            Instantiate(sharedProgressionPrefab);
                        instance.name = sharedProgressionPrefab.name;
                        instance.Spawn(destroyWithScene: false);
                    }
                }

                while (enabled && manager != null && manager.IsListening)
                {
                    yield return null;
                }

                _serverSpawnRequested = false;
                yield return null;
            }
        }
    }
}
