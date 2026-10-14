using NightWorld.Progression;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    public class ProgressionTests
    {
        private ProgressionSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            settings.levelCap = 20;
            settings.baseXp = 50f;
            settings.exponent = 1.6f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void XpToNext_Level1_EqualsBaseXp()
        {
            Assert.AreEqual(50, settings.XpToNext(1));
        }

        [Test]
        public void XpToNext_IncreasesEveryLevel()
        {
            for (int level = 1; level < settings.levelCap; level++)
            {
                Assert.Greater(settings.XpToNext(level + 1), settings.XpToNext(level), $"level {level}");
            }
        }

        [Test]
        public void AddXp_EnoughForOneLevel_LevelsUpAndKeepsRemainder()
        {
            var p = new PlayerProgression(settings);

            int gained = p.AddXp(60);

            Assert.AreEqual(1, gained);
            Assert.AreEqual(2, p.Level);
            Assert.AreEqual(10, p.Xp);
            Assert.AreEqual(60, p.TotalXp);
        }

        [Test]
        public void AddXp_LargeAmount_LevelsUpSeveralTimes()
        {
            var p = new PlayerProgression(settings);
            int needed = settings.XpToNext(1) + settings.XpToNext(2) + settings.XpToNext(3);
            int events = 0;
            p.LeveledUp += _ => events++;

            p.AddXp(needed);

            Assert.AreEqual(4, p.Level);
            Assert.AreEqual(0, p.Xp);
            Assert.AreEqual(3, events);
        }

        [Test]
        public void AddXp_StopsAtLevelCap()
        {
            var p = new PlayerProgression(settings);

            p.AddXp(10_000_000);

            Assert.AreEqual(settings.levelCap, p.Level);
            Assert.IsTrue(p.IsMaxLevel);
            Assert.AreEqual(0, p.Xp);
            Assert.AreEqual(0, p.XpToNext);
        }

        [Test]
        public void TrySpendGold_NotEnough_ReturnsFalseAndKeepsGold()
        {
            var p = new PlayerProgression(settings);
            p.AddGold(10);

            Assert.IsFalse(p.TrySpendGold(11));
            Assert.AreEqual(10, p.Gold);
            Assert.IsTrue(p.TrySpendGold(10));
            Assert.AreEqual(0, p.Gold);
        }
    }
}
