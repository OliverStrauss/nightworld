using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NightWorld.Data;
using NightWorld.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NightWorld.EditorTools
{
    /// <summary>
    /// Builds each zone in an empty scene and renders PNGs from its screenshot points into
    /// &lt;project&gt;/Screenshots/&lt;zone-id&gt;/&lt;point&gt;.png. The reviewer agent looks at these.
    ///
    ///  - Menu: NightWorld → Capture Zone Screenshots
    ///  - Batch mode: -executeMethod NightWorld.EditorTools.ZoneScreenshotter.RunFromCommandLine
    ///    (must NOT be started with -nographics, or there's nothing to render with)
    /// </summary>
    public static class ZoneScreenshotter
    {
        private const int Width = 1280;
        private const int Height = 720;

        public static string OutputRoot =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");

        [MenuItem("NightWorld/Capture Zone Screenshots")]
        public static void CaptureFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var files = CaptureAll();
            Debug.Log($"[ZoneScreenshotter] Wrote {files.Count} screenshots to {OutputRoot}");
            EditorUtility.RevealInFinder(OutputRoot);
        }

        public static void RunFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    Debug.LogError("[ZoneScreenshotter] No graphics device. Unity was probably started with " +
                                   "-nographics, so screenshots can't be rendered.");
                    exitCode = 2;
                }
                else
                {
                    var files = CaptureAll();
                    Debug.Log($"[ZoneScreenshotter] Wrote {files.Count} screenshots to {OutputRoot}");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static List<string> CaptureAll()
        {
            var files = new List<string>();
            var zones = AssetDatabase.FindAssets("t:ZoneDefinition", new[] { ContentValidator.DataFolder })
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<ZoneDefinition>(path))
                .Where(z => z != null);

            foreach (var zone in zones) files.AddRange(CaptureZone(zone));
            return files;
        }

        public static List<string> CaptureZone(ZoneDefinition zone)
        {
            var files = new List<string>();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.65f);

            var root = ZoneBuilder.Build(zone);
            foreach (var spawner in root.GetComponentsInChildren<EnemySpawner>()) spawner.SpawnAll();

            var cam = new GameObject("ScreenshotCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2000f;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            string dir = Path.Combine(OutputRoot, zone.id);
            Directory.CreateDirectory(dir);

            try
            {
                foreach (var point in zone.screenshotPoints)
                {
                    if (point == null) continue;

                    cam.transform.position = point.position;
                    cam.transform.LookAt(point.lookAt);
                    cam.targetTexture = rt;
                    cam.Render();

                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = previous;
                    cam.targetTexture = null;

                    string path = Path.Combine(dir, SafeFileName(point.name) + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    files.Add(path);
                }
            }
            finally
            {
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }

            return files;
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }
}
