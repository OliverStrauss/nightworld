using System;
using UnityEngine;

namespace NightWorld.Progression
{
    /// <summary>
    /// Tunable numbers for leveling. One asset lives at Data/progression-settings.asset.
    /// XP needed to go from level L to L+1 = round(baseXp * L^exponent).
    /// </summary>
    [CreateAssetMenu(menuName = "NightWorld/Progression Settings", fileName = "progression-settings")]
    public class ProgressionSettings : ScriptableObject
    {
        [Min(1)] public int levelCap = 20;
        [Min(1f)] public float baseXp = 50f;
        [Min(1f)] public float exponent = 1.6f;

        [Header("Per-level stats")]
        [Min(1)] public int baseMaxHp = 100;
        [Min(0)] public int maxHpPerLevel = 10;
        [Min(0)] public int baseDamage = 5;
        [Min(0)] public int damagePerLevel = 1;

        public int XpToNext(int level) => XpCurve(level, baseXp, exponent);

        public int MaxHpAt(int level) => baseMaxHp + maxHpPerLevel * (level - 1);

        public int DamageAt(int level) => baseDamage + damagePerLevel * (level - 1);

        public static int XpCurve(int level, float baseXp, float exponent)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), "Levels start at 1.");
            return Mathf.RoundToInt(baseXp * Mathf.Pow(level, exponent));
        }
    }
}
