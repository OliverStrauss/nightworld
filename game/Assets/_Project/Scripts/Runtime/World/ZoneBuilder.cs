using System;
using NightWorld.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace NightWorld.World
{
    /// <summary>
    /// Turns a <see cref="ZoneDefinition"/> into GameObjects. This is the only way zones get
    /// into a scene: agents add content by editing data, never by editing scene files.
    ///
    /// Layout of the result:
    ///   Zone_&lt;id&gt;
    ///    ├── Ground        (generated mesh + collider)
    ///    ├── Props
    ///    ├── Enemies       (one EnemySpawner per spawn entry)
    ///    ├── Shops
    ///    └── PlayerSpawn   (empty marker on the terrain)
    /// </summary>
    public static class ZoneBuilder
    {
        public static GameObject Build(ZoneDefinition zone, Transform parent = null)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));

            var root = new GameObject($"Zone_{zone.id}");
            if (parent != null) root.transform.SetParent(parent, false);

            BuildGround(zone.ground, root.transform);

            var propsRoot = CreateChild(root.transform, "Props");
            for (int i = 0; i < zone.props.Count; i++)
            {
                var prop = zone.props[i];
                if (prop != null) BuildProp(prop, zone.ground, propsRoot);
            }

            var enemiesRoot = CreateChild(root.transform, "Enemies");
            for (int i = 0; i < zone.enemySpawns.Count; i++)
            {
                var spawn = zone.enemySpawns[i];
                if (spawn == null || spawn.enemy == null) continue;

                var go = new GameObject($"Spawner_{spawn.enemy.id}_{i}");
                go.transform.SetParent(enemiesRoot, false);
                go.transform.localPosition = SnapToGround(zone.ground, spawn.position, 0f);
                var spawner = go.AddComponent<EnemySpawner>();
                spawner.Configure(spawn.enemy, spawn.position, spawn.radius, spawn.count,
                    zone.ground, root.transform, StableSeed(zone.id, i));
            }

            var shopsRoot = CreateChild(root.transform, "Shops");
            for (int i = 0; i < zone.shops.Count; i++)
            {
                var placement = zone.shops[i];
                if (placement != null && placement.shop != null) BuildShop(placement, zone.ground, shopsRoot);
            }

            var spawnMarker = CreateChild(root.transform, "PlayerSpawn");
            spawnMarker.localPosition = SnapToGround(zone.ground, zone.playerSpawn, 0f);

            return root;
        }

        /// <summary>Terrain height (zone-local y) at zone-local x/z. Deterministic per seed.</summary>
        public static float SampleGroundHeight(GroundSettings ground, float x, float z)
        {
            if (ground == null || ground.heightAmplitude <= 0f) return 0f;

            float ox = 1000f + ground.seed * 17.13f;
            float oz = 1000f + ground.seed * 31.71f;
            float n = Mathf.PerlinNoise(x * ground.noiseScale + ox, z * ground.noiseScale + oz);
            return (n - 0.5f) * 2f * ground.heightAmplitude;
        }

        /// <summary>Returns position with y = terrain height + yOffset.</summary>
        public static Vector3 SnapToGround(GroundSettings ground, Vector3 position, float yOffset)
        {
            return new Vector3(position.x, SampleGroundHeight(ground, position.x, position.z) + yOffset, position.z);
        }

        /// <summary>True if zone-local x/z is on the ground mesh.</summary>
        public static bool IsInsideGround(GroundSettings ground, Vector3 position)
        {
            return Mathf.Abs(position.x) <= ground.size.x * 0.5f && Mathf.Abs(position.z) <= ground.size.y * 0.5f;
        }

        /// <summary>Half the height of Unity's built-in primitive, so placeholders sit on the ground.</summary>
        public static float PrimitiveHalfHeight(PlaceholderShape shape)
        {
            switch (shape)
            {
                case PlaceholderShape.Capsule:
                case PlaceholderShape.Cylinder:
                    return 1f;
                default:
                    return 0.5f;
            }
        }

        public static GameObject CreatePlaceholder(PlaceholderShape shape, Color color)
        {
            PrimitiveType type;
            switch (shape)
            {
                case PlaceholderShape.Sphere: type = PrimitiveType.Sphere; break;
                case PlaceholderShape.Capsule: type = PrimitiveType.Capsule; break;
                case PlaceholderShape.Cylinder: type = PrimitiveType.Cylinder; break;
                default: type = PrimitiveType.Cube; break;
            }

            var go = GameObject.CreatePrimitive(type);
            go.GetComponent<MeshRenderer>().sharedMaterial = PlaceholderMaterials.Get(color);
            return go;
        }

        private static void BuildGround(GroundSettings ground, Transform parent)
        {
            float spacing = Mathf.Max(0.5f, ground.vertexSpacing);
            int nx = Mathf.Max(2, Mathf.CeilToInt(ground.size.x / spacing) + 1);
            int nz = Mathf.Max(2, Mathf.CeilToInt(ground.size.y / spacing) + 1);

            var vertices = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    float u = x / (float)(nx - 1);
                    float v = z / (float)(nz - 1);
                    float px = (u - 0.5f) * ground.size.x;
                    float pz = (v - 0.5f) * ground.size.y;
                    int idx = z * nx + x;
                    vertices[idx] = new Vector3(px, SampleGroundHeight(ground, px, pz), pz);
                    uvs[idx] = new Vector2(u, v);
                }
            }

            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int z = 0; z < nz - 1; z++)
            {
                for (int x = 0; x < nx - 1; x++)
                {
                    int i = z * nx + x;
                    triangles[t++] = i;
                    triangles[t++] = i + nx;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + nx;
                    triangles[t++] = i + nx + 1;
                }
            }

            var mesh = new Mesh { name = "GroundMesh" };
            if (vertices.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = PlaceholderMaterials.Get(ground.color);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void BuildProp(PropPlacement prop, GroundSettings ground, Transform parent)
        {
            GameObject go;
            float yOffset = prop.position.y;

            if (prop.prefab != null)
            {
                go = UnityEngine.Object.Instantiate(prop.prefab);
            }
            else if (prop.placeholder != PlaceholderShape.None)
            {
                go = CreatePlaceholder(prop.placeholder, prop.placeholderColor);
                yOffset += PrimitiveHalfHeight(prop.placeholder) * prop.scale.y;
            }
            else
            {
                Debug.LogWarning($"[ZoneBuilder] Prop '{prop.name}' has no prefab and no placeholder; skipped.");
                return;
            }

            go.name = string.IsNullOrEmpty(prop.name) ? "prop" : prop.name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = prop.snapToGround
                ? SnapToGround(ground, prop.position, yOffset)
                : prop.position;
            go.transform.localRotation = Quaternion.Euler(prop.rotation);
            go.transform.localScale = prop.scale;
        }

        private static void BuildShop(ShopPlacement placement, GroundSettings ground, Transform parent)
        {
            var shopRoot = new GameObject($"Shop_{placement.shop.id}");
            shopRoot.transform.SetParent(parent, false);
            shopRoot.transform.localPosition = SnapToGround(ground, placement.position, 0f);
            shopRoot.transform.localRotation = Quaternion.Euler(0f, placement.rotationY, 0f);

            if (placement.prefab != null)
            {
                var building = UnityEngine.Object.Instantiate(placement.prefab);
                building.transform.SetParent(shopRoot.transform, false);
            }
            else
            {
                var stall = CreatePlaceholder(PlaceholderShape.Cube, new Color(0.55f, 0.35f, 0.2f, 1f));
                stall.name = "Stall";
                stall.transform.SetParent(shopRoot.transform, false);
                stall.transform.localScale = new Vector3(3f, 2.5f, 3f);
                stall.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            }

            var trigger = new GameObject("ShopTrigger");
            trigger.transform.SetParent(shopRoot.transform, false);
            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(6f, 3f, 6f);
            box.center = new Vector3(0f, 1.5f, 0f);
            trigger.AddComponent<ShopTrigger>().shop = placement.shop;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>FNV-1a hash so seeds are identical on every machine and run.</summary>
        public static int StableSeed(string text, int salt)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }
                }
                hash ^= (uint)salt;
                hash *= 16777619;
                return (int)hash;
            }
        }
    }
}
