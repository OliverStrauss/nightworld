using System.Collections;
using System.Collections.Generic;
using NightWorld.Data;
using NightWorld.Player;
using NightWorld.Progression;
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
            config.progressionSettings = MakeProgressionSettings();
            created.Add(config);
            var cam = MakeMainCamera();

            var go = new GameObject("Bootstrap");
            created.Add(go);
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.config = config;
            yield return null; // Start()

            created.Add(bootstrap.WorldRoot);
            if (bootstrap.Hud != null) created.Add(bootstrap.Hud.gameObject);
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

            var stats = player.GetComponent<PlayerStats>();
            Assert.IsNotNull(stats, "GameBootstrap's hero has no PlayerStats");
            Assert.AreEqual(config.progressionSettings.MaxHpAt(1), stats.Health.Max, "the hero's HP doesn't come from config.progressionSettings");

            Assert.IsNotNull(player.GetComponent<FollowCamera>(), "GameBootstrap didn't attach a FollowCamera to the hero");
            yield return null;
            yield return null; // FollowCamera's Start and first LateUpdate
            FreeCursor();
            Assert.AreEqual(config.playerSettings.cameraDistance, Vector3.Distance(cam.transform.position, Pivot(player.transform, config.playerSettings)), 0.5f,
                $"the camera at {cam.transform.position} isn't following the hero (still on the zone's screenshot point?)");
        }

        [UnityTest]
        public IEnumerator AfterWalking10Metres_TheCameraIsCameraDistanceFromThePivot_AndLooksAtIt()
        {
            var settings = MakeSettings();
            var cam = MakeMainCamera();
            var follow = SpawnWithFollowCamera(settings);
            var player = follow.GetComponent<PlayerController>();
            yield return null;
            yield return null;
            FreeCursor();

            Vector3 start = player.transform.position;
            player.MoveInput = new Vector2(0f, 1f);
            for (int frame = 0; frame < 250 && FlatOffset(start, player.transform.position).magnitude < 10f; frame++)
                yield return null;
            player.MoveInput = Vector2.zero;
            yield return null; // the hero stands still; the camera caught up with it in the last LateUpdate

            Assert.GreaterOrEqual(FlatOffset(start, player.transform.position).magnitude, 10f, "the hero never walked 10 m");
            Vector3 toPivot = Pivot(player.transform, settings) - cam.transform.position;
            Assert.AreEqual(settings.cameraDistance, toPivot.magnitude, 0.5f, "the camera isn't cameraDistance from the pivot");
            Assert.Less(Vector3.Angle(cam.transform.forward, toPivot), 2f, "the camera doesn't look at the pivot");
        }

        [UnityTest]
        public IEnumerator LookInputWorth90DegreesOfYaw_TurnsTheCamera90DegreesAroundTheHero()
        {
            var settings = MakeSettings();
            var cam = MakeMainCamera();
            var follow = SpawnWithFollowCamera(settings);
            yield return null;
            yield return null;
            FreeCursor();

            Vector3 before = FlatOffset(follow.transform.position, cam.transform.position);
            follow.LookInput = new Vector2(1f, 0f);
            for (int frame = 0; frame < FramesToTurn(settings, 90f); frame++) yield return null;
            follow.LookInput = Vector2.zero;
            Vector3 after = FlatOffset(follow.transform.position, cam.transform.position);

            Assert.AreEqual(90f, Vector3.Angle(before, after), 2f);
        }

        [UnityTest]
        public IEnumerator WithTheCameraTurned90Degrees_ForwardWalksAlongTheCamerasFlatForward()
        {
            var settings = MakeSettings();
            var cam = MakeMainCamera();
            var follow = SpawnWithFollowCamera(settings);
            var player = follow.GetComponent<PlayerController>();
            yield return null;
            yield return null;
            FreeCursor();

            follow.LookInput = new Vector2(1f, 0f);
            for (int frame = 0; frame < FramesToTurn(settings, 90f); frame++) yield return null;
            follow.LookInput = Vector2.zero;
            Vector3 cameraForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            Assert.AreEqual(90f, Vector3.Angle(Vector3.forward, cameraForward), 2f, "the camera didn't turn away from world +z");

            Vector3 start = player.transform.position;
            player.MoveInput = new Vector2(0f, 1f);
            for (int frame = 0; frame < 25; frame++) yield return null;
            Vector3 walked = FlatOffset(start, player.transform.position);

            Assert.Greater(walked.magnitude, 1f, "the hero didn't walk");
            Assert.Less(Vector3.Angle(cameraForward, walked), 5f, $"walked {walked}, the camera looks along {cameraForward}");
        }

        [UnityTest]
        public IEnumerator LargeLookInputPitch_NeverTakesTheCameraPastPitchMaxOrPitchMin()
        {
            var settings = MakeSettings();
            var cam = MakeMainCamera();
            var follow = SpawnWithFollowCamera(settings);
            yield return null;
            yield return null;
            FreeCursor();

            // Stick down raises the camera towards pitchMax, stick up drops it towards pitchMin.
            foreach (float push in new[] { -50f, 50f })
            {
                follow.LookInput = new Vector2(0f, push);
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return null;
                    float pitch = CameraPitch(cam, follow.transform, settings);
                    Assert.LessOrEqual(pitch, settings.pitchMax + 0.5f, $"LookInput.y = {push} took the camera past pitchMax");
                    Assert.GreaterOrEqual(pitch, settings.pitchMin - 0.5f, $"LookInput.y = {push} took the camera past pitchMin");
                }
                float limit = push < 0f ? settings.pitchMax : settings.pitchMin;
                Assert.AreEqual(limit, CameraPitch(cam, follow.transform, settings), 0.5f, $"LookInput.y = {push} didn't reach the limit");
            }
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
            var player = PlayerSpawner.Spawn(marker, settings, MakeProgressionSettings());
            created.Add(player.gameObject);
            return player;
        }

        private ProgressionSettings MakeProgressionSettings()
        {
            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            created.Add(settings);
            return settings;
        }

        private PlayerSettings MakeSettings()
        {
            var settings = ScriptableObject.CreateInstance<PlayerSettings>();
            settings.moveSpeed = 7f;
            settings.turnSpeed = 720f;
            settings.gravity = -25f;
            settings.fallResetY = -20f;
            settings.cameraPivotHeight = 1.6f;
            settings.cameraDistance = 8f;
            settings.cameraStartPitch = 20f;
            settings.pitchMin = 5f;
            settings.pitchMax = 60f;
            settings.orbitSpeed = 180f;
            settings.mouseSensitivity = 0.15f;
            created.Add(settings);
            return settings;
        }

        /// <summary>The hero on a flat 60 m zone, with a FollowCamera.</summary>
        private FollowCamera SpawnWithFollowCamera(PlayerSettings settings)
        {
            var root = ZoneBuilder.Build(MakeZone("test-flat", 60f));
            created.Add(root);
            var player = Spawn(root.transform.Find("PlayerSpawn"), settings);
            return player.gameObject.AddComponent<FollowCamera>();
        }

        /// <summary>The test scene has no camera; FollowCamera drives whichever one is Camera.main.</summary>
        private Camera MakeMainCamera()
        {
            var go = new GameObject("TestCamera") { tag = "MainCamera" };
            created.Add(go);
            return go.AddComponent<Camera>();
        }

        /// <summary>FollowCamera locks the cursor in Start; free it so a real mouse can't turn the camera mid-test.</summary>
        private static void FreeCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Frames of LookInput.x = 1 that turn the camera this many degrees.</summary>
        private static int FramesToTurn(PlayerSettings settings, float degrees) =>
            Mathf.RoundToInt(degrees / (settings.orbitSpeed * FrameTime));

        private static Vector3 Pivot(Transform hero, PlayerSettings settings) =>
            hero.position + Vector3.up * settings.cameraPivotHeight;

        private static Vector3 FlatOffset(Vector3 from, Vector3 to) => Vector3.ProjectOnPlane(to - from, Vector3.up);

        /// <summary>Degrees the camera sits above the pivot.</summary>
        private static float CameraPitch(Camera cam, Transform hero, PlayerSettings settings)
        {
            Vector3 offset = cam.transform.position - Pivot(hero, settings);
            return Mathf.Asin(offset.y / offset.magnitude) * Mathf.Rad2Deg;
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
