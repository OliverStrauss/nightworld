using System.Collections.Generic;
using System.Linq;
using NightWorld.Input;
using NightWorld.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace NightWorld.Tests
{
    public class GameControlsTests
    {
        private const string ControlSettingsPath = "Assets/_Project/Data/control-settings.asset";
        private const string GameConfigPath = "Assets/_Project/Data/game-config.asset";

        private readonly List<GameControls> controls = new List<GameControls>();

        [TearDown]
        public void TearDown()
        {
            foreach (var c in controls) c.Dispose();
            controls.Clear();
        }

        [Test]
        public void RealControlSettings_GiveFourActions_EachWithABinding()
        {
            var game = Build(LoadSettings());

            Assert.AreEqual(GameControls.MapName, game.Map.name);
            Assert.AreEqual(4, game.Map.actions.Count);

            var actions = new[] { game.Move, game.Look, game.Inventory, game.Menu };
            var names = new[] { "Move", "Look", "Inventory", "Menu" };
            for (int i = 0; i < actions.Length; i++)
            {
                Assert.IsNotNull(actions[i], names[i] + " is null");
                Assert.AreEqual(names[i], actions[i].name);
                Assert.AreSame(actions[i], game.Map.FindAction(names[i]), names[i] + " is not in the player map");
                Assert.GreaterOrEqual(actions[i].bindings.Count, 1, names[i] + " has no bindings");
            }
        }

        [Test]
        public void InventoryAndMenu_HaveTheDefaultPaths()
        {
            var game = Build(LoadSettings());

            CollectionAssert.AreEqual(new[] { "<Keyboard>/i", "<Gamepad>/buttonNorth" }, Paths(game.Inventory));
            CollectionAssert.AreEqual(new[] { "<Keyboard>/escape", "<Gamepad>/start" }, Paths(game.Menu));
        }

        [Test]
        public void Move_HasWasdAndArrowCompositesAndTheLeftStick()
        {
            var game = Build(LoadSettings());
            var bindings = game.Move.bindings;

            // Two composites (1 + 4 parts each) plus the stick.
            Assert.AreEqual(11, bindings.Count);
            Assert.IsTrue(bindings[0].isComposite);
            Assert.AreEqual("<Keyboard>/w", bindings[1].path);
            Assert.IsTrue(bindings[5].isComposite);
            Assert.AreEqual("<Keyboard>/upArrow", bindings[6].path);
            Assert.AreEqual("<Gamepad>/leftStick", bindings[10].path);
        }

        [Test]
        public void TwoGameControls_FromTheSameSettings_HaveIdenticalBindingIds()
        {
            var settings = LoadSettings();
            var first = Build(settings);
            var second = Build(settings);

            var firstIds = first.Map.bindings.Select(b => b.id).ToList();
            var secondIds = second.Map.bindings.Select(b => b.id).ToList();

            Assert.IsNotEmpty(firstIds);
            CollectionAssert.AreEqual(firstIds, secondIds);
            CollectionAssert.AllItemsAreUnique(firstIds);
        }

        [Test]
        public void BindingIds_ComeFromActionNameAndIndex()
        {
            var game = Build(LoadSettings());

            foreach (var action in game.Map.actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                    Assert.AreEqual(GameControls.StableBindingId(action.name, i), action.bindings[i].id, $"{action.name} binding {i}");
            }
        }

        [Test]
        public void GameConfig_ReferencesTheControlSettingsAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);

            Assert.IsNotNull(config, "no GameConfig at " + GameConfigPath);
            Assert.AreSame(LoadSettings(), config.controlSettings);
        }

        private GameControls Build(ControlSettings settings)
        {
            var game = new GameControls(settings);
            controls.Add(game);
            return game;
        }

        private static ControlSettings LoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ControlSettings>(ControlSettingsPath);
            Assert.IsNotNull(settings, "no ControlSettings at " + ControlSettingsPath);
            return settings;
        }

        private static string[] Paths(InputAction action) => action.bindings.Select(b => b.path).ToArray();
    }
}
