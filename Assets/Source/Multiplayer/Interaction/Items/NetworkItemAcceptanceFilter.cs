using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    [CreateAssetMenu(
        fileName = "ItemAcceptanceFilter",
        menuName = "EXW/Multiplayer/Items/Acceptance Filter")]
    public sealed class NetworkItemAcceptanceFilter : ScriptableObject
    {
        [InfoBox(
            "Empty lists accept every item. Require Any means at least one tag; " +
            "Require All means every tag; Reject Any always wins.")]
        [SerializeField]
        private NetworkItemTagDefinition[] requireAny =
            Array.Empty<NetworkItemTagDefinition>();

        [SerializeField]
        private NetworkItemTagDefinition[] requireAll =
            Array.Empty<NetworkItemTagDefinition>();

        [SerializeField]
        private NetworkItemTagDefinition[] rejectAny =
            Array.Empty<NetworkItemTagDefinition>();

        public bool Accepts(NetworkWorldItem item, out string rejectionMessage)
        {
            rejectionMessage = string.Empty;

            if (item == null)
            {
                rejectionMessage = "No item was supplied.";
                return false;
            }

            NetworkItemDefinition definition = item.Definition;

            if (ContainsMatchingTag(definition, rejectAny))
            {
                rejectionMessage = $"{item.DisplayName} is rejected here.";
                return false;
            }

            if (HasAnyConfiguredTag(requireAny) &&
                !ContainsMatchingTag(definition, requireAny))
            {
                rejectionMessage = $"{item.DisplayName} is not an accepted type.";
                return false;
            }

            if (!ContainsEveryTag(definition, requireAll))
            {
                rejectionMessage = $"{item.DisplayName} is missing a required tag.";
                return false;
            }

            return true;
        }

        private static bool ContainsMatchingTag(
            NetworkItemDefinition definition,
            NetworkItemTagDefinition[] candidates)
        {
            if (definition == null || candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                NetworkItemTagDefinition tag = candidates[i];

                if (tag != null && definition.HasTag(tag))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsEveryTag(
            NetworkItemDefinition definition,
            NetworkItemTagDefinition[] candidates)
        {
            if (candidates == null)
            {
                return true;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                NetworkItemTagDefinition tag = candidates[i];

                if (tag != null &&
                    (definition == null || !definition.HasTag(tag)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAnyConfiguredTag(
            NetworkItemTagDefinition[] candidates)
        {
            if (candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
