using System.Collections.Generic;
using System.Linq;
using NightWorld.Core;
using NightWorld.Data;
using NightWorld.EditorTools;
using NightWorld.Player;
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

        [TestCase(-1f)]
        [TestCase(21f)] // ground is 80 x 40, so half the shorter side is 20
        public void EdgeBlendOutOfRange_IsError(float edgeBlend)
        {
            var zone = Zone("blend-zone");
            zone.ground.size = new Vector2(80f, 40f);
            zone.ground.edgeBlend = edgeBlend;

            AssertHasError(new Definition[] { zone }, "edgeBlend");
        }

        [Test]
        public void EdgeBlendOfHalfTheShorterSide_HasNoErrors()
        {
            var zone = Zone("blend-zone");
            zone.ground.size = new Vector2(80f, 40f);
            zone.ground.edgeBlend = 20f;

            Assert.IsEmpty(Errors(new Definition[] { zone }));
        }

        [Test]
        public void TouchingLinkedZones_HaveNoErrors()
        {
            var town = LinkedPair(new Vector3(0f, 0f, 110f), out var fields);

            Assert.IsEmpty(Errors(new Definition[] { town, fields }));
        }

        [Test]
        public void LinkWithoutZone_IsError()
        {
            var town = Zone("test-town");
            town.neighbours.Add(new ZoneLink { zone = null, offset = new Vector3(0f, 0f, 100f) });

            AssertHasError(new Definition[] { town }, "has no zone");
        }

        [Test]
        public void LinkToItself_IsError()
        {
            var town = Zone("test-town");
            town.neighbours.Add(new ZoneLink { zone = town, offset = new Vector3(0f, 0f, 100f) });

            AssertHasError(new Definition[] { town }, "links the zone to itself");
        }

        [Test]
        public void LinkWithVerticalOffset_IsError()
        {
            var town = LinkedPair(new Vector3(0f, 5f, 110f), out var fields);

            AssertHasError(new Definition[] { town, fields }, "offset.y");
        }

        [Test]
        public void LinkedGroundsOverlapping_IsError()
        {
            var town = LinkedPair(new Vector3(0f, 0f, 100f), out var fields);

            AssertHasError(new Definition[] { town, fields }, "overlaps");
        }

        [Test]
        public void LinkedGroundsWithAGap_IsError()
        {
            var town = LinkedPair(new Vector3(0f, 0f, 111f), out var fields);

            AssertHasError(new Definition[] { town, fields }, "doesn't touch");
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
            config.playerSettings = null;

            var errors = ConfigErrors(config);

            Assert.IsTrue(errors.Any(e => e.Contains("startZone is missing")), string.Join("\n", errors));
            Assert.IsTrue(errors.Any(e => e.Contains("progressionSettings is missing")), string.Join("\n", errors));
            Assert.IsTrue(errors.Any(e => e.Contains("playerSettings is missing")), string.Join("\n", errors));
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
            var player = ScriptableObject.CreateInstance<PlayerSettings>();
            created.Add(player);
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.startZone = Zone("test-zone");
            config.progressionSettings = settings;
            config.playerSettings = player;
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

        /// <summary>An 80 m town linking a 140 m field at offset; (0, 0, 110) makes the grounds touch at z = 40.</summary>
        private ZoneDefinition LinkedPair(Vector3 offset, out ZoneDefinition fields)
        {
            var town = Zone("test-town");
            town.ground.size = new Vector2(80f, 80f);
            fields = Zone("test-fields");
            fields.ground.size = new Vector2(140f, 140f);
            town.neighbours.Add(new ZoneLink { zone = fields, offset = offset });
            return town;
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
