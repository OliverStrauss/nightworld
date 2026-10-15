using System.Collections;
using System.Collections.Generic;
using NightWorld.Data;
using NightWorld.Player;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NightWorld.Tests
{
    /// <summary>The hero on in-memory zones, so the tests don't break when real content changes.</summary>
    public class PlayerControllerTests
    {
        /// <summary>Every frame advances game time by exactly this much, so "1 s" is always 50 frames.</summary>
        private const float FrameTime = 0.02f;

        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = FrameTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (var o in created)
            {
                if (o != null) Object.Destroy(o);
            }
            created.Clear();
        }

        [UnityTest]
        public IEnumerator Bootstrap_SpawnsABlueCapsuleHero_OnTheStartZonesPlayerSpawn()
        {
            var zone = MakeZone("test-town", 40f);
            zone.playerSpawn = new Vector3(5f, 0f, -5f);
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.startZone = zone;
            config.playerSettings = MakeSettings();
            created.Add(config);

            var go = new GameObject("Bootstrap");
            created.Add(go);
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.config = config;
            yield return null; // Start()

            created.Add(bootstrap.WorldRoot);
            var player = bootstrap.Player;
            Assert.IsNotNull(player, "GameBootstrap spawned no player");
            created.Add(player.gameObject);

            var marker = bootstrap.WorldRoot.transform.Find("Zone_test-town/PlayerSpawn");
            Assert.AreSame(marker, player.spawnPoint);
            Assert.Less(Vector3.Distance(marker.position, player.transform.position), 0.2f, "player isn't standing on PlayerSpawn");
            Assert.AreEqual("Player", player.tag);

            var body = player.GetComponent<CharacterController>();
            Assert.AreEqual(1.8f, body.height, 0.001f);
            Assert.AreEqual(0.4f, body.radius, 0.001f);

            Color color = player.GetComponentInChildren<MeshRenderer>().sharedMaterial.color;
            Assert.Greater(color.b, 0.8f, "the hero should be bright blue");
            Assert.Less(color.r, 0.5f, "red or purple means danger, the hero must not look like that");
        }

        [UnityTest]
        public IEnumerator WalkingNorthForOneSecond_CoversMoveSpeed_WithFeetOnTheGround()
        {
            var zone = MakeZone("test-flat", 40f);
            var root = ZoneBuilder.Build(zone);
            created.Add(root);
            var settings = MakeSettings();
            var player = Spawn(root.transform.Find("PlayerSpawn"), settings);

            Vector3 start = player.transform.position;
            player.MoveInput = new Vector2(0f, 1f);
            float startTime = Time.time;
            while (Time.time - startTime < 1f)
            {
                yield return null;
                Vector3 p = player.transform.position;
                float ground = ZoneBuilder.SampleGroundHeight(zone.ground, p.x, p.z);
                Assert.AreEqual(ground, p.y, 0.2f, $"feet left the ground at z = {p.z}");
            }

            float distance = player.transform.position.z - start.z;
            Assert.That(distance, Is.InRange(0.8f * settings.moveSpeed, 1.2f * settings.moveSpeed));
            Assert.AreEqual(start.x, player.transform.position.x, 0.01f, "walking north drifted sideways");
        }

        [UnityTest]
        public IEnumerator WalkingEast_TurnsTheHeroToFaceEast()
        {
            var root = ZoneBuilder.Build(MakeZone("test-flat", 40f));
            created.Add(root);
            var player = Spawn(root.transform.Find("PlayerSpawn"), MakeSettings());

            player.MoveInput = new Vector2(1f, 0f);
            for (int frame = 0; frame < 15; frame++) yield return null; // 0.3 s; a 90° turn takes 0.125 s at 720°/s

            Assert.Greater(Vector3.Dot(player.transform.forward, Vector3.right), 0.99f);
        }

        [UnityTest]
        public IEnumerator WalkingNorth_CrossesFromTheFirstZoneOntoTheLinkedOne_WithoutFalling()
        {
            // Two 20 m zones; the border is at z = 10 and the player starts at z = 0.
            var town = MakeZone("test-town", 20f);
            var fields = MakeZone("test-fields", 20f);
            town.neighbours.Add(new ZoneLink { zone = fields, offset = new Vector3(0f, 0f, 20f) });
            var world = WorldBuilder.Build(town);
            created.Add(world);
            var player = Spawn(world.transform.Find("Zone_test-town/PlayerSpawn"), MakeSettings());

            player.MoveInput = new Vector2(0f, 1f);
            float lowest = player.transform.position.y;
            for (int frame = 0; frame < 250 && player.transform.position.z < 15f; frame++)
            {
                yield return null;
                lowest = Mathf.Min(lowest, player.transform.position.y);
            }
            player.MoveInput = Vector2.zero;

            Assert.GreaterOrEqual(lowest, -1f, "the player fell at the border");
            Assert.GreaterOrEqual(player.transform.position.z, 15f, "the player never got 5 m into the second zone");
            Assert.IsTrue(Physics.Raycast(player.transform.position + Vector3.up, Vector3.down, out var hit, 3f), "no ground under the player");
            Assert.IsTrue(hit.collider.transform.IsChildOf(world.transform.Find("Zone_test-fields")),
                $"standing on '{hit.collider.name}', not on test-fields' ground");
        }

        [UnityTest]
        public IEnumerator FallingBelowFallResetY_PutsThePlayerBackOnPlayerSpawn_AfterOnePhysicsStep()
        {
            var zone = MakeZone("test-flat", 40f);
            zone.playerSpawn = new Vector3(5f, 0f, -5f);
            var root = ZoneBuilder.Build(zone);
            created.Add(root);
            var marker = root.transform.Find("PlayerSpawn");
            var player = Spawn(marker, MakeSettings());
            yield return null;

            player.Teleport(new Vector3(30f, -25f, 30f)); // off the ground and below fallResetY (-20)
            yield return new WaitForFixedUpdate();

            Assert.Less(Vector3.Distance(marker.position, player.transform.position), 0.5f,
                $"player is at {player.transform.position}, PlayerSpawn is at {marker.position}");
        }

        private PlayerController Spawn(Transform marker, PlayerSettings settings)
        {
            Assert.IsNotNull(marker, "missing PlayerSpawn marker");
            var player = PlayerSpawner.Spawn(marker, settings);
            created.Add(player.gameObject);
            return player;
        }

        private PlayerSettings MakeSettings()
        {
            var settings = ScriptableObject.CreateInstance<PlayerSettings>();
            settings.moveSpeed = 7f;
            settings.turnSpeed = 720f;
            settings.gravity = -25f;
            settings.fallResetY = -20f;
            created.Add(settings);
            return settings;
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
