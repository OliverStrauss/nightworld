using NightWorld.Data;
using NightWorld.Player;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Lives in the Boot scene. Builds the world from the starting zone's data, spawns the hero
    /// at the start zone's PlayerSpawn and points the camera at the zone.
    /// Create the Boot scene with the menu item NightWorld → Setup → Create Boot Scene.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("The game's data root (Data/game-config.asset). Set by NightWorld → Setup → Create Boot Scene.")]
        public GameConfig config;

        [Tooltip("Old fallback, only used while config is empty.")]
        public ZoneDefinition startZone;

        /// <summary>The "World" root: the start zone plus every zone linked from it.</summary>
        public GameObject WorldRoot { get; private set; }

        /// <summary>The hero, spawned in Start when config has playerSettings.</summary>
        public PlayerController Player { get; private set; }

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

            WorldRoot = WorldBuilder.Build(zone);
            SpawnPlayer(zone);

            // Temporary until the player/camera cards land: frame the zone like the first screenshot.
            var cam = Camera.main;
            if (cam != null && zone.screenshotPoints.Count > 0)
            {
                var point = zone.screenshotPoints[0];
                cam.transform.position = point.position;
                cam.transform.LookAt(point.lookAt);
            }
        }

        private void SpawnPlayer(ZoneDefinition zone)
        {
            var settings = config != null ? config.playerSettings : null;
            if (settings == null)
            {
                Debug.LogWarning("[GameBootstrap] No playerSettings in the GameConfig; no player spawned.");
                return;
            }

            var marker = WorldRoot.transform.Find($"Zone_{zone.id}/PlayerSpawn");
            if (marker == null)
            {
                Debug.LogError($"[GameBootstrap] Zone_{zone.id}/PlayerSpawn not found; no player spawned.");
                return;
            }

            Player = PlayerSpawner.Spawn(marker, settings);
        }
    }
}
