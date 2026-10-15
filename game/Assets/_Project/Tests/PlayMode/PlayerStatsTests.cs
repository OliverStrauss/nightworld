using System.Collections;
using System.Collections.Generic;
using NightWorld.Combat;
using NightWorld.Data;
using NightWorld.Player;
using NightWorld.Progression;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NightWorld.Tests
{
    /// <summary>The hero's HP, level, XP and gold, on in-memory content so real content changes don't break it.</summary>
    public class PlayerStatsTests
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
        public IEnumerator KillingAnEnemy_PaysXpAndGoldToTheHero()
        {
            var root = BuildZone(enemyCount: 1);
            var stats = SpawnPlayer(root, MakeProgressionSettings());
            yield return null; // spawners run Start()

            var enemy = root.GetComponentInChildren<Enemy>();
            Assert.IsNotNull(enemy, "the zone spawned no enemy");
            enemy.Health.TakeDamage(9999);

            Assert.AreEqual(12, stats.Progression.TotalXp);
            Assert.That(stats.Progression.Gold, Is.InRange(3, 6));
        }

        [UnityTest]
        public IEnumerator ReachingLevel2_RaisesMaxHpToMaxHpAt2_AndHealsToFull()
        {
            var settings = MakeProgressionSettings();
            var root = BuildZone(enemyCount: 0);
            var stats = SpawnPlayer(root, settings);
            yield return null;

            Assert.AreEqual(settings.MaxHpAt(1), stats.Health.Max, "the hero didn't spawn with MaxHpAt(1)");
            Assert.AreEqual(stats.Health.Max, stats.Health.Current, "the hero didn't spawn at full HP");

            stats.Health.TakeDamage(30);
            stats.Progression.AddXp(settings.XpToNext(1));

            Assert.AreEqual(2, stats.Progression.Level);
            Assert.AreEqual(settings.MaxHpAt(2), stats.Health.Max);
            Assert.AreEqual(stats.Health.Max, stats.Health.Current, "levelling up didn't heal to full");
        }

        [UnityTest]
        public IEnumerator AfterTheHeroIsDestroyed_KillingAnEnemy_ThrowsNothing_AndPaysNothing()
        {
            var root = BuildZone(enemyCount: 2);
            var stats = SpawnPlayer(root, MakeProgressionSettings());
            yield return null; // spawners run Start()

            var enemies = root.GetComponentsInChildren<Enemy>();
            Assert.AreEqual(2, enemies.Length);
            enemies[0].Health.TakeDamage(9999);

            var progression = stats.Progression;
            int xp = progression.TotalXp;
            int gold = progression.Gold;
            Assert.AreEqual(12, xp, "the first kill should still pay out");

            Object.Destroy(stats.gameObject);
            yield return null; // OnDestroy runs and drops the Enemy.AnyKilled subscription

            Assert.DoesNotThrow(() => enemies[1].Health.TakeDamage(9999));
            Assert.AreEqual(xp, progression.TotalXp, "a destroyed hero still earned XP");
            Assert.AreEqual(gold, progression.Gold, "a destroyed hero still earned gold");
        }

        private PlayerStats SpawnPlayer(GameObject zoneRoot, ProgressionSettings progressionSettings)
        {
            var marker = zoneRoot.transform.Find("PlayerSpawn");
            Assert.IsNotNull(marker, "missing PlayerSpawn marker");

            var playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            created.Add(playerSettings);

            var player = PlayerSpawner.Spawn(marker, playerSettings, progressionSettings);
            created.Add(player.gameObject);
            var stats = player.GetComponent<PlayerStats>();
            Assert.IsNotNull(stats, "PlayerSpawner didn't add PlayerStats");
            return stats;
        }

        private ProgressionSettings MakeProgressionSettings()
        {
            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            created.Add(settings);
            return settings;
        }

        /// <summary>A flat 60 m enemy zone; its slimes give 12 XP and 3–6 gold each.</summary>
        private GameObject BuildZone(int enemyCount)
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
            if (enemyCount > 0)
                zone.enemySpawns.Add(new EnemySpawn { enemy = enemy, position = new Vector3(0f, 0f, 10f), radius = 3f, count = enemyCount });
            zone.screenshotPoints.Add(new ScreenshotPoint());
            created.Add(zone);

            var root = ZoneBuilder.Build(zone);
            created.Add(root);
            return root;
        }
    }
}
