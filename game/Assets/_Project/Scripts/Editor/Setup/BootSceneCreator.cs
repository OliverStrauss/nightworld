using System.Linq;
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

        [MenuItem("NightWorld/Setup/Create Boot Scene")]
        public static void CreateBootScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var bootstrap = new GameObject("Bootstrap").AddComponent<GameBootstrap>();
            bootstrap.config = ContentValidator.LoadAllGameConfigs().FirstOrDefault();

            if (bootstrap.config == null)
                Debug.LogWarning($"[BootSceneCreator] No GameConfig found under {ContentValidator.DataFolder}; assign config by hand.");

            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            Debug.Log($"[BootSceneCreator] Created {BootScenePath} and set it as the only build scene.");
        }
    }
}
