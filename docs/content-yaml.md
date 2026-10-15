# Writing content as YAML (for agents)

The nightly builder can't open the Unity editor, so it adds content by writing `.asset` files
directly. Unity reads them on next import. CI's `ContentValidator` test catches mistakes.

**Copy an existing asset of the same type and edit it.** That's the safest way. Examples:
`Data/Items/wooden-sword.asset`, `Data/Enemies/meadow-slime.asset`,
`Data/Shops/harbor-outfitter.asset`, `Data/Zones/meadow-fields.asset`.

## Every new asset needs two files

1. `<id>.asset`: the content. `m_Name` must equal the file name, which must equal `id`.
2. `<id>.asset.meta`: gives the asset a GUID so other assets can reference it.

```yaml
fileFormatVersion: 2
guid: <32 lowercase hex chars, NEW and unique: python3 -c "import uuid;print(uuid.uuid4().hex)">
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Never reuse or change an existing GUID; every reference to that asset would break.

## Script GUIDs (the `m_Script` line)

| Type | Script GUID |
|---|---|
| `ItemDefinition` | `5e3a3a5427faa739666a2863392c263f` |
| `EnemyDefinition` | `5fec34f75235fd738dc7f29685f99052` |
| `ShopDefinition` | `8cf633378040821de1d7b5a95040d4dc` |
| `ZoneDefinition` | `c46237c3d1255c7dc44973e80aaf38dc` |
| `ProgressionSettings` | `8fdb4a4428ded21f6af0aa55a168ace4` |
| `PlayerSettings` | `ed6698ab44cf4c73b2a231c1c0468590` |

```yaml
  m_Script: {fileID: 11500000, guid: <script guid from the table>, type: 3}
```

## Value formats

| C# type | YAML |
|---|---|
| reference to another asset | `{fileID: 11400000, guid: <that asset's .meta guid>, type: 2}` |
| empty reference | `{fileID: 0}` |
| enum | its integer value (see the enum in C#) |
| bool | `0` or `1` |
| Vector2 / Vector3 | `{x: 1, y: 2}` / `{x: 1, y: 2, z: 3}` |
| Color | `{r: 0.5, g: 0.5, b: 0.5, a: 1}` |
| empty list | `[]` |
| list of objects | one `- field: value` block per element, indented 2 spaces under the key |

Enum values: `ItemType` Weapon 0, Armor 1, Consumable 2, Material 3. `Rarity` Common 0,
Uncommon 1, Rare 2. `ZoneType` Town 0, Enemy 1. `PlaceholderShape` None 0, Cube 1, Sphere 2,
Capsule 3, Cylinder 4.

## Checklist before you commit content
- [ ] `id`, `m_Name` and file name all match, kebab-case
- [ ] new `.meta` with a freshly generated GUID
- [ ] every `guid:` you reference exists in some `.meta` file (`grep -r <guid> game/Assets`)
- [ ] positions are inside the zone's ground (|x| ≤ size.x/2, |z| ≤ size.y/2)
- [ ] zones still have at least one screenshot point
