using NightWorld.Data;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Lives in the Boot scene. Builds the starting zone from data and points the camera at it.
    /// Create the Boot scene with the menu item NightWorld → Setup → Create Boot Scene.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("The game's data root (Data/game-config.asset). Set by NightWorld → Setup → Create Boot Scene.")]
        public GameConfig config;

        [Tooltip("Old fallback, only used while config is empty.")]
        public ZoneDefinition startZone;

        public GameObject ActiveZone { get; private set; }

        /// <summary>config.startZone when config is set, otherwise the old startZone field.</summary>
        public ZoneDefinition StartZone => config != null ? config.startZone : startZone;

        private void Start()
        {
            if (config == null)
                Debug.LogWarning("[GameBootstrap] No GameConfig assigned; using the old startZone field. Rerun NightWorld → Setup → Create Boot Scene to fix this.");

            var zone = StartZone;
            if (zone == null)
            {
                Debug.LogError("[GameBootstrap] No startZone assigned.");
                return;
            }

            ActiveZone = ZoneBuilder.Build(zone);

            // Temporary until the player/camera cards land: frame the zone like the first screenshot.
            var cam = Camera.main;
            if (cam != null && zone.screenshotPoints.Count > 0)
            {
                var point = zone.screenshotPoints[0];
                cam.transform.position = point.position;
                cam.transform.LookAt(point.lookAt);
            }
        }
    }
}
