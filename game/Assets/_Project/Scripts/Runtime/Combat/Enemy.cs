using System;
using NightWorld.Data;
using UnityEngine;

namespace NightWorld.Combat
{
    /// <summary>
    /// Runtime enemy. Holds its definition and Health; announces its death on
    /// <see cref="AnyKilled"/> so other systems (rewards, quests) can react without
    /// knowing about each other. AI and attacks are future cards.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public static event Action<Enemy> AnyKilled;

        public EnemyDefinition Definition { get; private set; }
        public Health Health { get; private set; }

        public void Init(EnemyDefinition definition)
        {
            Definition = definition;
            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.Init(definition.maxHp);
            Health.Died += OnDied;
        }

        private void OnDied(Health health)
        {
            health.Died -= OnDied;
            AnyKilled?.Invoke(this);

            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
