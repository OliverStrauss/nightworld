using System.Collections;
using System.Collections.Generic;
using NightWorld.Combat;
using NightWorld.Data;
using NightWorld.Progression;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NightWorld.Tests
{
    /// <summary>
    /// End-to-end checks of the core loop with in-memory content, so they don't break when
    /// real content changes. When the player controller exists, add a bot test here that walks
    /// into a zone and fights instead of calling TakeDamage directly.
    /// </summary>
    public class ZoneLoopTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
            {
                if (o != null) Object.Destroy(o);
            }
            created.Clear();
        }

        [UnityTest]
        public IEnumerator Spawner_SpawnsConfiguredCount_OnTheGround_WithinRadius()
        {
            var zone = MakeZone(count: 3, radius: 4f, center: new Vector3(5f, 0f, 10f));
            zone.ground.heightAmplitude = 3f;

            var root = ZoneBuilder.Build(zone);
            created.Add(root);
            yield return null; // spawners run Start()

            var enemies = root.GetComponentsInChildren<Enemy>();
            Assert.AreEqual(3, enemies.Length);

            foreach (var e in enemies)
            {
                Vector3 p = e.transform.localPosition;
                float flatDistance = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(5f, 10f));
                Assert.LessOrEqual(flatDistance, 4.01f, $"{e.name} spawned outside the radius");

                float ground = ZoneBuilder.SampleGroundHeight(zone.ground, p.x, p.z);
                Assert.AreEqual(ground + 1f, p.y, 0.01f, $"{e.name} isn't standing on the ground");
            }
        }

        [UnityTest]
        public IEnumerator KillingAnEnemy_GrantsXpAndGold_AndRemovesIt()
        {
            var zone = MakeZone(count: 2, radius: 3f, center: new Vector3(0f, 0f, 10f));
            var root = ZoneBuilder.Build(zone);
            created.Add(root);
            yield return null;

            var enemies = root.GetComponentsInChildren<Enemy>();
            Assert.AreEqual(2, enemies.Length);

            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            created.Add(settings);
            var progression = new PlayerProgression(settings);

            using (new RewardSystem(progression, seed: 1))
            {
                enemies[0].Health.TakeDamage(9999);
            }
            yield return null; // let Destroy() happen

            Assert.AreEqual(12, progression.TotalXp);
            Assert.That(progression.Gold, Is.InRange(3, 6));
            Assert.AreEqual(1, root.GetComponentsInChildren<Enemy>().Length);
        }

        private ZoneDefinition MakeZone(int count, float radius, Vector3 center)
        {
            var gel = ScriptableObject.CreateInstance<ItemDefinition>();
            gel.id = "test-gel";
            created.Add(gel);

            var enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemy.id = "test-slime";
            enemy.maxHp = 10;
            enemy.xpReward = 12;
            enemy.goldMin = 3;
            enemy.goldMax = 6;
            enemy.lootTable.Add(new LootEntry { item = gel, chance = 0.5f });
            created.Add(enemy);

            var zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.id = "test-zone";
            zone.zoneType = ZoneType.Enemy;
            zone.ground.size = new Vector2(60f, 60f);
            zone.ground.heightAmplitude = 0f;
            zone.enemySpawns.Add(new EnemySpawn { enemy = enemy, position = center, radius = radius, count = count });
            zone.screenshotPoints.Add(new ScreenshotPoint());
            created.Add(zone);
            return zone;
        }
    }
}
