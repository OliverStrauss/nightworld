using System.Collections.Generic;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>Flat-coloured materials for placeholder geometry, cached per colour.</summary>
    public static class PlaceholderMaterials
    {
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        public static Material Get(Color color)
        {
            if (Cache.TryGetValue(color, out var cached) && cached != null) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = $"Placeholder_{ColorUtility.ToHtmlStringRGB(color)}" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);

            Cache[color] = mat;
            return mat;
        }
    }
}
