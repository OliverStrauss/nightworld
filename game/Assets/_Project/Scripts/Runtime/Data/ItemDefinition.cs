using NightWorld.Core;
using UnityEngine;

namespace NightWorld.Data
{
    public enum ItemType
    {
        Weapon = 0,
        Armor = 1,
        Consumable = 2,
        Material = 3,
    }

    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
    }

    [CreateAssetMenu(menuName = "NightWorld/Item", fileName = "new-item")]
    public class ItemDefinition : Definition
    {
        public ItemType itemType;
        public Rarity rarity;

        [Tooltip("Shop buy price. Selling returns baseValue * shop.sellRatio.")]
        [Min(0)] public int baseValue = 10;

        [Header("Weapon")]
        [Tooltip("Damage dealt per hit while equipped. Only used when itemType is Weapon.")]
        [Min(0)] public int damage;

        [Header("Armor")]
        [Tooltip("Armor value added while equipped. Only used when itemType is Armor.")]
        [Min(0)] public int armor;

        [Header("Consumable")]
        [Tooltip("HP restored when used. Only used when itemType is Consumable.")]
        [Min(0)] public int healAmount;
    }
}
