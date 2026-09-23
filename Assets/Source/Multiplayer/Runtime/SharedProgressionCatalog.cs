using System;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Stable local ordering for the shared skill bar. Slot zero maps to key 1.
    /// Every build must use the same catalog and skill ids.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SharedProgressionCatalog",
        menuName = "EXW/Multiplayer/Progression/Shared Progression Catalog")]
    public sealed class SharedProgressionCatalog : ScriptableObject
    {
        [SerializeField] private SharedSkillDefinition[] skills =
            Array.Empty<SharedSkillDefinition>();

        public int SkillCount => skills?.Length ?? 0;

        public bool TryGetSkillAtSlot(
            int slotIndex,
            out SharedSkillDefinition definition)
        {
            definition = null;

            if (skills == null || slotIndex < 0 ||
                slotIndex >= skills.Length)
            {
                return false;
            }

            definition = skills[slotIndex];
            return definition != null;
        }

        public bool TryGetSkill(
            int skillId,
            out SharedSkillDefinition definition)
        {
            definition = null;

            if (skills == null)
            {
                return false;
            }

            for (int i = 0; i < skills.Length; i++)
            {
                SharedSkillDefinition candidate = skills[i];

                if (candidate != null && candidate.SkillId == skillId)
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            if (skills == null)
            {
                return;
            }

            for (int i = 0; i < skills.Length; i++)
            {
                SharedSkillDefinition current = skills[i];

                if (current == null)
                {
                    continue;
                }

                for (int j = i + 1; j < skills.Length; j++)
                {
                    if (skills[j] != null &&
                        skills[j].SkillId == current.SkillId)
                    {
                        Debug.LogError(
                            $"Duplicate shared skill id {current.SkillId} " +
                            $"in {name}.",
                            this);
                    }
                }
            }
        }
    }
}
