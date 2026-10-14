using System.Collections.Generic;
using UnityEditor;

namespace NightWorld.EditorTools
{
    /// <summary>
    /// Sets each asset pack's model import scale so 1 Unity unit = 1 metre. Runs on every import,
    /// so a fresh checkout or a reimport always gets the same size. Add a line when a new pack
    /// arrives at the wrong size.
    /// </summary>
    public class ThirdPartyImportScale : AssetPostprocessor
    {
        // Kenney kits are built on 1-unit tiles (a wall is 1 tall), too small next to a 1.8 m player.
        private static readonly Dictionary<string, float> PackScale = new Dictionary<string, float>
        {
            { "Assets/ThirdParty/KenneyNature/", 3f },
            { "Assets/ThirdParty/KenneyFantasyTown/", 3f },
        };

        public static float ScaleFor(string assetPath)
        {
            foreach (var pack in PackScale)
                if (assetPath.StartsWith(pack.Key)) return pack.Value;
            return 1f;
        }

        private void OnPreprocessModel()
        {
            ((ModelImporter)assetImporter).globalScale = ScaleFor(assetPath);
        }
    }
}
