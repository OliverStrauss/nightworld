using System.Collections.Generic;
using NightWorld.Core;
using UnityEngine;

namespace NightWorld.Data
{
    [System.Serializable]
    public class LootEntry
    {
        public ItemDefinition item;

        [Tooltip("Chance (0-1) this item drops when the enemy dies.")]
        [Range(0f, 1f)] public float chance = 0.25f;
    }

    [CreateAssetMenu(menuName = "NightWorld/Enemy", fileName = "new-enemy")]
    public class EnemyDefinition : Definition
    {
        [Header("Stats")]
        [Min(1)] public int maxHp = 20;
        [Min(0)] public int damage = 3;
        [Min(0.1f)] public float attackCooldown = 1.5f;
        [Min(0f)] public float moveSpeed = 2f;

        [Header("Rewards")]
        [Min(0)] public int xpReward = 10;
        [Min(0)] public int goldMin = 1;
        [Min(0)] public int goldMax = 3;
        public List<LootEntry> lootTable = new List<LootEntry>();

        [Header("Visuals")]
        [Tooltip("Model prefab (pivot at the feet). Leave empty to use a coloured capsule placeholder.")]
        public GameObject prefab;
        public Color placeholderColor = new Color(0.4f, 0.8f, 0.3f, 1f);
        public Vector3 placeholderScale = Vector3.one;
    }
}
