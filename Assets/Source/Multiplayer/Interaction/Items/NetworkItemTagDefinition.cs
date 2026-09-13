using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    [CreateAssetMenu(
        fileName = "ItemTag",
        menuName = "EXW/Multiplayer/Items/Item Tag")]
    public sealed class NetworkItemTagDefinition : ScriptableObject
    {
        [ShowInInspector]
        [ReadOnly]
        [LabelText("Stable ID")]
        public string StableId => stableId;

        [SerializeField]
        [HideInInspector]
        private string stableId;

        [SerializeField]
        private string displayName = "Item Tag";

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                stableId = Guid.NewGuid().ToString("N");
            }
        }
    }
}
