using System.Linq;
using NightWorld.Data;
using NightWorld.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NightWorld.EditorTools
{
    /// <summary>
    /// Creates Assets/_Project/Scenes/Boot.unity from code so nobody has to hand-edit scene YAML.
    /// Run once after creating the Unity project: NightWorld → Setup → Create Boot Scene.
    /// </summary>
    public static class BootSceneCreator
    {
        public const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        public const string StartZoneId = "harbor-town";

        [MenuItem("NightWorld/Setup/Create Boot Scene")]
        public static void CreateBootScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var bootstrap = new GameObject("Bootstrap").AddComponent<GameBootstrap>();
            bootstrap.startZone = AssetDatabase.FindAssets("t:ZoneDefinition", new[] { ContentValidator.DataFolder })
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<ZoneDefinition>(path))
                .FirstOrDefault(z => z != null && z.id == StartZoneId);

            if (bootstrap.startZone == null)
                Debug.LogWarning($"[BootSceneCreator] No zone with id '{StartZoneId}' found; assign startZone by hand.");

            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            Debug.Log($"[BootSceneCreator] Created {BootScenePath} and set it as the only build scene.");
        }
    }
}
