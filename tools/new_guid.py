#!/usr/bin/env python3
"""Print fresh Unity GUIDs (32 lowercase hex chars), checked against every existing .meta.

    python3 tools/new_guid.py        # one guid
    python3 tools/new_guid.py 3      # three guids
"""
import re
import sys
import uuid
from pathlib import Path

ASSETS = Path(__file__).resolve().parent.parent / "game" / "Assets"
existing = set()
for meta in ASSETS.rglob("*.meta"):
    m = re.search(r"^guid:\s*([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="replace"), re.M)
    if m:
        existing.add(m.group(1))

count = int(sys.argv[1]) if len(sys.argv) > 1 else 1
made = 0
while made < count:
    g = uuid.uuid4().hex
    if g not in existing:
        existing.add(g)
        print(g)
        made += 1
