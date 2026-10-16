# Nightly runner (Raspberry Pi)

`nightly.sh` is the whole loop: pick a card → builder agent → wait for CI → reviewer agent →
report, then the next card, while the night's points (Night settings on the board) last. It's a
plain bash script so you can read exactly what happens at 2 a.m.

## One-time setup on the Pi

```bash
# Tools
sudo apt update && sudo apt install -y git git-lfs jq gh
git lfs install

# Claude Code (native installer), then sign in
curl -fsSL https://claude.ai/install.sh | bash
claude auth login
# For cron, a long-lived token is the most reliable:
#   claude setup-token    → put CLAUDE_CODE_OAUTH_TOKEN=... in ~/.config/nightworld/runner.env

# GitHub CLI: sign in, and let git push through it
gh auth login
gh auth setup-git

# A dedicated clone just for the runner (it gets reset every night, so don't work in it)
git clone https://github.com/<you>/nightworld.git ~/nightworld-runner

# Config
mkdir -p ~/.config/nightworld
cp ~/nightworld-runner/runner/.env.example ~/.config/nightworld/runner.env
chmod 600 ~/.config/nightworld/runner.env
nano ~/.config/nightworld/runner.env
chmod +x ~/nightworld-runner/runner/nightly.sh
```

Then add the cron line from `crontab.example` (pointing at `~/nightworld-runner/runner/nightly.sh`).

## Running on a Mac instead

macOS lacks `flock` and `timeout`. The script falls back to a `mkdir` lock, and to `gtimeout`
from `brew install coreutils`; without it the run has no time limits. Install the other tools with
`brew install git-lfs jq gh`.

## Run it by hand

```bash
~/nightworld-runner/runner/nightly.sh
```

Logs for each run go to `$LOG_DIR/<date_time>/`: `run.log`, `card.txt`, `builder.jsonl` (everything
the builder did, one event per line) and `builder.json` (its final result), `checks.txt`, `ci/`
(downloaded artifacts and screenshots), `review.txt`.

## Watching a night

You don't need to log in to the runner's machine. The runner sends its log to the board every
15 s, with one line per thing the builder does (files it reads and edits, commands it runs, what
it says), and uploads CI's zone screenshots. Open **Tonight** on the board
(`$BOARD_URL/tonight`): which card is building, its steps, the PR, the screenshots and the live
log. If the board is down the night still runs; it just isn't shown.

## Safety rails

- The builder can only use the tools listed in `nightly.sh` (`--allowedTools`); anything else
  is denied, not prompted (`--permission-prompts none`). It can push only to `night/*` branches.
- `.claude/settings.json` denies edits to `ThirdParty/`, and force pushes.
- CI's scope check fails any `night/*` PR that touches ThirdParty, CI or agent config.
- The reviewer is read-only and runs in a separate session from the builder.
- `flock` stops two runs from overlapping. Timeouts stop a stuck run.
- Nothing is merged automatically. You merge.

## Pi resources

One Claude Code session uses little CPU; the work happens in the cloud. Unity never runs on the
Pi. Expect each card to take 30–90 minutes, mostly waiting for CI; Night settings on the board cap
how many cards a night gets.
