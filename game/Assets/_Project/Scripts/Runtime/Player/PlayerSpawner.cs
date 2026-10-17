using System;
using NightWorld.Data;
using NightWorld.Input;
using NightWorld.Progression;
using NightWorld.World;
using UnityEngine;

namespace NightWorld.Player
{
    /// <summary>
    /// Creates the hero: a CharacterController with a bright blue capsule placeholder. Layout:
    ///   Player   (CharacterController + PlayerController + Health + PlayerStats, pivot at the feet)
    ///    └── Body (capsule mesh, no collider)
    /// </summary>
    public static class PlayerSpawner
    {
        public const float Height = 1.8f;
        public const float Radius = 0.4f;

        /// <summary>Spawn this far above the marker so the capsule never starts inside the ground; gravity settles it at once.</summary>
        public const float SpawnLift = 0.1f;

        /// <summary>Bright blue: it stands out on green grass, and red and purple mean danger.</summary>
        public static readonly Color BodyColor = new Color(0.15f, 0.5f, 1f, 1f);

        /// <param name="controls">The shared controls the hero walks with; null = none (tests set MoveInput by hand).</param>
        public static PlayerController Spawn(Transform spawnMarker, PlayerSettings settings, ProgressionSettings progressionSettings,
            GameControls controls = null)
        {
            if (spawnMarker == null) throw new ArgumentNullException(nameof(spawnMarker));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (progressionSettings == null) throw new ArgumentNullException(nameof(progressionSettings));

            var go = new GameObject("Player") { tag = "Player" };
            go.transform.position = spawnMarker.position + Vector3.up * SpawnLift;

            var body = go.AddComponent<CharacterController>();
            body.height = Height;
            body.radius = Radius;
            body.center = new Vector3(0f, Height * 0.5f, 0f);

            var visual = ZoneBuilder.CreatePlaceholder(PlaceholderShape.Capsule, BodyColor);
            visual.name = "Body";
            // Removed at once so the CharacterController never bumps into its own body.
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = body.center;
            // Unity's capsule mesh is 2 m tall with radius 0.5.
            visual.transform.localScale = new Vector3(Radius * 2f, Height * 0.5f, Radius * 2f);

            var player = go.AddComponent<PlayerController>();
            player.settings = settings;
            player.spawnPoint = spawnMarker;
            player.SetControls(controls);

            go.AddComponent<PlayerStats>().Init(progressionSettings);
            return player;
        }
    }
}
