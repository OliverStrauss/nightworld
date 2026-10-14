#!/usr/bin/env python3
"""
Fast checks on the Unity project that don't need Unity. Run before every commit:

    python3 tools/check_assets.py

Checks:
  1. Every file and folder under game/Assets has a .meta (Unity silently regenerates missing
     ones with NEW guids, which breaks every reference to that asset).
  2. Every .meta has a matching file or folder (orphaned metas).
  3. No two .meta files share a GUID.
  4. Every guid referenced by a content asset in game/Assets/_Project/Data resolves to a .meta.
  5. Content assets: m_Name matches the file name, and `id` matches too for Definitions.

Exits 1 if anything fails. Standard library only, so it runs on the Pi, CI and a Mac.
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "game" / "Assets"
DATA = ASSETS / "_Project" / "Data"

GUID_LINE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)
GUID_REF = re.compile(r"guid:\s*([0-9a-f]{32})")
NAME_LINE = re.compile(r"^\s*m_Name:\s*(.*?)\s*$", re.MULTILINE)
ID_LINE = re.compile(r"^  id:\s*(.*?)\s*$", re.MULTILINE)

# Unity's own built-in resources; references to these are fine.
BUILTIN_GUIDS = {
    "0000000000000000e000000000000000",
    "0000000000000000f000000000000000",
    "0000000000000000d000000000000000",
}


def ignored(path: Path) -> bool:
    return any(part.startswith(".") or part.endswith("~") for part in path.relative_to(ASSETS).parts)


def main() -> int:
    errors: list[str] = []

    if not ASSETS.is_dir():
        print(f"No Unity Assets folder at {ASSETS}")
        return 1

    guid_owner: dict[str, Path] = {}

    for path in sorted(ASSETS.rglob("*")):
        if ignored(path):
            continue
        rel = path.relative_to(REPO)

        if path.suffix == ".meta":
            target = path.with_suffix("")
            if not target.exists():
                errors.append(f"orphaned meta (no matching file/folder): {rel}")
            text = path.read_text(encoding="utf-8", errors="replace")
            m = GUID_LINE.search(text)
            if not m:
                errors.append(f"meta without a valid 32-char hex guid: {rel}")
                continue
            guid = m.group(1)
            if guid in guid_owner:
                errors.append(f"duplicate guid {guid}: {rel} and {guid_owner[guid].relative_to(REPO)}")
            else:
                guid_owner[guid] = path
        else:
            meta = path.with_name(path.name + ".meta")
            if not meta.exists():
                errors.append(f"missing .meta: {rel}")

    if DATA.is_dir():
        for asset in sorted(DATA.rglob("*.asset")):
            if ignored(asset):
                continue
            rel = asset.relative_to(REPO)
            text = asset.read_text(encoding="utf-8", errors="replace")

            for guid in set(GUID_REF.findall(text)):
                if guid not in guid_owner and guid not in BUILTIN_GUIDS:
                    errors.append(f"{rel}: references guid {guid}, which no .meta file defines")

            name = NAME_LINE.search(text)
            if not name:
                errors.append(f"{rel}: no m_Name line")
            elif name.group(1) != asset.stem:
                errors.append(f"{rel}: m_Name '{name.group(1)}' should equal the file name '{asset.stem}'")

            ident = ID_LINE.search(text)
            if ident and ident.group(1) != asset.stem:
                errors.append(f"{rel}: id '{ident.group(1)}' should equal the file name '{asset.stem}'")

    if errors:
        print(f"check_assets: {len(errors)} problem(s)")
        for e in errors:
            print("  - " + e)
        return 1

    print(f"check_assets: OK ({len(guid_owner)} meta files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
