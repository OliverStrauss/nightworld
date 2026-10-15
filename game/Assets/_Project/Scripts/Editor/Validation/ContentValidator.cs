using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NightWorld.Core;
using NightWorld.Data;
using NightWorld.World;
using UnityEditor;
using UnityEngine;

namespace NightWorld.EditorTools
{
    /// <summary>
    /// Checks all content in Assets/_Project/Data for mistakes agents commonly make.
    /// Runs three ways:
    ///  - Menu: NightWorld → Validate Content
    ///  - EditMode test: ContentValidatorTests.AllProjectContent_IsValid (so CI always runs it)
    ///  - Batch mode: -executeMethod NightWorld.EditorTools.ContentValidator.RunFromCommandLine
    /// </summary>
    public static class ContentValidator
    {
        public const string DataFolder = "Assets/_Project/Data";

        /// <summary>How far apart (or into each other) two linked grounds may be and still count as touching, in metres.</summary>
        private const float LinkTolerance = 0.01f;

        private static readonly Regex IdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        public enum Severity
        {
            Warning,
            Error,
        }

        public readonly struct Issue
        {
            public readonly Severity Severity;
            public readonly string Asset;
            public readonly string Message;

            public Issue(Severity severity, string asset, string message)
            {
                Severity = severity;
                Asset = asset;
                Message = message;
            }

            public override string ToString() => $"[{Severity}] {Asset}: {Message}";
        }

        public static List<Definition> LoadAllDefinitions()
        {
            return AssetDatabase.FindAssets("t:Definition", new[] { DataFolder })
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<Definition>(path))
                .Where(d => d != null)
                .ToList();
        }

        public static List<GameConfig> LoadAllGameConfigs()
        {
            return AssetDatabase.FindAssets("t:GameConfig", new[] { DataFolder })
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Select(path => AssetDatabase.LoadAssetAtPath<GameConfig>(path))
                .Where(c => c != null)
                .ToList();
        }

        /// <summary>Everything under Data/: definitions plus the single GameConfig.</summary>
        public static List<Issue> ValidateProject()
        {
            var issues = Validate(LoadAllDefinitions());
            issues.AddRange(ValidateGameConfigs(LoadAllGameConfigs()));
            return issues;
        }

        /// <summary>GameConfig is not a Definition, so it's checked here: exactly one, no missing references.</summary>
        public static List<Issue> ValidateGameConfigs(IEnumerable<GameConfig> configs)
        {
            var issues = new List<Issue>();
            var all = configs.Where(c => c != null).ToList();

            if (all.Count == 0)
                issues.Add(Error(DataFolder, "no GameConfig asset; Boot needs exactly one (Data/game-config.asset)."));
            else if (all.Count > 1)
                issues.Add(Error(DataFolder, $"{all.Count} GameConfig assets; Boot needs exactly one."));

            foreach (var config in all)
            {
                string path = AssetDatabase.GetAssetPath(config);
                string label = string.IsNullOrEmpty(path) ? $"GameConfig '{config.name}'" : path;
                if (config.startZone == null) issues.Add(Error(label, "startZone is missing."));
                if (config.progressionSettings == null) issues.Add(Error(label, "progressionSettings is missing."));
            }

            return issues;
        }

        public static List<Issue> Validate(IEnumerable<Definition> definitions)
        {
            var issues = new List<Issue>();
            var all = definitions.Where(d => d != null).ToList();
            var seenIds = new Dictionary<string, Definition>();

            foreach (var def in all)
            {
                string label = Label(def);

                if (string.IsNullOrEmpty(def.id))
                {
                    issues.Add(Error(label, "id is empty."));
                }
                else
                {
                    if (!IdPattern.IsMatch(def.id))
                        issues.Add(Error(label, $"id '{def.id}' must be lowercase kebab-case (e.g. iron-sword)."));

                    if (seenIds.TryGetValue(def.id, out var other))
                        issues.Add(Error(label, $"duplicate id '{def.id}' (also used by {Label(other)})."));
                    else
                        seenIds[def.id] = def;

                    if (!string.IsNullOrEmpty(def.name) && def.name != def.id)
                        issues.Add(Warning(label, $"asset file name '{def.name}' should match id '{def.id}'."));
                }

                if (string.IsNullOrWhiteSpace(def.displayName))
                    issues.Add(Warning(label, "displayName is empty."));

                switch (def)
                {
                    case ItemDefinition item: ValidateItem(item, label, issues); break;
                    case EnemyDefinition enemy: ValidateEnemy(enemy, label, issues); break;
                    case ShopDefinition shop: ValidateShop(shop, label, issues); break;
                    case ZoneDefinition zone: ValidateZone(zone, label, issues); break;
                }
            }

            return issues;
        }

        private static void ValidateItem(ItemDefinition item, string label, List<Issue> issues)
        {
            if (item.baseValue < 0) issues.Add(Error(label, "baseValue is negative."));
            if (item.itemType == ItemType.Weapon && item.damage <= 0) issues.Add(Error(label, "weapon has no damage."));
            if (item.itemType == ItemType.Armor && item.armor <= 0) issues.Add(Error(label, "armor has no armor value."));
            if (item.itemType == ItemType.Consumable && item.healAmount <= 0) issues.Add(Warning(label, "consumable does nothing (healAmount is 0)."));
        }

        private static void ValidateEnemy(EnemyDefinition enemy, string label, List<Issue> issues)
        {
            if (enemy.maxHp <= 0) issues.Add(Error(label, "maxHp must be > 0."));
            if (enemy.goldMin > enemy.goldMax) issues.Add(Error(label, $"goldMin ({enemy.goldMin}) > goldMax ({enemy.goldMax})."));

            if (enemy.lootTable == null || enemy.lootTable.Count == 0)
            {
                issues.Add(Error(label, "lootTable is empty; every enemy needs at least one loot entry."));
            }
            else
            {
                for (int i = 0; i < enemy.lootTable.Count; i++)
                {
                    var entry = enemy.lootTable[i];
                    if (entry == null || entry.item == null) issues.Add(Error(label, $"lootTable[{i}] has no item (missing reference?)."));
                    else if (entry.chance <= 0f || entry.chance > 1f) issues.Add(Error(label, $"lootTable[{i}] chance {entry.chance} must be in (0, 1]."));
                }
            }

            if (enemy.prefab == null) issues.Add(Warning(label, "no prefab; a placeholder capsule will be used."));
        }

        private static void ValidateShop(ShopDefinition shop, string label, List<Issue> issues)
        {
            if (shop.inventory == null || shop.inventory.Count == 0)
            {
                issues.Add(Error(label, "inventory is empty."));
                return;
            }

            for (int i = 0; i < shop.inventory.Count; i++)
            {
                if (shop.inventory[i] == null) issues.Add(Error(label, $"inventory[{i}] is missing (broken reference?)."));
            }
        }

        private static void ValidateZone(ZoneDefinition zone, string label, List<Issue> issues)
        {
            var g = zone.ground;
            if (g == null || g.size.x <= 0f || g.size.y <= 0f)
            {
                issues.Add(Error(label, "ground size must be > 0."));
                return;
            }

            float maxEdgeBlend = Mathf.Min(g.size.x, g.size.y) * 0.5f;
            if (g.edgeBlend < 0f || g.edgeBlend > maxEdgeBlend)
                issues.Add(Error(label, $"ground edgeBlend {g.edgeBlend} must be between 0 and {maxEdgeBlend} (half the shorter ground side)."));

            if (zone.levelMin > zone.levelMax) issues.Add(Error(label, $"levelMin ({zone.levelMin}) > levelMax ({zone.levelMax})."));
            if (!ZoneBuilder.IsInsideGround(g, zone.playerSpawn)) issues.Add(Error(label, "playerSpawn is outside the ground."));

            if (zone.screenshotPoints == null || zone.screenshotPoints.Count == 0)
                issues.Add(Error(label, "no screenshotPoints; CI can't photograph this zone for review."));
            else
            {
                var names = new HashSet<string>();
                foreach (var p in zone.screenshotPoints)
                {
                    if (p == null) continue;
                    if (string.IsNullOrEmpty(p.name) || !names.Add(p.name))
                        issues.Add(Error(label, $"screenshot point name '{p.name}' is empty or duplicated."));
                    if ((p.position - p.lookAt).sqrMagnitude < 0.01f)
                        issues.Add(Error(label, $"screenshot point '{p.name}' looks at its own position."));
                }
            }

            if (zone.zoneType == ZoneType.Town && zone.enemySpawns.Count > 0)
                issues.Add(Error(label, "towns must not have enemy spawns."));

            for (int i = 0; i < zone.enemySpawns.Count; i++)
            {
                var s = zone.enemySpawns[i];
                if (s == null || s.enemy == null) { issues.Add(Error(label, $"enemySpawns[{i}] has no enemy (missing reference?).")); continue; }
                if (s.count < 1) issues.Add(Error(label, $"enemySpawns[{i}] count must be >= 1."));
                if (!ZoneBuilder.IsInsideGround(g, s.position)) issues.Add(Error(label, $"enemySpawns[{i}] ({s.enemy.id}) is outside the ground."));
            }

            for (int i = 0; i < zone.props.Count; i++)
            {
                var p = zone.props[i];
                if (p == null) continue;
                if (p.prefab == null && p.placeholder == PlaceholderShape.None)
                    issues.Add(Error(label, $"props[{i}] '{p.name}' has no prefab and no placeholder shape."));
                if (!ZoneBuilder.IsInsideGround(g, p.position))
                    issues.Add(Error(label, $"props[{i}] '{p.name}' is outside the ground."));
                if (p.scale.x <= 0f || p.scale.y <= 0f || p.scale.z <= 0f)
                    issues.Add(Error(label, $"props[{i}] '{p.name}' has a zero or negative scale."));
            }

            for (int i = 0; i < zone.shops.Count; i++)
            {
                var s = zone.shops[i];
                if (s == null || s.shop == null) { issues.Add(Error(label, $"shops[{i}] has no shop (missing reference?).")); continue; }
                if (!ZoneBuilder.IsInsideGround(g, s.position)) issues.Add(Error(label, $"shops[{i}] ({s.shop.id}) is outside the ground."));
            }

            for (int i = 0; i < zone.neighbours.Count; i++)
            {
                var link = zone.neighbours[i];
                if (link == null || link.zone == null) { issues.Add(Error(label, $"neighbours[{i}] has no zone (missing reference?).")); continue; }
                if (link.zone == zone) { issues.Add(Error(label, $"neighbours[{i}] links the zone to itself.")); continue; }
                if (link.offset.y != 0f) issues.Add(Error(label, $"neighbours[{i}] ({link.zone.id}) offset.y is {link.offset.y}; it must be 0."));

                var n = link.zone.ground;
                if (n == null || n.size.x <= 0f || n.size.y <= 0f) continue; // reported on the neighbour itself

                // Distance between the two ground rectangles: < 0 overlaps, 0 touches, > 0 leaves a gap.
                float gapX = Mathf.Abs(link.offset.x) - (g.size.x + n.size.x) * 0.5f;
                float gapZ = Mathf.Abs(link.offset.z) - (g.size.y + n.size.y) * 0.5f;
                float gap = Mathf.Max(gapX, gapZ);
                if (gap < -LinkTolerance)
                    issues.Add(Error(label, $"neighbours[{i}] ({link.zone.id}) ground overlaps this zone's ground by {-gap} m; the edges must touch."));
                else if (gap > LinkTolerance)
                    issues.Add(Error(label, $"neighbours[{i}] ({link.zone.id}) ground doesn't touch this zone's ground (gap {gap} m)."));
            }
        }

        [MenuItem("NightWorld/Validate Content")]
        public static void ValidateFromMenu()
        {
            var issues = ValidateProject();
            Report(issues);
            if (issues.All(i => i.Severity != Severity.Error))
                Debug.Log("[ContentValidator] No errors.");
        }

        /// <summary>Batch-mode entry point. Exits 1 if there are errors.</summary>
        public static void RunFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                var issues = ValidateProject();
                Report(issues);
                exitCode = issues.Any(i => i.Severity == Severity.Error) ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static string Format(IEnumerable<Issue> issues)
        {
            var sb = new StringBuilder();
            foreach (var issue in issues) sb.AppendLine(issue.ToString());
            return sb.ToString();
        }

        private static void Report(List<Issue> issues)
        {
            foreach (var issue in issues)
            {
                if (issue.Severity == Severity.Error) Debug.LogError("[ContentValidator] " + issue);
                else Debug.LogWarning("[ContentValidator] " + issue);
            }
        }

        private static string Label(Definition d)
        {
            string path = AssetDatabase.GetAssetPath(d);
            return string.IsNullOrEmpty(path) ? $"{d.GetType().Name} '{d.id}'" : path;
        }

        private static Issue Error(string asset, string message) => new Issue(Severity.Error, asset, message);

        private static Issue Warning(string asset, string message) => new Issue(Severity.Warning, asset, message);
    }
}
