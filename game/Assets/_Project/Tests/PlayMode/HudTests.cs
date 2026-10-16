using System.Collections;
using System.Collections.Generic;
using NightWorld.Data;
using NightWorld.Player;
using NightWorld.Progression;
using NightWorld.UI;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NightWorld.Tests
{
    /// <summary>The HUD on in-memory stats, so real content changes don't break it.</summary>
    public class HudTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // GameBootstrap's FollowCamera locks the cursor.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            foreach (var o in created)
            {
                if (o != null) Object.Destroy(o);
            }
            created.Clear();
        }

        [UnityTest]
        public IEnumerator BoundToFreshStats_AddingHalfOfXpToNext_FillsTheXpBarHalfway()
        {
            var stats = MakeStats(MakeProgressionSettings());
            var hud = MakeHud(stats);

            stats.Progression.AddXp(stats.Progression.XpToNext / 2);
            yield return null;

            Assert.AreEqual(0.5f, hud.XpFill.fillAmount, 0.01f);
        }

        [UnityTest]
        public IEnumerator AtMaxLevel_TheXpBarIsFull()
        {
            var settings = MakeProgressionSettings();
            settings.levelCap = 2;
            var stats = MakeStats(settings);
            var hud = MakeHud(stats);

            stats.Progression.AddXp(settings.XpToNext(1));
            yield return null;

            Assert.IsTrue(stats.Progression.IsMaxLevel);
            Assert.AreEqual(1f, hud.XpFill.fillAmount, 0.01f);
        }

        [UnityTest]
        public IEnumerator WithMax100_TakingDamage25_FillsTheHpBarTo75Percent_AndShows75()
        {
            var stats = MakeStats(MakeProgressionSettings());
            var hud = MakeHud(stats);
            Assert.AreEqual(100, stats.Health.Max, "the default ProgressionSettings should give 100 max HP at level 1");

            stats.Health.TakeDamage(25);
            yield return null;

            Assert.AreEqual(0.75f, hud.HpFill.fillAmount, 0.01f);
            StringAssert.Contains("75", hud.HpText.text);
        }

        [UnityTest]
        public IEnumerator AddingGold_AndReachingLevel2_UpdatesTheGoldAndLevelText()
        {
            var stats = MakeStats(MakeProgressionSettings());
            var hud = MakeHud(stats);
            StringAssert.DoesNotContain("2", hud.LevelText.text, "a fresh hero should be level 1");

            stats.Progression.AddGold(42);
            stats.Progression.AddXp(stats.Progression.XpToNext);
            yield return null;

            Assert.AreEqual(2, stats.Progression.Level);
            StringAssert.Contains("42", hud.GoldText.text);
            StringAssert.Contains("2", hud.LevelText.text);
        }

        [UnityTest]
        public IEnumerator Bootstrap_CreatesExactlyOneOverlayCanvas_BoundToTheHeroesStats()
        {
            var zone = ScriptableObject.CreateInstance<ZoneDefinition>();
            zone.id = "test-town";
            zone.ground.size = new Vector2(40f, 40f);
            zone.ground.heightAmplitude = 0f;
            zone.screenshotPoints.Add(new ScreenshotPoint());
            created.Add(zone);
            var playerSettings = ScriptableObject.CreateInstance<PlayerSettings>();
            created.Add(playerSettings);
            var config = ScriptableObject.CreateInstance<GameConfig>();
            config.startZone = zone;
            config.playerSettings = playerSettings;
            config.progressionSettings = MakeProgressionSettings();
            created.Add(config);

            var go = new GameObject("Bootstrap");
            created.Add(go);
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.config = config;
            yield return null; // Start()

            created.Add(bootstrap.WorldRoot);
            Assert.IsNotNull(bootstrap.Player, "GameBootstrap spawned no player");
            created.Add(bootstrap.Player.gameObject);
            Assert.IsNotNull(bootstrap.Hud, "GameBootstrap created no HUD");
            created.Add(bootstrap.Hud.gameObject);

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Assert.AreEqual(1, canvases.Length, "there should be exactly one Canvas, the HUD's");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvases[0].renderMode);
            Assert.AreSame(bootstrap.Player.GetComponent<PlayerStats>(), bootstrap.Hud.Stats, "the HUD isn't bound to the hero's PlayerStats");
        }

        private PlayerStats MakeStats(ProgressionSettings settings)
        {
            var go = new GameObject("TestHero");
            created.Add(go);
            var stats = go.AddComponent<PlayerStats>();
            stats.Init(settings);
            return stats;
        }

        private Hud MakeHud(PlayerStats stats)
        {
            var hud = new GameObject("Hud").AddComponent<Hud>();
            created.Add(hud.gameObject);
            hud.Bind(stats);
            return hud;
        }

        private ProgressionSettings MakeProgressionSettings()
        {
            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            created.Add(settings);
            return settings;
        }
    }
}
