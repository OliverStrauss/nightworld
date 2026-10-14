using System.Collections.Generic;
using NightWorld.Core;
using UnityEngine;

namespace NightWorld.Data
{
    [CreateAssetMenu(menuName = "NightWorld/Shop", fileName = "new-shop")]
    public class ShopDefinition : Definition
    {
        public List<ItemDefinition> inventory = new List<ItemDefinition>();

        [Tooltip("Fraction of baseValue the shop pays when the player sells.")]
        [Range(0f, 1f)] public float sellRatio = 0.4f;

        public int BuyPrice(ItemDefinition item) => item.baseValue;

        public int SellPrice(ItemDefinition item) => Mathf.RoundToInt(item.baseValue * sellRatio);
    }
}
