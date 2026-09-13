using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    [CreateAssetMenu(
        fileName = "ItemDefinition",
        menuName = "EXW/Multiplayer/Items/Item Definition")]
    public sealed class NetworkItemDefinition : ScriptableObject
    {
        [ShowInInspector]
        [ReadOnly]
        [LabelText("Stable ID")]
        public string StableId => stableId;

        [SerializeField]
        [HideInInspector]
        private string stableId;

        [SerializeField]
        private string displayName = "World Item";

        [SerializeField]
        [ListDrawerSettings(Expanded = true)]
        private NetworkItemTagDefinition[] tags =
            Array.Empty<NetworkItemTagDefinition>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

        public bool HasTag(NetworkItemTagDefinition tag)
        {
            if (tag == null || tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == tag)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                stableId = Guid.NewGuid().ToString("N");
            }
        }
    }
}
