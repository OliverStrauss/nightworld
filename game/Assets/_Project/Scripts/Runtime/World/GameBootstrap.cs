using NightWorld.Data;
using NightWorld.Input;
using NightWorld.Player;
using NightWorld.UI;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Lives in the Boot scene. Builds the world from the starting zone's data, spawns the hero
    /// at the start zone's PlayerSpawn, gives it a FollowCamera that drives Camera.main and shows
    /// its stats on a Hud. Owns the one <see cref="GameControls"/>, built from config.controlSettings,
    /// that the hero and its camera listen to.
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

        /// <summary>The hero, spawned in Start when config has playerSettings and progressionSettings.</summary>
        public PlayerController Player { get; private set; }

        /// <summary>The HUD, created and bound to the hero's PlayerStats right after the hero spawns.</summary>
        public Hud Hud { get; private set; }

        /// <summary>The game's input actions, created and enabled right before the hero spawns;
        /// null without config.controlSettings. Disposed in OnDestroy.</summary>
        public GameControls Controls { get; private set; }

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
        }

        private void SpawnPlayer(ZoneDefinition zone)
        {
            var settings = config != null ? config.playerSettings : null;
            if (settings == null)
            {
                Debug.LogWarning("[GameBootstrap] No playerSettings in the GameConfig; no player spawned.");
                return;
            }
            if (config.progressionSettings == null)
            {
                Debug.LogWarning("[GameBootstrap] No progressionSettings in the GameConfig; no player spawned.");
                return;
            }

            var marker = WorldRoot.transform.Find($"Zone_{zone.id}/PlayerSpawn");
            if (marker == null)
            {
                Debug.LogError($"[GameBootstrap] Zone_{zone.id}/PlayerSpawn not found; no player spawned.");
                return;
            }

            if (config.controlSettings != null)
            {
                Controls = new GameControls(config.controlSettings);
                Controls.Enable();
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] No controlSettings in the GameConfig; the hero spawns but can't be controlled.");
            }

            Player = PlayerSpawner.Spawn(marker, settings, config.progressionSettings, Controls);
            Player.gameObject.AddComponent<FollowCamera>().SetControls(Controls);

            Hud = new GameObject("Hud").AddComponent<Hud>();
            Hud.Bind(Player.GetComponent<PlayerStats>());
        }

        private void OnDestroy()
        {
            Controls?.Dispose();
            Controls = null;
        }
    }
}
