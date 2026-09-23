using UnityEngine;

namespace EXW.Multiplayer
{
    public enum SharedSkillEffectType : byte
    {
        None = 0,
        RevealMatchingGameCases = 1
    }

    public enum GameCaseRevealTargetMode : byte
    {
        AllInstances = 0,
        NotHeld = 1,
        LooseWorldOnly = 2
    }

    /// <summary>
    /// Local definition for one shared co-op skill. Only the small runtime
    /// state (id, level, unlock and cooldown end) is replicated by NGO.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SharedSkill_",
        menuName = "EXW/Multiplayer/Progression/Shared Skill")]
    public sealed class SharedSkillDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Min(1)] private int skillId = 1;
        [SerializeField] private string displayName = "Case Finder";
        [SerializeField] private Sprite icon;

        [Header("Availability")]
        [Tooltip("Development convenience. Later the upgrade system unlocks it.")]
        [SerializeField] private bool unlockedByDefault = true;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float cooldownSeconds = 30f;
        [SerializeField, Min(0.1f)] private float effectDurationSeconds = 8f;

        [Header("Effect")]
        [SerializeField] private SharedSkillEffectType effectType =
            SharedSkillEffectType.RevealMatchingGameCases;
        [SerializeField] private GameCaseRevealTargetMode revealTargetMode =
            GameCaseRevealTargetMode.NotHeld;

        public int SkillId => Mathf.Max(1, skillId);
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? $"Skill {SkillId}"
            : displayName;
        public Sprite Icon => icon;
        public bool UnlockedByDefault => unlockedByDefault;
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public float EffectDurationSeconds =>
            Mathf.Max(0.1f, effectDurationSeconds);
        public SharedSkillEffectType EffectType => effectType;
        public GameCaseRevealTargetMode RevealTargetMode => revealTargetMode;

        private void OnValidate()
        {
            skillId = Mathf.Max(1, skillId);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            effectDurationSeconds = Mathf.Max(0.1f, effectDurationSeconds);
        }
    }
}
