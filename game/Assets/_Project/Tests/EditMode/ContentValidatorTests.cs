using System.Collections.Generic;
using System.Linq;
using NightWorld.Core;
using NightWorld.Data;
using NightWorld.EditorTools;
using NightWorld.Progression;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    public class ContentValidatorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        /// <summary>The real content in Assets/_Project/Data. This is the test CI relies on.</summary>
        [Test]
        public void AllProjectContent_IsValid()
        {
            var issues = ContentValidator.ValidateProject();
            var errors = issues.Where(i => i.Severity == ContentValidator.Severity.Error).ToList();

            Assert.IsEmpty(errors, "Content errors:\n" + ContentValidator.Format(errors));
        }

        [Test]
        public void MinimalValidContent_HasNoErrors()
        {
            var defs = ValidSet();

            Assert.IsEmpty(Errors(defs));
        }

        [Test]
        public void DuplicateIds_AreErrors()
        {
            var a = Item("same-id");
            var b = Item("same-id");

            AssertHasError(new Definition[] { a, b }, "duplicate id");
        }

        [Test]
        public void BadIdFormat_IsError()
        {
            AssertHasError(new Definition[] { Item("Iron Sword") }, "kebab-case");
        }

        [Test]
        public void EnemyWithoutLoot_IsError()
        {
            var enemy = Enemy("lonely-slime", null);

            AssertHasError(new Definition[] { enemy }, "lootTable is empty");
        }

        [Test]
        public void ZoneWithoutScreenshotPoints_IsError()
        {
            var zone = Zone("blank-zone");
            zone.screenshotPoints.Clear();

            AssertHasError(new Definition[] { zone }, "no screenshotPoints");
        }

        [Test]
        public void SpawnOutsideGround_IsError()
        {
            var gel = Item("gel");
            var enemy = Enemy("slime", gel);
            var zone = Zone("tiny-zone");
            zone.ground.size = new Vector2(20f, 20f);
            zone.enemySpawns.Add(new EnemySpawn { enemy = enemy, position = new Vector3(500f, 0f, 0f), count = 1 });

            AssertHasError(new Definition[] { gel, enemy, zone }, "outside the ground");
        }

        [Test]
        public void TownWithEnemies_IsError()
        {
            var gel = Item("gel");
            var enemy = Enemy("slime", gel);
            var town = Zone("quiet-town");
            town.zoneType = ZoneType.Town;
            town.enemySpawns.Add(new EnemySpawn { enemy = enemy, position = Vector3.zero, count = 1 });

            AssertHasError(new Definition[] { gel, enemy, town }, "towns must not have enemy spawns");
        }

        [Test]
        public void ValidGameConfig_HasNoErrors()
        {
            Assert.IsEmpty(ConfigErrors(Config()));
        }

        [Test]
        public void GameConfigWithMissingReferences_IsError()
        {
            var config = Config();
            config.startZone = null;
            config.progressionSettings = null;

            var errors = ConfigErrors(config);

            Assert.IsTrue(errors.Any(e => e.Contains("startZone is missing")), string.Join("\n", errors));
            Assert.IsTrue(errors.Any(e => e.Contains("progressionSettings is missing")), string.Join("\n", errors));
        }

        [Test]
        public void TwoGameConfigs_IsError()
        {
            var errors = ConfigErrors(Config(), Config());

            Assert.IsTrue(errors.Any(e => e.Contains("2 GameConfig assets")), string.Join("\n", errors));
        }

        [Test]
        public void NoGameConfig_IsError()
        {
            var errors = ConfigErrors();

            Assert.IsTrue(errors.Any(e => e.Contains("no GameConfig asset")), string.Join("\n", errors));
        }

        private static List<string> ConfigErrors(params GameConfig[] configs)
        {
            return ContentValidator.ValidateGameConfigs(configs)
                .Where(i => i.Severity == ContentValidator.Severity.Error)
                .Select(i => i.Message)
                .ToList();
        }

        private GameConfig Config()
        {
            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            created.Add(settings);
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.startZone = Zone("test-zone");
            config.progressionSettings = settings;
            created.Add(config);
            return config;
        }

        private static void AssertHasError(IEnumerable<Definition> defs, string fragment)
        {
            var errors = Errors(defs);
            Assert.IsTrue(errors.Any(e => e.Contains(fragment)),
                "Expected an error containing '" + fragment + "' but got:\n" + string.Join("\n", errors));
        }

        private static List<string> Errors(IEnumerable<Definition> defs)
        {
            return ContentValidator.Validate(defs)
                .Where(i => i.Severity == ContentValidator.Severity.Error)
                .Select(i => i.Message)
                .ToList();
        }

        private Definition[] ValidSet()
        {
            var sword = Item("test-sword");
            var enemy = Enemy("test-slime", sword);
            var shop = Make<ShopDefinition>("test-shop");
            shop.inventory.Add(sword);
            var zone = Zone("test-zone");
            zone.enemySpawns.Add(new EnemySpawn { enemy = enemy, position = new Vector3(10f, 0f, 10f), count = 2 });
            return new Definition[] { sword, enemy, shop, zone };
        }

        private ItemDefinition Item(string id)
        {
            var item = Make<ItemDefinition>(id);
            item.itemType = ItemType.Weapon;
            item.damage = 3;
            return item;
        }

        private EnemyDefinition Enemy(string id, ItemDefinition loot)
        {
            var enemy = Make<EnemyDefinition>(id);
            if (loot != null) enemy.lootTable.Add(new LootEntry { item = loot, chance = 0.5f });
            return enemy;
        }

        private ZoneDefinition Zone(string id)
        {
            var zone = Make<ZoneDefinition>(id);
            zone.screenshotPoints.Add(new ScreenshotPoint());
            return zone;
        }

        private T Make<T>(string id) where T : Definition
        {
            var def = ScriptableObject.CreateInstance<T>();
            def.id = id;
            def.displayName = id;
            created.Add(def);
            return def;
        }
    }
}
