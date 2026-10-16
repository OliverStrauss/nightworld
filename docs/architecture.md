# Architecture

## The one big idea: the world is data

Agents are good at writing code and structured data. They are bad at editing Unity scene and
prefab YAML, which is huge, full of file IDs, and breaks silently. So:

- **Definitions** (C# `ScriptableObject` classes) describe *kinds of things*:
  `ItemDefinition`, `EnemyDefinition`, `ShopDefinition`, `ZoneDefinition`.
- **Content** is assets of those types in `Data/`. Adding an enemy = adding an asset.
- **Zones are built at runtime.** `ZoneBuilder` takes a `ZoneDefinition` and spawns the ground,
  props, enemy spawners and shop triggers. The only scene is `Boot`, which loads a zone.
- **Prefabs** are thin wrappers around asset-pack models plus our components. They're made
  once by a human (or an editor script) and then only *referenced* by data.

```
ZoneDefinition (asset)
 ├── ground: size, colour, height noise
 ├── props[]:        prefab + position + rotation + scale
 ├── enemySpawns[]:  EnemyDefinition + position + radius + count
 ├── shops[]:        ShopDefinition + position
 ├── playerSpawn
 └── screenshotPoints[]: camera pose used by CI to photograph the zone
          │
          ▼
     ZoneBuilder.Build(zone)  →  GameObjects in the scene
```

## Assemblies

| Assembly | Folder | References |
|---|---|---|
| `NightWorld.Runtime` | `Scripts/Runtime` | — |
| `NightWorld.Editor` | `Scripts/Editor` | Runtime (editor only) |
| `NightWorld.Tests.EditMode` | `Tests/EditMode` | Runtime, Editor, test framework |
| `NightWorld.Tests.PlayMode` | `Tests/PlayMode` | Runtime, test framework |

Separate assemblies keep compile times short and stop runtime code from depending on editor
code, which would break builds.

## Systems (runtime)

| Folder | Responsibility |
|---|---|
| `Core/` | `Definition` base class (id + displayName); an ID lookup database is a future card |
| `Data/` | Definition classes |
| `World/` | `ZoneBuilder`, `EnemySpawner`, `ShopTrigger`, `GameBootstrap` |
| `Player/` | Movement, camera |
| `Input/` | `ControlSettings` (default bindings as data), `GameControls` (the `player` action map) |
| `Combat/` | `Health`, `Enemy` (AI and attacks to be built) |
| `Progression/` | `ProgressionSettings` (XP curve), `PlayerProgression`, `RewardSystem` (inventory to be built) |
| `UI/` | `Hud` (HP, level, XP and gold; builds its own uGUI canvas from code) |

Keep systems talking through small interfaces or C# events, not by finding each other with
`FindObjectOfType`.

## Editor tooling (used by CI and agents)

- `ContentValidator` — also runs as the EditMode test `AllProjectContent_IsValid`, so every CI run checks content. Checks every definition: unique IDs, no missing references, enemies have
  loot, zones have spawn + screenshot points. Runs in batch mode:
  `-executeMethod NightWorld.EditorTools.ContentValidator.RunFromCommandLine`
- `ZoneScreenshotter` — builds each zone in an empty scene and renders PNGs from its
  screenshot points into `Screenshots/`. Runs in batch mode:
  `-executeMethod NightWorld.EditorTools.ZoneScreenshotter.RunFromCommandLine`
- `BootSceneCreator` — menu NightWorld → Setup → Create Boot Scene. Builds the only scene from
  code so nobody hand-edits scene YAML.
- `WrapperPrefabCreator` — menu NightWorld → Setup → Create Wrapper Prefabs. Wraps every FBX
  under `ThirdParty/` in a prefab variant with colliders under `_Project/Prefabs/`, mirroring the
  folder layout. Existing prefabs are skipped. Data references these prefabs, never raw models.
- `ThirdPartyImportScale` — sets each pack's model import scale so 1 unit = 1 metre. Add a line
  when a new pack imports at the wrong size.

Agents add content by writing `.asset` YAML directly; see [content-yaml.md](content-yaml.md).
