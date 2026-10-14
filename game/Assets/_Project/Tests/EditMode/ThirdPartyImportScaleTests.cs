using NightWorld.EditorTools;
using NUnit.Framework;

namespace NightWorld.Tests
{
    public class ThirdPartyImportScaleTests
    {
        [TestCase("Assets/ThirdParty/KenneyNature/tree_default.fbx", 3f)]
        [TestCase("Assets/ThirdParty/KenneyFantasyTown/wall.fbx", 3f)]
        [TestCase("Assets/ThirdParty/SomeOtherPack/thing.fbx", 1f)]
        public void ScaleFor_UsesPackScaleOrOne(string path, float expected)
        {
            Assert.AreEqual(expected, ThirdPartyImportScale.ScaleFor(path));
        }
    }
}
