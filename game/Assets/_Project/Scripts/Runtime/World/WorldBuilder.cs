using System;
using System.Collections.Generic;
using NightWorld.Data;
using UnityEngine;

namespace NightWorld.World
{
    /// <summary>
    /// Builds the start zone and every zone reachable through <see cref="ZoneDefinition.neighbours"/>
    /// as one world. Each zone is built by <see cref="ZoneBuilder"/> and placed at its parent's
    /// origin + link offset, so zone-local coordinates in the data never change.
    ///
    /// Layout of the result:
    ///   World
    ///    ├── Zone_&lt;start-id&gt;      (at the origin)
    ///    └── Zone_&lt;neighbour-id&gt;  (at start origin + link offset), ...
    /// </summary>
    public static class WorldBuilder
    {
        public static GameObject Build(ZoneDefinition start)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));

            var world = new GameObject("World");
            var seen = new HashSet<ZoneDefinition> { start };
            var queue = new Queue<KeyValuePair<ZoneDefinition, Vector3>>();
            queue.Enqueue(new KeyValuePair<ZoneDefinition, Vector3>(start, Vector3.zero));

            // Breadth-first, so a zone linked from two places is placed by the link nearest the start.
            while (queue.Count > 0)
            {
                var next = queue.Dequeue();
                var zone = next.Key;
                var origin = next.Value;

                var root = ZoneBuilder.Build(zone, world.transform);
                root.transform.localPosition = origin;

                foreach (var link in zone.neighbours)
                {
                    if (link == null || link.zone == null || !seen.Add(link.zone)) continue;
                    queue.Enqueue(new KeyValuePair<ZoneDefinition, Vector3>(link.zone, origin + link.offset));
                }
            }

            return world;
        }
    }
}
