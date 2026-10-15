using System.Collections;
using System.Collections.Generic;
using NightWorld.Data;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NightWorld.Tests
{
    /// <summary>WorldBuilder with in-memory zones, so the tests don't break when real content changes.</summary>
    public class WorldBuilderTests
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

        [Test]
        public void LinkedZones_ArePlacedAtParentOriginPlusOffset()
        {
            var town = MakeZone("test-town", 80f);
            var fields = MakeZone("test-fields", 140f);
            var hills = MakeZone("test-hills", 60f);
            town.neighbours.Add(new ZoneLink { zone = fields, offset = new Vector3(0f, 0f, 110f) });
            fields.neighbours.Add(new ZoneLink { zone = hills, offset = new Vector3(100f, 0f, 0f) });

            var world = WorldBuilder.Build(town);
            created.Add(world);

            Assert.AreEqual("World", world.name);
            Assert.AreEqual(Vector3.zero, ZoneRoot(world, "test-town").position);
            Assert.AreEqual(new Vector3(0f, 0f, 110f), ZoneRoot(world, "test-fields").position);
            Assert.AreEqual(new Vector3(100f, 0f, 110f), ZoneRoot(world, "test-hills").position);
        }

        [Test]
        public void ZoneLinkedFromTwoPlaces_IsBuiltOnce()
        {
            var town = MakeZone("test-town", 80f);
            var west = MakeZone("test-west", 80f);
            var north = MakeZone("test-north", 80f);
            town.neighbours.Add(new ZoneLink { zone = west, offset = new Vector3(-80f, 0f, 0f) });
            town.neighbours.Add(new ZoneLink { zone = north, offset = new Vector3(0f, 0f, 80f) });
            west.neighbours.Add(new ZoneLink { zone = north, offset = new Vector3(80f, 0f, 80f) });
            north.neighbours.Add(new ZoneLink { zone = town, offset = new Vector3(0f, 0f, -80f) });

            var world = WorldBuilder.Build(town);
            created.Add(world);

            Assert.AreEqual(3, world.transform.childCount);
            int norths = 0;
            foreach (Transform child in world.transform)
            {
                if (child.name == "Zone_test-north") norths++;
            }
            Assert.AreEqual(1, norths);
        }

        [UnityTest]
        public IEnumerator Bootstrap_BuildsTheWorldFromTheStartZone()
        {
            var town = MakeZone("test-town", 80f);
            var fields = MakeZone("test-fields", 140f);
            town.neighbours.Add(new ZoneLink { zone = fields, offset = new Vector3(0f, 0f, 110f) });

            var go = new GameObject("Bootstrap");
            created.Add(go);
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.startZone = town; // no config: logs a warning and uses the old field
            yield return null; // Start()

            Assert.IsNotNull(bootstrap.WorldRoot);
            created.Add(bootstrap.WorldRoot);
            Assert.AreEqual("World", bootstrap.WorldRoot.name);
            Assert.AreEqual(new Vector3(0f, 0f, 110f), ZoneRoot(bootstrap.WorldRoot, "test-fields").position);
        }

        private static Transform ZoneRoot(GameObject world, string id)
        {
            var root = world.transform.Find("Zone_" + id);
            Assert.IsNotNull(root, "missing Zone_" + id);
            return root;
        }

        private ZoneDefinition MakeZone(string id, float size)
        {
            var zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.id = id;
            zone.ground.size = new Vector2(size, size);
            zone.ground.heightAmplitude = 0f;
            zone.screenshotPoints.Add(new ScreenshotPoint());
            created.Add(zone);
            return zone;
        }
    }
}
