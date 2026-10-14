using NightWorld.Data;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    public class ShopTests
    {
        [TestCase(100, 0.4f, 40)]
        [TestCase(5, 0.4f, 2)]
        [TestCase(3, 0.4f, 1)]
        [TestCase(50, 0f, 0)]
        public void SellPrice_IsBaseValueTimesSellRatio(int baseValue, float ratio, int expected)
        {
            var shop = ScriptableObject.CreateInstance<ShopDefinition>();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            shop.sellRatio = ratio;
            item.baseValue = baseValue;

            Assert.AreEqual(expected, shop.SellPrice(item));
            Assert.AreEqual(baseValue, shop.BuyPrice(item));

            Object.DestroyImmediate(shop);
            Object.DestroyImmediate(item);
        }
    }
}
