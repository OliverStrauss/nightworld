using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NightWorld.EditorTools
{
    /// <summary>
    /// Wraps every model under Assets/ThirdParty in a prefab variant under Assets/_Project/Prefabs,
    /// mirroring the folder layout, with colliders so the player can't walk through props.
    /// Zone and enemy data reference these prefabs, never the raw models.
    /// Prefabs that already exist are skipped, so their GUIDs and any hand tweaks survive a re-run.
    /// Run after importing a pack: NightWorld → Setup → Create Wrapper Prefabs.
    /// </summary>
    public static class WrapperPrefabCreator
    {
        public const string ThirdPartyFolder = "Assets/ThirdParty";
        public const string PrefabsFolder = "Assets/_Project/Prefabs";

        [MenuItem("NightWorld/Setup/Create Wrapper Prefabs")]
        public static void CreateAll()
        {
            int created = 0;
            // Preview scene so building prefabs never touches or dirties the open scene.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ThirdPartyFolder }))
                {
                    string modelPath = AssetDatabase.GUIDToAssetPath(guid);
                    // Packs ship each model as FBX, OBJ and DAE; wrapping all of them would triplicate prefabs.
                    if (!modelPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                    string prefabPath = PrefabPathFor(modelPath);
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) continue;

                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                    AddColliders(instance);
                    EnsureFolder(Path.GetDirectoryName(prefabPath).Replace('\\', '/'));
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    Object.DestroyImmediate(instance);
                    created++;
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log($"[WrapperPrefabCreator] Created {created} prefabs under {PrefabsFolder}.");
        }

        /// <summary>Assets/ThirdParty/Pack/FBX/Tree.fbx → Assets/_Project/Prefabs/Pack/FBX/Tree.prefab</summary>
        public static string PrefabPathFor(string modelPath)
        {
            string relative = modelPath.Substring(ThirdPartyFolder.Length + 1);
            return $"{PrefabsFolder}/{Path.ChangeExtension(relative, ".prefab")}";
        }

        /// <summary>
        /// Adds a MeshCollider to every static mesh that has no collider yet. Rigged models
        /// (characters, monsters) get none: the player and enemy systems own their colliders.
        /// </summary>
        // ponytail: every static mesh gets a collider, grass and flowers too; delete it in the
        // prefab for walk-through props, or add a per-pack rule if that gets tedious.
        public static void AddColliders(GameObject root)
        {
            if (root.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
