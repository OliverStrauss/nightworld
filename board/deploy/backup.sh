#!/usr/bin/env bash
# Copies the board's SQLite database to ~/nightworld-backups/board-<date>.db and keeps the last 14.
# Safe while the board is running (uses SQLite's online backup). Run from cron, see README.md.
set -euo pipefail

ENV_FILE="${BOARD_ENV:-$HOME/.config/nightworld/board.env}"
BACKUP_DIR="${BACKUP_DIR:-$HOME/nightworld-backups}"
KEEP="${KEEP:-14}"

DB=$(sed -n 's/^\(export \)\{0,1\}BOARD_DB=//p' "$ENV_FILE" | tail -n1 | tr -d "\"'")
DB="${DB/#\~/$HOME}"
[[ -f "$DB" ]] || { echo "No database at '$DB' (BOARD_DB in $ENV_FILE)" >&2; exit 1; }

mkdir -p "$BACKUP_DIR"
OUT="$BACKUP_DIR/board-$(date +%F).db"
python3 - "$DB" "$OUT" <<'PY'
import sqlite3, sys
src, dst = sqlite3.connect(sys.argv[1]), sqlite3.connect(sys.argv[2])
src.backup(dst)
dst.close(); src.close()
PY

ls -1t "$BACKUP_DIR"/board-*.db | tail -n +"$((KEEP + 1))" | xargs -r rm --
echo "Backed up $DB to $OUT"
