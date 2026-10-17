using System.Collections;
using System.Collections.Generic;
using NightWorld.Data;
using NightWorld.Input;
using NightWorld.Player;
using NightWorld.Progression;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace NightWorld.Tests
{
    /// <summary>The hero and its camera driven by virtual devices through GameControls.
    /// InputTestFixture swaps in a clean input system, so a real keyboard or mouse can't interfere.</summary>
    public class GameControlsInputTests : InputTestFixture
    {
        private readonly List<Object> created = new List<Object>();
        private readonly List<GameControls> controls = new List<GameControls>();
        private Keyboard keyboard;

        public override void Setup()
        {
            base.Setup();
            // CI runs the editor headless with no focused Game view. By default the Input System then sends
            // keyboard and mouse events to editor updates, where gameplay actions never see them. These two
            // settings (reset by the fixture in TearDown) send every virtual device's input to the game.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
            // Clean up before the fixture restores the real input system. DestroyImmediate so every
            // OnDestroy (which unsubscribes from the actions) runs now, not at the end of the frame.
            foreach (var o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
            foreach (var c in controls) c.Dispose();
            controls.Clear();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator HoldingW_ThroughGameControls_SetsMoveInputToForward()
        {
            var game = MakeControls();
            var player = Spawn(game);
            yield return null;

            Press(keyboard.wKey);

            Assert.AreEqual(new Vector2(0f, 1f), player.MoveInput, "W didn't reach PlayerController through GameControls.Move");
            yield return null;
            Assert.AreEqual(new Vector2(0f, 1f), player.MoveInput, "MoveInput didn't stay forward while W is held");

            Release(keyboard.wKey);

            Assert.AreEqual(Vector2.zero, player.MoveInput, "releasing W didn't stop the hero");
        }

        [UnityTest]
        public IEnumerator PressingEscape_WhileTheCursorIsLocked_FreesTheCursorViaMenu()
        {
            var game = MakeControls();
            var follow = Spawn(game).gameObject.AddComponent<FollowCamera>();
            follow.SetControls(game);
            yield return null; // FollowCamera.Start locks the cursor

            // FollowCamera's own lock state, not Cursor.lockState: a headless player can't lock the OS cursor.
            Assert.IsTrue(follow.CursorLocked, "precondition: FollowCamera.Start should lock the cursor");

            Press(keyboard.escapeKey);
            yield return null;

            Assert.IsFalse(follow.CursorLocked, "Escape (the Menu action) didn't free the cursor");
        }

        [UnityTest]
        public IEnumerator RightStick_OnLook_SetsLookInput_AsARate()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var game = MakeControls();
            var follow = Spawn(game).gameObject.AddComponent<FollowCamera>();
            follow.SetControls(game);
            yield return null;

            Set(gamepad.rightStick, new Vector2(1f, 0f));

            Assert.AreEqual(1f, follow.LookInput.x, 0.001f, "the right stick on Look didn't reach FollowCamera.LookInput");
            Assert.AreEqual(0f, follow.LookInput.y, 0.001f);

            Set(gamepad.rightStick, Vector2.zero);

            Assert.AreEqual(Vector2.zero, follow.LookInput, "letting go of the stick didn't stop the camera");
        }

        [Test]
        public void Turn_StickIsARate_ScaledByOrbitSpeedAndDeltaTime()
        {
            var settings = MakePlayerSettings();
            settings.orbitSpeed = 90f;

            Vector2 turn = FollowCamera.Turn(new Vector2(1f, -0.5f), Vector2.zero, true, settings, 0.5f);

            Assert.AreEqual(45f, turn.x, 0.0001f);
            Assert.AreEqual(-22.5f, turn.y, 0.0001f);
        }

        [Test]
        public void Turn_MouseIsPerFrame_ScaledByMouseSensitivity_OnlyWhileTheCursorIsLocked()
        {
            var settings = MakePlayerSettings();
            settings.orbitSpeed = 90f;
            settings.mouseSensitivity = 0.2f;

            Vector2 locked = FollowCamera.Turn(Vector2.zero, new Vector2(10f, 5f), true, settings, 0.5f);
            Vector2 lockedOtherFrameTime = FollowCamera.Turn(Vector2.zero, new Vector2(10f, 5f), true, settings, 0.01f);
            Vector2 unlocked = FollowCamera.Turn(Vector2.zero, new Vector2(10f, 5f), false, settings, 0.5f);

            Assert.AreEqual(2f, locked.x, 0.0001f);
            Assert.AreEqual(1f, locked.y, 0.0001f);
            Assert.AreEqual(locked, lockedOtherFrameTime, "a mouse delta must not scale with frame time");
            Assert.AreEqual(Vector2.zero, unlocked, "the mouse turned the camera while the cursor was free");
        }

        [UnityTest]
        public IEnumerator Bootstrap_CreatesEnabledControls_GivesThemToTheHeroAndCamera_AndDisposesThemOnDestroy()
        {
            var zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.id = "test-town";
            zone.ground.size = new Vector2(40f, 40f);
            zone.ground.heightAmplitude = 0f;
            zone.screenshotPoints.Add(new ScreenshotPoint());
            created.Add(zone);

            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.startZone = zone;
            config.playerSettings = MakePlayerSettings();
            config.progressionSettings = MakeProgressionSettings();
            config.controlSettings = MakeControlSettings();
            created.Add(config);

            var go = new GameObject("Bootstrap");
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.config = config;
            yield return null; // Start()

            // Created after Start, so TearDown destroys them before the bootstrap if the test fails early.
            created.Insert(0, bootstrap.WorldRoot);
            if (bootstrap.Hud != null) created.Insert(0, bootstrap.Hud.gameObject);
            if (bootstrap.Player != null) created.Insert(0, bootstrap.Player.gameObject);
            created.Add(go);

            var game = bootstrap.Controls;
            Assert.IsNotNull(game, "GameBootstrap made no GameControls from config.controlSettings");
            Assert.IsTrue(game.Map.enabled, "GameBootstrap didn't enable its GameControls");
            Assert.IsNotNull(bootstrap.Player, "GameBootstrap spawned no player");
            Assert.AreSame(game, bootstrap.Player.Controls, "the hero didn't get the bootstrap's GameControls");
            var follow = bootstrap.Player.GetComponent<FollowCamera>();
            Assert.IsNotNull(follow, "GameBootstrap didn't attach a FollowCamera");
            Assert.AreSame(game, follow.Controls, "the FollowCamera didn't get the bootstrap's GameControls");

            Press(keyboard.wKey);
            Assert.AreEqual(new Vector2(0f, 1f), bootstrap.Player.MoveInput, "W didn't reach the bootstrap's hero");
            Release(keyboard.wKey);

            Object.DestroyImmediate(go);
            Assert.IsNull(bootstrap.Controls, "GameBootstrap didn't dispose its GameControls in OnDestroy");
        }

        private PlayerController Spawn(GameControls game)
        {
            var marker = new GameObject("PlayerSpawn");
            created.Add(marker);
            var player = PlayerSpawner.Spawn(marker.transform, MakePlayerSettings(), MakeProgressionSettings(), game);
            // Before the marker, so the hero is gone before its spawn point.
            created.Insert(0, player.gameObject);
            return player;
        }

        /// <summary>Controls built from a test ControlSettings, so these tests don't break when the real bindings change.</summary>
        private GameControls MakeControls()
        {
            var game = new GameControls(MakeControlSettings());
            controls.Add(game);
            game.Enable();
            return game;
        }

        private ControlSettings MakeControlSettings()
        {
            var settings = ScriptableObject.CreateInstance<ControlSettings>();
            settings.move.Add(new ControlBinding
            {
                kind = ControlBindingKind.Vector2Composite,
                up = "<Keyboard>/w",
                down = "<Keyboard>/s",
                left = "<Keyboard>/a",
                right = "<Keyboard>/d",
            });
            settings.look.Add(new ControlBinding { path = "<Gamepad>/rightStick" });
            settings.look.Add(new ControlBinding { path = "<Mouse>/delta" });
            settings.menu.Add(new ControlBinding { path = "<Keyboard>/escape" });
            created.Add(settings);
            return settings;
        }

        private PlayerSettings MakePlayerSettings()
        {
            var settings = ScriptableObject.CreateInstance<PlayerSettings>();
            created.Add(settings);
            return settings;
        }

        private ProgressionSettings MakeProgressionSettings()
        {
            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            created.Add(settings);
            return settings;
        }
    }
}
