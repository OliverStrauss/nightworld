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

        [Header("Follow camera")]
        [Tooltip("The camera orbits a point this many metres above the hero's feet.")]
        [Min(0f)] public float cameraPivotHeight = 1.6f;

        [Tooltip("Metres from the pivot to the camera.")]
        [Min(0.5f)] public float cameraDistance = 8f;

        [Tooltip("Camera pitch when play starts, in degrees above the pivot. Clamped to pitchMin..pitchMax.")]
        public float cameraStartPitch = 20f;

        [Tooltip("Lowest camera pitch in degrees. Keep above 0 so the camera stays above the hero.")]
        public float pitchMin = 5f;

        [Tooltip("Highest camera pitch in degrees.")]
        public float pitchMax = 60f;

        [Tooltip("Right stick turn speed at full tilt, in degrees per second. LookInput is in stick units.")]
        [Min(0f)] public float orbitSpeed = 180f;

        [Tooltip("Mouse turn in degrees per pixel moved.")]
        [Min(0f)] public float mouseSensitivity = 0.15f;
    }
}
