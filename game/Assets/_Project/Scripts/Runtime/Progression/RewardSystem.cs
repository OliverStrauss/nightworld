using System;
using NightWorld.Combat;
using UnityEngine;

namespace NightWorld.Progression
{
    /// <summary>
    /// Listens for enemy deaths and pays out XP and gold to the player.
    /// Dispose it to stop listening. Loot drops are a future card.
    /// </summary>
    public class RewardSystem : IDisposable
    {
        private readonly PlayerProgression progression;
        private readonly System.Random rng;

        public RewardSystem(PlayerProgression progression, int seed = 0)
        {
            this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
            rng = new System.Random(seed);
            Enemy.AnyKilled += OnEnemyKilled;
        }

        private void OnEnemyKilled(Enemy enemy)
        {
            var def = enemy.Definition;
            if (def == null) return;

            progression.AddXp(def.xpReward);

            int min = def.goldMin;
            int max = Mathf.Max(def.goldMin, def.goldMax);
            int gold = rng.Next(min, max + 1);
            if (gold > 0) progression.AddGold(gold);
        }

        public void Dispose()
        {
            Enemy.AnyKilled -= OnEnemyKilled;
        }
    }
}
