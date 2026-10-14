using UnityEngine;

namespace NightWorld.Core
{
    /// <summary>
    /// Base class for every piece of game content (items, enemies, shops, zones).
    /// The <see cref="id"/> is the stable key used by saves and by name lookups:
    /// lowercase kebab-case, unique across all content, and never renamed.
    /// </summary>
    public abstract class Definition : ScriptableObject
    {
        [Tooltip("Stable lowercase kebab-case ID, e.g. iron-sword. Unique across all content. Never rename.")]
        public string id;

        [Tooltip("Name shown to the player.")]
        public string displayName;
    }
}
