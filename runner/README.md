# Nightly runner (Raspberry Pi)

`nightly.sh` is the whole loop: pick a card → builder agent → wait for CI → reviewer agent →
report, then the next card, while the night's points (Night settings on the board) last. It's a
plain bash script so you can read exactly what happens at 2 a.m.

## Setup on the Pi

Needs 64-bit Raspberry Pi OS. Unity never runs here: CI builds, tests and screenshots every PR,
so the Pi only needs git, `gh`, `jq` and Claude Code. If the board already runs on this Pi,
`install.sh` takes the builder and reviewer tokens from its `board.env`.

```bash
git clone https://github.com/OliverStrauss/nightworld.git ~/nightworld-runner
~/nightworld-runner/runner/deploy/install.sh
```

If GitHub or Claude Code isn't signed in yet, it stops and prints the command to run
(`gh auth login` + `gh auth setup-git`, or `~/.local/bin/claude auth login`). Run that, then
`install.sh` again.

The runner gets its own clone; it's reset to `origin/main` every night, so don't work in it.
`install.sh` installs any missing tools, writes `~/.config/nightworld/runner.env`, checks both
sign-ins, and installs a systemd timer that starts `nightly.sh` every night at 02:13 (Pi time;
check it with `timedatectl`). To change the time, set `RUN_AT=` in `runner.env` and run
`install.sh` again. Run it again after `git pull` too; it's safe to repeat.

```bash
sudo systemctl start --no-block nightworld-night   # start a run now
journalctl -u nightworld-night -f                  # watch it live
systemctl list-timers nightworld-night.timer       # when the next run is
sudo systemctl stop nightworld-night               # stop a run (builder and reviewer too)
```

To skip nights, pause the night shift in the board's Night settings. To turn the timer off:
`sudo systemctl disable --now nightworld-night.timer`.

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
