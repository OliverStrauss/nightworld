using NightWorld.Data;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Lives in the Boot scene. Builds the starting zone from data and points the camera at it.
    /// Create the Boot scene with the menu item NightWorld → Setup → Create Boot Scene.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public ZoneDefinition startZone;

        public GameObject ActiveZone { get; private set; }

        private void Start()
        {
            if (startZone == null)
            {
                Debug.LogError("[GameBootstrap] No startZone assigned.");
                return;
            }

            ActiveZone = ZoneBuilder.Build(startZone);

            // Temporary until the player/camera cards land: frame the zone like the first screenshot.
            var cam = Camera.main;
            if (cam != null && startZone.screenshotPoints.Count > 0)
            {
                var point = startZone.screenshotPoints[0];
                cam.transform.position = point.position;
                cam.transform.LookAt(point.lookAt);
            }
        }
    }
}
