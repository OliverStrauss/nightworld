#!/usr/bin/env bash
# Installs (or updates) the task board as a systemd service on the Pi.
# Run as your normal user from a clone of the repo:  ~/nightworld-board/board/deploy/install.sh
# Safe to run again after `git pull`: it updates the venv and restarts the service.
set -euo pipefail

BOARD_DIR="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="${BOARD_ENV:-$HOME/.config/nightworld/board.env}"
UNIT=/etc/systemd/system/nightworld-board.service

say() { printf '\n==> %s\n' "$*"; }

# 1. Python 3.11+ (Raspberry Pi OS Bookworm has 3.11)
python3 -c 'import sys; sys.exit(sys.version_info < (3, 11))' || {
  echo "Needs Python 3.11+, found $(python3 --version). Use Raspberry Pi OS Bookworm (12) or newer." >&2
  exit 1
}
python3 -c 'import venv, ensurepip' 2>/dev/null || {
  say "Installing python3-venv"
  sudo apt-get update -q && sudo apt-get install -y python3-venv
}

# 2. Virtualenv with the board's dependencies
say "Installing the board into $BOARD_DIR/.venv"
[[ -d "$BOARD_DIR/.venv" ]] || python3 -m venv "$BOARD_DIR/.venv"
"$BOARD_DIR/.venv/bin/pip" install -q --upgrade pip
"$BOARD_DIR/.venv/bin/pip" install -q -e "$BOARD_DIR"

# 3. Config (first run only)
if [[ ! -f "$ENV_FILE" ]]; then
  mkdir -p "$(dirname "$ENV_FILE")"
  sed -e "s|/home/oliver|$HOME|g" "$BOARD_DIR/deploy/board.env.example" > "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  say "Created $ENV_FILE"
  echo "Fill it in (or paste in your laptop's board.env), then run this script again:"
  echo "  nano $ENV_FILE"
  exit 0
fi
# systemd reads this file itself: no `export`, and no shell tricks.
sed -i 's/^export //' "$ENV_FILE"
if grep -q 'change-me' "$ENV_FILE"; then
  echo "$ENV_FILE still has 'change-me' values. Fill them in, then run this script again." >&2
  exit 1
fi

mkdir -p "$HOME/nightworld-backups"   # backup.sh and its cron log go here

# 4. systemd service
say "Installing $UNIT"
sed -e "s|@USER@|$(id -un)|g" -e "s|@HOME@|$HOME|g" \
    -e "s|@BOARD_DIR@|$BOARD_DIR|g" -e "s|@ENV_FILE@|$ENV_FILE|g" \
    "$BOARD_DIR/deploy/nightworld-board.service" | sudo tee "$UNIT" > /dev/null
sudo systemctl daemon-reload
sudo systemctl enable -q nightworld-board
sudo systemctl restart nightworld-board

# 5. Is it up?
PORT=$(sed -n 's/^BOARD_PORT=//p' "$ENV_FILE" | tail -n1 | tr -d "\"'")
PORT="${PORT:-8787}"
for _ in $(seq 1 20); do
  if curl -fs -o /dev/null "http://127.0.0.1:$PORT/login"; then
    say "The board is running: http://$(hostname).local:$PORT"
    echo "Logs: journalctl -u nightworld-board -f"
    exit 0
  fi
  sleep 1
done
echo "The board didn't answer on port $PORT. See: journalctl -u nightworld-board -n 50" >&2
exit 1
