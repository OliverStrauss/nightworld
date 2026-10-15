using UnityEngine;

namespace NightWorld.Player
{
    /// <summary>
    /// Tunable numbers for walking around. One asset lives at Data/player-settings.asset and
    /// game-config points at it.
    /// </summary>
    [CreateAssetMenu(menuName = "NightWorld/Player Settings", fileName = "player-settings")]
    public class PlayerSettings : ScriptableObject
    {
        [Tooltip("Walking speed in metres per second.")]
        [Min(0f)] public float moveSpeed = 7f;

        [Tooltip("How fast the hero turns to face where it walks, in degrees per second.")]
        [Min(0f)] public float turnSpeed = 720f;

        [Tooltip("Downward acceleration in metres per second squared. Negative.")]
        public float gravity = -25f;

        [Tooltip("Below this world height the hero is put back on the start zone's PlayerSpawn.")]
        public float fallResetY = -20f;
    }
}
