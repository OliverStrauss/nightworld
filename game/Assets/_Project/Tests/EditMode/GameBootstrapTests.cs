using System.Collections.Generic;
using NightWorld.Data;
using NightWorld.World;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    public class GameBootstrapTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void StartZone_ComesFromConfig_AndFallsBackToOldFieldWithoutOne()
        {
            var oldZone = Make<ZoneDefinition>();
            var configZone = Make<ZoneDefinition>();
            var config = Make<GameConfig>();
            config.startZone = configZone;

            var go = new GameObject("Bootstrap");
            created.Add(go);
            var bootstrap = go.AddComponent<GameBootstrap>();
            bootstrap.startZone = oldZone;

            Assert.AreSame(oldZone, bootstrap.StartZone, "without a config the old startZone field is used");

            bootstrap.config = config;

            Assert.AreSame(configZone, bootstrap.StartZone, "with a config its startZone wins");
        }

        private T Make<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            created.Add(o);
            return o;
        }
    }
}
