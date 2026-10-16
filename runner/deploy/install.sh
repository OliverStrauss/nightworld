#!/usr/bin/env bash
# Installs (or updates) the night shift on the Pi: tools, runner.env, and a systemd timer that
# runs runner/nightly.sh every night. Run as your normal user from the runner's own clone:
#   ~/nightworld-runner/runner/deploy/install.sh
# Safe to run again after `git pull`: it only adds what's missing and reinstalls the units.
set -euo pipefail

RUNNER_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REPO_DIR="$(cd "$RUNNER_DIR/.." && pwd)"
ENV_FILE="${NIGHTWORLD_ENV:-$HOME/.config/nightworld/runner.env}"
BOARD_ENV="${BOARD_ENV:-$HOME/.config/nightworld/board.env}"
export PATH="$HOME/.local/bin:$PATH"

say() { printf '\n==> %s\n' "$*"; }
die() { printf '\n%s\n' "$*" >&2; exit 1; }
env_get() { sed -n "s/^\(export \)\{0,1\}$1=//p" "$2" 2>/dev/null | tail -n1 | tr -d "\"'"; }

# 1. 64-bit Linux (Claude Code has no 32-bit ARM build)
[[ "$(uname -m)" == aarch64 || "$(uname -m)" == x86_64 ]] ||
  die "Needs 64-bit Raspberry Pi OS; this is $(uname -m). Reflash with the 64-bit image."

# 2. Tools. flock and timeout come with Raspberry Pi OS (util-linux, coreutils).
missing=()
for tool in git git-lfs jq gh curl; do
  command -v "$tool" >/dev/null || missing+=("$tool")
done
if ((${#missing[@]})); then
  say "Installing ${missing[*]}"
  sudo apt-get update -q && sudo apt-get install -y "${missing[@]}"
fi
git lfs install >/dev/null
if ! command -v claude >/dev/null; then
  say "Installing Claude Code"
  curl -fsSL https://claude.ai/install.sh | bash
fi

# 3. runner.env (first run only). The tokens come from the board on this Pi.
if [[ ! -f "$ENV_FILE" ]]; then
  mkdir -p "$(dirname "$ENV_FILE")"
  sed -e "s|^REPO_DIR=.*|REPO_DIR=$REPO_DIR|" -e "s|/home/oliver|$HOME|g" \
    "$RUNNER_DIR/.env.example" > "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  if [[ -f "$BOARD_ENV" ]]; then
    port=$(env_get BOARD_PORT "$BOARD_ENV")
    sed -i -e "s|^BOARD_URL=.*|BOARD_URL=http://localhost:${port:-8787}|" \
      -e "s|^BUILDER_TOKEN=.*|BUILDER_TOKEN=$(env_get BOARD_TOKEN_BUILDER "$BOARD_ENV")|" \
      -e "s|^REVIEWER_TOKEN=.*|REVIEWER_TOKEN=$(env_get BOARD_TOKEN_REVIEWER "$BOARD_ENV")|" \
      "$ENV_FILE"
    say "Created $ENV_FILE (board address and tokens taken from $BOARD_ENV)"
  else
    say "Created $ENV_FILE"
  fi
fi
grep -q 'change-me' "$ENV_FILE" &&
  die "$ENV_FILE still has 'change-me' values (no board.env on this Pi?). Fill them in:
  nano $ENV_FILE
then run this script again."
[[ "$(env_get REPO_DIR "$ENV_FILE")" == "$REPO_DIR" ]] ||
  die "REPO_DIR in $ENV_FILE is '$(env_get REPO_DIR "$ENV_FILE")', but this clone is $REPO_DIR.
Set REPO_DIR=$REPO_DIR there (the runner resets that clone every night), then run this again."
LOG_DIR="$(env_get LOG_DIR "$ENV_FILE")"
mkdir -p "${LOG_DIR:-$HOME/nightworld-logs}"

# 4. Sign-ins. Both are needed by the timer, which has no terminal to ask you on.
gh auth status >/dev/null 2>&1 ||
  die "GitHub CLI isn't signed in. Run these two, then this script again:
  gh auth login
  gh auth setup-git"
git config --global credential.https://github.com.helper >/dev/null || gh auth setup-git
say "Checking that Claude Code works without a terminal"
# shellcheck source=/dev/null
if ! (set -a; source "$ENV_FILE"; set +a
      cd "$REPO_DIR" && timeout 180 claude -p --max-turns 1 "Reply with just: ok" </dev/null) >/dev/null 2>&1; then
  die "Claude Code didn't answer (not signed in for $(id -un), or no internet). Run this, sign in,
then run this script again:
  ~/.local/bin/claude auth login"
fi

# 5. systemd service + timer
RUN_AT="$(env_get RUN_AT "$ENV_FILE")"
RUN_AT="${RUN_AT:-02:13}"
[[ "$RUN_AT" =~ ^[0-2][0-9]:[0-5][0-9]$ ]] || die "RUN_AT in $ENV_FILE should look like 02:13, not '$RUN_AT'."
chmod +x "$RUNNER_DIR/nightly.sh"
say "Installing the night shift timer (every night at $RUN_AT, $(date +%Z) time)"
sed -e "s|@USER@|$(id -un)|g" -e "s|@HOME@|$HOME|g" -e "s|@ENV_FILE@|$ENV_FILE|g" \
    -e "s|@RUNNER@|$RUNNER_DIR/nightly.sh|g" \
    "$RUNNER_DIR/deploy/nightworld-night.service" | sudo tee /etc/systemd/system/nightworld-night.service >/dev/null
sed -e "s|@RUN_AT@|$RUN_AT|g" \
    "$RUNNER_DIR/deploy/nightworld-night.timer" | sudo tee /etc/systemd/system/nightworld-night.timer >/dev/null
sudo systemctl daemon-reload
sudo systemctl enable -q --now nightworld-night.timer

say "The night shift is installed."
systemctl list-timers nightworld-night.timer --no-pager | head -n 2
cat <<MSG

Start a run now:   sudo systemctl start --no-block nightworld-night
Watch it live:     journalctl -u nightworld-night -f
Last night's logs: ls ${LOG_DIR:-$HOME/nightworld-logs}
Skip nights:       pause the night shift in the board's Night settings
MSG
