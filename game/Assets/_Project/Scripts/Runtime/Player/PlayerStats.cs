using System;
using NightWorld.Combat;
using NightWorld.Progression;
using UnityEngine;

namespace NightWorld.Player
{
    /// <summary>
    /// The hero's live numbers: level, XP and gold in <see cref="Progression"/>, hit points in
    /// <see cref="Health"/>. Enemy kills pay out through a RewardSystem that lives exactly as long
    /// as this component. Levelling up resets Health to the new level's max HP, which also heals
    /// to full. Added by <see cref="PlayerSpawner.Spawn"/>.
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        public PlayerProgression Progression { get; private set; }
        public Health Health { get; private set; }

        private ProgressionSettings settings;
        private RewardSystem rewards;

        /// <summary>Call once, right after AddComponent.</summary>
        public void Init(ProgressionSettings progressionSettings)
        {
            if (progressionSettings == null) throw new ArgumentNullException(nameof(progressionSettings));
            settings = progressionSettings;

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.Init(settings.MaxHpAt(1));

            Progression = new PlayerProgression(settings);
            Progression.LeveledUp += OnLeveledUp;
            rewards = new RewardSystem(Progression);
        }

        private void OnLeveledUp(int level) => Health.Init(settings.MaxHpAt(level));

        // Unhooks Enemy.AnyKilled, so a destroyed hero (or a finished test) never earns rewards.
        private void OnDestroy()
        {
            rewards?.Dispose();
            rewards = null;
        }
    }
}
