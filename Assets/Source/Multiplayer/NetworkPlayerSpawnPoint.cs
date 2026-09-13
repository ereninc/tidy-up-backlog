using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Scene-owned spawn marker. SteamConnectionApproval selects an unused marker
    /// before NGO creates the player object, so its initial spawn position is already
    /// correct on every peer.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Network Player Spawn Point")]
    public sealed class NetworkPlayerSpawnPoint : MonoBehaviour
    {
        private static readonly List<NetworkPlayerSpawnPoint> ActivePoints =
            new List<NetworkPlayerSpawnPoint>();

        [InfoBox(
            "Place these markers in the gameplay scene. Lower Spawn Order values " +
            "are assigned first; the host normally receives the first point.")]
        [MinValue(0)]
        [LabelText("Spawn Order")]
        [SerializeField] private int spawnOrder;

        [SerializeField] private Color gizmoColor =
            new Color(0.2f, 0.85f, 1f, 0.85f);

        public int SpawnOrder => spawnOrder;

        public static int Count
        {
            get
            {
                RemoveMissingPoints();
                return ActivePoints.Count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActivePoints.Clear();
        }

        private void OnEnable()
        {
            if (!ActivePoints.Contains(this))
            {
                ActivePoints.Add(this);
                SortPoints();
            }
        }

        private void OnDisable()
        {
            ActivePoints.Remove(this);
        }

        private void OnValidate()
        {
            spawnOrder = Mathf.Max(0, spawnOrder);

            if (isActiveAndEnabled)
            {
                SortPoints();
            }
        }

        public static bool TryGetBySlot(
            int slot,
            out Vector3 position,
            out Quaternion rotation)
        {
            RemoveMissingPoints();
            SortPoints();

            if (slot < 0 || slot >= ActivePoints.Count)
            {
                position = default;
                rotation = default;
                return false;
            }

            Transform target = ActivePoints[slot].transform;
            position = target.position;
            rotation = target.rotation;
            return true;
        }

        [Button("LOG ACTIVE SPAWN POINTS")]
        private void LogActiveSpawnPoints()
        {
            RemoveMissingPoints();
            SortPoints();

            Debug.Log(
                $"[PlayerSpawnPoint] Active spawn points={ActivePoints.Count}",
                this);

            for (int i = 0; i < ActivePoints.Count; i++)
            {
                NetworkPlayerSpawnPoint point = ActivePoints[i];
                Debug.Log(
                    $"[PlayerSpawnPoint] Slot={i}, Order={point.spawnOrder}, " +
                    $"Name={point.name}, Position={point.transform.position}",
                    point);
            }
        }

        private static void RemoveMissingPoints()
        {
            for (int i = ActivePoints.Count - 1; i >= 0; i--)
            {
                if (ActivePoints[i] == null)
                {
                    ActivePoints.RemoveAt(i);
                }
            }
        }

        private static void SortPoints()
        {
            ActivePoints.Sort(ComparePoints);
        }

        private static int ComparePoints(
            NetworkPlayerSpawnPoint left,
            NetworkPlayerSpawnPoint right)
        {
            if (left == null)
            {
                return right == null ? 0 : 1;
            }

            if (right == null)
            {
                return -1;
            }

            int orderComparison = left.spawnOrder.CompareTo(right.spawnOrder);

            return orderComparison != 0
                ? orderComparison
                : left.GetInstanceID().CompareTo(right.GetInstanceID());
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.DrawWireSphere(new Vector3(0f, 0.12f, 0f), 0.32f);
            Gizmos.DrawLine(
                new Vector3(0f, 0.12f, 0f),
                new Vector3(0f, 0.12f, 0.9f));
            Gizmos.DrawLine(
                new Vector3(0f, 0.12f, 0.9f),
                new Vector3(-0.18f, 0.12f, 0.68f));
            Gizmos.DrawLine(
                new Vector3(0f, 0.12f, 0.9f),
                new Vector3(0.18f, 0.12f, 0.68f));

            Gizmos.matrix = previousMatrix;
        }
    }
}