using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    /// <summary>Keys and buttons live in Data/control-settings.asset and reach gameplay through GameControls.</summary>
    public class RuntimeBindingsTests
    {
        private static readonly string[] Forbidden =
        {
            "<Keyboard>", "<Mouse>", "<Gamepad>", "Keyboard.current", "Mouse.current", "Gamepad.current",
        };

        [Test]
        public void RuntimeScripts_HaveNoHardCodedBindings()
        {
            string runtime = Path.Combine(Application.dataPath, "_Project", "Scripts", "Runtime");
            Assert.IsTrue(Directory.Exists(runtime), "no folder at " + runtime);

            string[] files = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files, "no scripts found under " + runtime);

            var hits = new List<string>();
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                foreach (string word in Forbidden)
                {
                    if (text.Contains(word)) hits.Add(Path.GetFileName(file) + " contains " + word);
                }
            }

            Assert.IsEmpty(hits, "Hard-coded input in runtime scripts; read it from GameControls instead:\n" + string.Join("\n", hits));
        }
    }
}
