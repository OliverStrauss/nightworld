#!/usr/bin/env bash
# NightWorld nightly runner. Runs on the Raspberry Pi from cron (see crontab.example).
#
# Reads tonight's budget from the board (points, max cards, stop time), then for each Ready card
# that fits in the points left:
#   1. take the card from the board
#   2. builder agent implements it on night/card-<id> and opens a PR
#   3. wait for GitHub Actions (asset checks, tests, screenshots)
#   4. reviewer agent (fresh session) judges the PR against the card
#   5. post the verdict to the PR and the board
# until the points or the card cap run out, nothing in Ready fits, or the stop time has passed.
#
# Config lives OUTSIDE the repo, in ~/.config/nightworld/runner.env (copy runner/.env.example),
# because the runner's clone is reset and cleaned every night.
# Run by hand with:  runner/nightly.sh
#
# Everything is inside main() so bash parses the whole script before running it; the script
# resets its own git checkout partway through, and that must not change the code mid-run.
set -euo pipefail

main() {
ENV_FILE="${NIGHTWORLD_ENV:-$HOME/.config/nightworld/runner.env}"
# shellcheck source=/dev/null
set -a
source "$ENV_FILE"
set +a

: "${REPO_DIR:?set REPO_DIR in $ENV_FILE}"
: "${BOARD_URL:?set BOARD_URL in $ENV_FILE}"
: "${BUILDER_TOKEN:?set BUILDER_TOKEN in $ENV_FILE}"
: "${REVIEWER_TOKEN:?set REVIEWER_TOKEN in $ENV_FILE}"
LOG_DIR="${LOG_DIR:-$HOME/nightworld-logs}"
BUILDER_MAX_TURNS="${BUILDER_MAX_TURNS:-120}"
REVIEWER_MAX_TURNS="${REVIEWER_MAX_TURNS:-40}"
BUILDER_TIMEOUT="${BUILDER_TIMEOUT:-3h}"
CI_TIMEOUT="${CI_TIMEOUT:-90m}"

STAMP="$(date +%F_%H%M)"
RUN="$LOG_DIR/$STAMP"   # one folder per night, with a card-<id> folder per card
mkdir -p "$RUN"
exec > >(tee -a "$RUN/run.log") 2>&1

log() { echo "[$(date +%T)] $*"; }

# On any exit: free the macOS lock, stop the log shipper and tell the board the night is over.
on_exit() {
  local code=$?
  [ -n "${LOCK_DIR:-}" ] && rmdir "$LOCK_DIR"
  [ -n "${SHIPPER:-}" ] && kill "$SHIPPER" 2>/dev/null
  if [ -n "${NIGHT_ID:-}" ]; then
    sleep 1   # let tee write the last lines
    ship_log 2>/dev/null || true
    board "$BUILDER_TOKEN" -X POST "$BOARD_URL/api/nights/$NIGHT_ID/finish" \
      -d "$(jq -n --argjson c "$code" --arg m "$(tail -n 1 "$RUN/run.log")" \
        '{status: (if $c == 0 then "finished" else "failed" end), message: $m}')" >/dev/null 2>&1 || true
  fi
}
trap on_exit EXIT

# Only one run at a time, even if cron fires twice or you run it by hand.
# macOS has no flock(1); fall back to an atomic mkdir lock there.
if command -v flock >/dev/null; then
  exec 9>"$LOG_DIR/.lock"
  if ! flock -n 9; then
    log "Another run is in progress; exiting."
    exit 0
  fi
elif mkdir "$LOG_DIR/.lock.d" 2>/dev/null; then
  LOCK_DIR="$LOG_DIR/.lock.d"
else
  log "Another run is in progress (or a stale $LOG_DIR/.lock.d); exiting."
  exit 0
fi

# macOS has no timeout(1); Homebrew coreutils installs it as gtimeout.
if ! command -v timeout >/dev/null; then
  if command -v gtimeout >/dev/null; then
    timeout() { gtimeout "$@"; }
  else
    log "No timeout(1) or gtimeout found (brew install coreutils); running without time limits."
    timeout() { shift; "$@"; }
  fi
fi

board() { # board <token> <curl args...>
  local token="$1"; shift
  curl -fsS -H "Authorization: Bearer $token" -H "Content-Type: application/json" "$@"
}
event() { # event <card> <type> <message>
  board "$BUILDER_TOKEN" -X POST "$BOARD_URL/api/cards/$1/events" \
    -d "$(jq -n --arg t "$2" --arg m "$3" '{type: $t, message: $m}')" >/dev/null || true
}

# ---------------------------------------------------------------- tonight's budget
board "$BUILDER_TOKEN" "$BOARD_URL/api/settings/night" > "$RUN/night.json"
if [ "$(jq -r '.paused' "$RUN/night.json")" = true ]; then
  log "The night shift is paused. Good night."
  exit 0
fi
POINTS_LEFT=$(jq -r '.pointsPerNight' "$RUN/night.json")
CARDS_LEFT=$(jq -r '.maxCards' "$RUN/night.json")
STOP=$(jq -r '.stopTime' "$RUN/night.json")
START=$(date +%H:%M)
log "Tonight: $POINTS_LEFT points, up to $CARDS_LEFT cards, no new card after $STOP"

# ---------------------------------------------------------------- live on the board (Tonight page)
# The board gets this night's run.log every 15 s; an empty send is the heartbeat behind
# "last heard from". If the board can't be reached, the night goes on without it.
ship_log() { # send the run.log lines the board hasn't seen yet
  local sent total
  sent=$(cat "$RUN/.shipped" 2>/dev/null || echo 0)
  total=$(($(wc -l < "$RUN/run.log")))   # $((…)) drops the padding macOS wc adds
  if [ "$total" -gt "$sent" ]; then sed -n "$((sent + 1)),${total}p" "$RUN/run.log"; fi |
    jq -Rn '{lines: [inputs]}' |
    board "$BUILDER_TOKEN" -X POST "$BOARD_URL/api/nights/$NIGHT_ID/log" -d @- >/dev/null &&
    echo "$total" > "$RUN/.shipped"
}
NIGHT_ID=$(board "$BUILDER_TOKEN" -X POST "$BOARD_URL/api/nights" \
  -d "$(jq --arg h "$(hostname -s)" '{pointsPerNight, maxCards, stopTime, host: $h}' "$RUN/night.json")" |
  jq -r '.id' || true)
if [ -n "$NIGHT_ID" ]; then
  log "Watch live: $BOARD_URL/nights/$NIGHT_ID"
  ( while sleep 15; do ship_log 2>/dev/null || true; done ) 9>&- &
  SHIPPER=$!
else
  log "Couldn't tell the board this night started; it won't show live."
fi

# Still before the stop time? HH:MM strings sort in time order. A run that starts after the stop
# time (say 22:00 with stop 06:00) stops tomorrow morning.
before_stop() {
  local now; now=$(date +%H:%M)
  if [[ "$START" < "$STOP" ]]; then [[ "$now" < "$STOP" ]]
  else [[ ! "$now" < "$START" || "$now" < "$STOP" ]]; fi
}

# Steps 1–5 for the card in $RUN/next.json, with its logs in $WORK. Sets CARD_POINTS.
# Not indented, like main(): it's the old one-card script, now run once per card.
build_card() {
# ---------------------------------------------------------------- 1. take the card
CARD_ID=$(jq -r '.id' "$RUN/next.json")
TITLE=$(jq -r '.title' "$RUN/next.json")
BRANCH="night/card-$CARD_ID"
WORK="$RUN/card-$CARD_ID"
mkdir -p "$WORK"
log "Card $CARD_ID: $TITLE"

board "$BUILDER_TOKEN" -X POST "$BOARD_URL/api/cards/$CARD_ID/claim" \
  -d "$(jq -n --arg b "$BRANCH" '{branch: $b}')" > "$WORK/card.json"
CARD_POINTS=$(jq -r '.points' "$WORK/card.json")

# ---------------------------------------------------------------- 2. build
cd "$REPO_DIR"   # a clone used ONLY by this runner; local changes here get thrown away
git fetch origin --prune
git checkout -q main
git reset -q --hard origin/main
git clean -qfd
if git ls-remote --exit-code --heads origin "$BRANCH" >/dev/null 2>&1; then
  log "Continuing existing branch $BRANCH (retry after review feedback)"
  git checkout -q -B "$BRANCH" "origin/$BRANCH"
else
  git checkout -q -B "$BRANCH"   # -B: a local branch may linger from a run that never pushed
fi

CARD_TEXT=$(jq -r '
  "Card \(.id): \(.title)\nType: \(.type)   Points: \(.points)   Attempt: \(.attempts)\n\n" +
  "Goal:\n\(.goal)\n\nAcceptance criteria:\n" +
  ([.criteria[] | "- [ ] " + .] | join("\n")) +
  "\n\nNotes:\n\(.notes // "none")" +
  (if .lastReview then "\n\nREVIEWER FEEDBACK FROM THE LAST ATTEMPT (fix these first):\n\(.lastReview)" else "" end)
' "$WORK/card.json")
printf '%s\n' "$CARD_TEXT" > "$WORK/card.txt"

event "$CARD_ID" builder_started "Builder started on $BRANCH"
# One log line per thing the builder does (edits, commands, what it says), so the Tonight page
# shows it working. fromjson? skips anything on stdout that isn't a stream-json event.
narrate() {
  jq --unbuffered -rR 'fromjson? | select(.type == "assistant") | .message.content[]? |
    if .type == "text" then "builder: " + (.text | gsub("\\s+"; " ") | .[0:240])
    elif .type == "tool_use" then "builder → " + .name + " " +
      ((.input.file_path // .input.command // .input.pattern // .input.path // "") | tostring | gsub("\\s+"; " ") | .[0:200])
    else empty end' |
  while IFS= read -r line; do log "  $line"; done
}
log "Running builder agent"
set +e
timeout "$BUILDER_TIMEOUT" claude -p \
  --agent builder \
  --permission-mode acceptEdits \
  --permission-prompts none \
  --max-turns "$BUILDER_MAX_TURNS" \
  --allowedTools "Read" "Write" "Edit" "Glob" "Grep" \
    "Bash(git status)" "Bash(git status *)" "Bash(git diff)" "Bash(git diff *)" "Bash(git log *)" "Bash(git add *)" \
    "Bash(git commit *)" "Bash(git push origin night/*)" "Bash(git push -u origin night/*)" \
    "Bash(gh pr create *)" "Bash(gh pr edit *)" "Bash(gh pr view *)" "Bash(gh pr list *)" \
    "Bash(python3 tools/check_assets.py)" "Bash(python3 tools/new_guid.py *)" \
    "Bash(mkdir *)" \
  --output-format stream-json --verbose \
  "Branch: $BRANCH (already checked out). Implement this card, push the branch, and open or update the PR.

Shell rules: nobody can approve commands, so anything not on the allowlist is denied. Run one plain
command per Bash call from the repo root, exactly in these forms: git status/diff/log/add/commit,
git push -u origin $BRANCH, gh pr create/edit/view/list, python3 tools/check_assets.py,
python3 tools/new_guid.py, mkdir. No cd, git -C, absolute paths, pipes, ;, && or echo.
If a command is denied, rephrase it to one of those forms; don't give up.

$CARD_TEXT" | tee "$WORK/builder.jsonl" | narrate
builder_exit=${PIPESTATUS[0]}
set -e
jq -cR 'fromjson? | select(.type == "result")' "$WORK/builder.jsonl" | tail -n 1 > "$WORK/builder.json"
log "Builder exited with $builder_exit"
jq -r '.result // empty' "$WORK/builder.json" > "$WORK/builder.txt" 2>/dev/null || true

PR=$(gh pr list --head "$BRANCH" --state open --json number -q '.[0].number' || true)
if [ -z "$PR" ]; then
  log "No PR was opened."
  # Notes become the next attempt's "reviewer feedback", so don't pass a crash or rate limit off as one.
  if [ "$builder_exit" != 0 ] || [ "$(jq -r '.is_error // false' "$WORK/builder.json" 2>/dev/null)" = true ]; then
    printf 'Runner: the builder did not finish (exit %s), e.g. a session limit or crash. Not a code review; just retry.\n' \
      "$builder_exit" > "$WORK/builder.txt"
  fi
  board "$REVIEWER_TOKEN" -X POST "$BOARD_URL/api/cards/$CARD_ID/review" \
    -d "$(jq -n --rawfile n "$WORK/builder.txt" '{verdict: "BLOCKED", notes: ("Builder did not open a PR.\n\n" + $n)}')" >/dev/null || true
  return 0   # the card is blocked; its points are still spent
fi
PR_URL=$(gh pr view "$PR" --json url -q .url)
log "PR #$PR: $PR_URL"
board "$BUILDER_TOKEN" -X PATCH "$BOARD_URL/api/cards/$CARD_ID" \
  -d "$(jq -n --arg u "$PR_URL" '{status: "verifying", prUrl: $u}')" >/dev/null

# ---------------------------------------------------------------- 3. wait for CI
log "Waiting for CI"
event "$CARD_ID" ci_started "Waiting for CI on PR #$PR"
sleep 30   # give GitHub a moment to register the checks
set +e
timeout "$CI_TIMEOUT" gh pr checks "$PR" --watch --interval 60 > "$WORK/checks.txt" 2>&1
ci_exit=$?
set -e
gh pr checks "$PR" > "$WORK/checks.txt" 2>&1 || true
log "CI finished (exit $ci_exit)"
case $ci_exit in
  0) event "$CARD_ID" ci_passed "CI passed" ;;
  124) event "$CARD_ID" ci_failed "CI didn't finish within $CI_TIMEOUT" ;;
  *) event "$CARD_ID" ci_failed "CI failed (gh pr checks exit $ci_exit)" ;;
esac

mkdir -p "$WORK/ci"
RUN_ID=$(gh run list --branch "$BRANCH" --workflow unity-ci.yml --limit 1 --json databaseId -q '.[0].databaseId' || true)
if [ -n "$RUN_ID" ]; then
  gh run download "$RUN_ID" -D "$WORK/ci" || log "Some artifacts were missing"
  gh run view "$RUN_ID" --log-failed > "$WORK/ci/failed-steps.log" 2>/dev/null || true
fi
# Zone screenshots to the board, so they show on the Tonight and card pages.
if [ -n "$NIGHT_ID" ] && [ -d "$WORK/ci/zone-screenshots" ]; then
  shots=0
  while IFS= read -r -d '' png; do
    name="${png#"$WORK/ci/zone-screenshots/"}"
    curl -fsS -X POST -H "Authorization: Bearer $BUILDER_TOKEN" -H "Content-Type: image/png" --data-binary "@$png" \
      "$BOARD_URL/api/cards/$CARD_ID/screenshots?night=$NIGHT_ID&name=$(jq -rn --arg n "$name" '$n | @uri')" \
      >/dev/null && shots=$((shots + 1))
  done < <(find "$WORK/ci/zone-screenshots" -name '*.png' -print0 | sort -z)
  log "Sent $shots screenshot(s) to the board"
fi

# ---------------------------------------------------------------- 4. review
log "Running reviewer agent"
event "$CARD_ID" reviewer_started "Reviewer started on PR #$PR"
set +e
timeout 45m claude -p \
  --agent reviewer \
  --permission-mode dontAsk \
  --permission-prompts none \
  --max-turns "$REVIEWER_MAX_TURNS" \
  --add-dir "$WORK" \
  --allowedTools "Read" "Glob" "Grep" \
    "Bash(git diff *)" "Bash(git log *)" "Bash(git show *)" \
    "Bash(gh pr view *)" "Bash(gh pr diff *)" \
  --output-format text \
  "Review PR #$PR on branch $BRANCH against main.

Task card:
$CARD_TEXT

CI check summary:
$(cat "$WORK/checks.txt")

CI artifacts (test results, zone screenshots, failed-step logs) are in: $WORK/ci
Builder's own summary is in: $WORK/builder.txt (claims, not evidence)." > "$WORK/review.txt"
set -e

VERDICT=$(grep -m1 -oE '^VERDICT: *(PASS|FAIL|BLOCKED)' "$WORK/review.txt" | awk '{print $2}' || true)
if [ -z "$VERDICT" ]; then
  VERDICT="BLOCKED"
  printf '\n\n(Runner: reviewer output had no VERDICT line, treating as BLOCKED.)\n' >> "$WORK/review.txt"
fi
log "Verdict: $VERDICT"

# ---------------------------------------------------------------- 5. report
gh pr comment "$PR" --body-file <(printf '## Reviewer agent: %s\n\n%s\n' "$VERDICT" "$(cat "$WORK/review.txt")") || true
board "$REVIEWER_TOKEN" -X POST "$BOARD_URL/api/cards/$CARD_ID/review" \
  -d "$(jq -n --arg v "$VERDICT" --rawfile n "$WORK/review.txt" --arg u "$PR_URL" '{verdict: $v, notes: $n, prUrl: $u}')" >/dev/null

log "Card $CARD_ID finished. Logs: $WORK"
}

TRIED=" "
while [ "$CARDS_LEFT" -gt 0 ] && [ "$POINTS_LEFT" -gt 0 ] && before_stop; do
  log "Asking the board for the next Ready card that fits in $POINTS_LEFT points"
  status=$(curl -sS -o "$RUN/next.json" -w '%{http_code}' \
    -H "Authorization: Bearer $BUILDER_TOKEN" "$BOARD_URL/api/cards/next?points=$POINTS_LEFT")
  if [ "$status" = "204" ]; then
    log "Nothing in Ready fits in $POINTS_LEFT points."
    break
  elif [ "$status" != "200" ]; then
    log "Board returned HTTP $status; giving up."
    exit 1
  fi
  # A FAIL puts the card back on top of Ready; its retry is tomorrow's (board/README.md).
  if [[ "$TRIED" == *" $(jq -r '.id' "$RUN/next.json") "* ]]; then
    log "The next card already failed review tonight; it gets its retry tomorrow."
    break
  fi
  build_card
  TRIED="$TRIED$CARD_ID "
  POINTS_LEFT=$((POINTS_LEFT - CARD_POINTS))
  CARDS_LEFT=$((CARDS_LEFT - 1))
done
log "Good night. Logs: $RUN"
}

main "$@"
