using NightWorld.Data;
using NightWorld.Progression;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// The game's one runtime data root. One asset lives at Data/game-config.asset and the Boot
    /// scene's GameBootstrap points at it, so new tuning assets are added here, not to Boot.unity.
    /// </summary>
    [CreateAssetMenu(menuName = "NightWorld/Game Config", fileName = "game-config")]
    public class GameConfig : ScriptableObject
    {
        public ZoneDefinition startZone;
        public ProgressionSettings progressionSettings;
    }
}
