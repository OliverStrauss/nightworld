using System.Collections.Generic;
using NightWorld.Core;
using UnityEngine;

namespace NightWorld.Data
{
    public enum ZoneType
    {
        Town = 0,
        Enemy = 1,
    }

    /// <summary>Primitive used when a prop has no prefab yet.</summary>
    public enum PlaceholderShape
    {
        None = 0,
        Cube = 1,
        Sphere = 2,
        Capsule = 3,
        Cylinder = 4,
    }

    [System.Serializable]
    public class GroundSettings
    {
        [Tooltip("Width (x) and depth (z) in metres. The ground is centred on the zone origin.")]
        public Vector2 size = new Vector2(100f, 100f);
        public Color color = new Color(0.45f, 0.7f, 0.35f, 1f);
        [Tooltip("Max hill height in metres. 0 = flat.")]
        [Min(0f)] public float heightAmplitude = 2f;
        [Min(0.001f)] public float noiseScale = 0.03f;
        public int seed = 1;
        [Tooltip("Distance between ground mesh vertices in metres.")]
        [Min(0.5f)] public float vertexSpacing = 2f;
    }

    [System.Serializable]
    public class PropPlacement
    {
        public string name = "prop";
        [Tooltip("Prefab with its pivot at the base. Leave empty and pick a placeholder shape instead.")]
        public GameObject prefab;
        public PlaceholderShape placeholder = PlaceholderShape.None;
        public Color placeholderColor = Color.gray;
        [Tooltip("Zone-local position. With snapToGround, y is an offset above the terrain.")]
        public Vector3 position;
        [Tooltip("Euler angles in degrees.")]
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
        public bool snapToGround = true;
    }

    [System.Serializable]
    public class EnemySpawn
    {
        public EnemyDefinition enemy;
        [Tooltip("Zone-local centre of the spawn area (y is ignored; enemies stand on the terrain).")]
        public Vector3 position;
        [Min(0f)] public float radius = 5f;
        [Min(1)] public int count = 3;
    }

    [System.Serializable]
    public class ShopPlacement
    {
        public ShopDefinition shop;
        public Vector3 position;
        public float rotationY;
        [Tooltip("Optional building prefab. Empty = placeholder stall.")]
        public GameObject prefab;
    }

    [System.Serializable]
    public class ScreenshotPoint
    {
        public string name = "overview";
        public Vector3 position = new Vector3(0f, 30f, -40f);
        public Vector3 lookAt = Vector3.zero;
    }

    [CreateAssetMenu(menuName = "NightWorld/Zone", fileName = "new-zone")]
    public class ZoneDefinition : Definition
    {
        public ZoneType zoneType = ZoneType.Enemy;
        [Min(1)] public int levelMin = 1;
        [Min(1)] public int levelMax = 1;
        [Range(1, 5)] public int dangerTier = 1;

        public GroundSettings ground = new GroundSettings();
        public Vector3 playerSpawn;

        public List<PropPlacement> props = new List<PropPlacement>();
        public List<EnemySpawn> enemySpawns = new List<EnemySpawn>();
        public List<ShopPlacement> shops = new List<ShopPlacement>();

        [Tooltip("Camera poses CI uses to photograph this zone for the reviewer agent. At least one required.")]
        public List<ScreenshotPoint> screenshotPoints = new List<ScreenshotPoint>();
    }
}
