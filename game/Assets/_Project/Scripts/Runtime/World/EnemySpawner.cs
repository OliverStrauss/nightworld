using System;
using System.Collections.Generic;
using NightWorld.Combat;
using NightWorld.Data;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Spawns a fixed number of enemies in a circle on the terrain. Positions are seeded so the
    /// same zone always looks the same, which keeps CI screenshots comparable night to night.
    /// Respawning is a future card.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private EnemyDefinition enemy;
        [SerializeField] private Vector3 center;
        [SerializeField] private float radius;
        [SerializeField] private int count;
        [SerializeField] private int seed;

        private GroundSettings ground;
        private Transform zoneRoot;
        private bool hasSpawned;
        private readonly List<Enemy> spawned = new List<Enemy>();

        public EnemyDefinition EnemyDefinition => enemy;
        public IReadOnlyList<Enemy> Spawned => spawned;

        public void Configure(EnemyDefinition enemy, Vector3 center, float radius, int count,
            GroundSettings ground, Transform zoneRoot, int seed)
        {
            this.enemy = enemy;
            this.center = center;
            this.radius = radius;
            this.count = count;
            this.ground = ground;
            this.zoneRoot = zoneRoot;
            this.seed = seed;
        }

        private void Start()
        {
            SpawnAll();
        }

        /// <summary>Spawns every enemy now. Safe to call in edit mode (used by the screenshotter).</summary>
        public void SpawnAll()
        {
            if (hasSpawned || enemy == null) return;
            hasSpawned = true;

            Transform parent = zoneRoot != null ? zoneRoot : transform.parent;
            var rng = new System.Random(seed);

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dist = radius * Mathf.Sqrt((float)rng.NextDouble());
                var local = new Vector3(center.x + Mathf.Cos(angle) * dist, 0f, center.z + Mathf.Sin(angle) * dist);

                GameObject go;
                float yOffset;
                if (enemy.prefab != null)
                {
                    go = Instantiate(enemy.prefab);
                    yOffset = 0f;
                }
                else
                {
                    go = ZoneBuilder.CreatePlaceholder(PlaceholderShape.Capsule, enemy.placeholderColor);
                    go.transform.localScale = enemy.placeholderScale;
                    yOffset = ZoneBuilder.PrimitiveHalfHeight(PlaceholderShape.Capsule) * enemy.placeholderScale.y;
                }

                go.name = $"{enemy.id}_{i}";
                go.transform.SetParent(parent, false);
                go.transform.localPosition = ground != null
                    ? ZoneBuilder.SnapToGround(ground, local, yOffset)
                    : new Vector3(local.x, yOffset, local.z);
                go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);

                var e = go.GetComponent<Enemy>();
                if (e == null) e = go.AddComponent<Enemy>();
                e.Init(enemy);
                spawned.Add(e);
            }
        }
    }
}
