#!/usr/bin/env bash

if LC_ALL=C grep -q $'\r' "$0"; then
  if [[ "${1:-}" != "--__cr_stripped" ]]; then
    tmp="$(mktemp)"
    tr -d '\r' < "$0" > "$tmp"
    chmod +x "$tmp"
    exec bash "$tmp" --__cr_stripped "$@"
  fi
fi

if [[ "${1:-}" == "--__cr_stripped" ]]; then
  shift
fi

set -euo pipefail
esc=$'\033'
bel=$'\a'

usage() {
  echo "Usage: $0 /path/to/LongevityWorldCup.db" >&2
  exit 1
}

db_path="${1:-}"
if [[ -z "${db_path//[[:space:]]/}" ]]; then
  usage
fi

if [[ "$db_path" != /* ]]; then
  db_path="$(pwd -P)/$db_path"
fi

command -v sqlite3 >/dev/null 2>&1 || { echo "sqlite3 is not installed" >&2; exit 1; }

if [[ ! -f "$db_path" ]]; then
  echo "Database file not found: $db_path" >&2
  exit 1
fi

svc_user="$(stat -c %U "$db_path" 2>/dev/null || true)"
if [[ -z "${svc_user:-}" || "$svc_user" == "UNKNOWN" ]]; then
  svc_user="$(id -un)"
fi

as_svc() {
  if [[ "$(id -un)" == "$svc_user" ]]; then
    "$@"
    return
  fi

  if [[ $EUID -eq 0 ]]; then
    if command -v runuser >/dev/null 2>&1; then
      runuser -u "$svc_user" -- "$@"
      return
    fi
    if command -v su >/dev/null 2>&1; then
      su -s /bin/bash "$svc_user" -c "$(printf '%q ' "$@")"
      return
    fi
    echo "Cannot switch user to '$svc_user' (root, but no runuser/su)." >&2
    exit 1
  fi

  if command -v sudo >/dev/null 2>&1; then
    sudo -u "$svc_user" -- "$@"
    return
  fi

  echo "Cannot switch user to '$svc_user' (not root, no sudo)." >&2
  exit 1
}

sqlite_timeout_ms=15000
sqlite_max_retries=50
sqlite_retry_initial_ms=50
sqlite_retry_max_ms=500

fs_type() {
  stat -f -c %T "$1" 2>/dev/null || echo "unknown"
}

writable_path_as_svc() {
  as_svc test -w "$1" >/dev/null 2>&1
}

diag_io() {
  local dir wal shm owner group fstype
  dir="$(dirname "$db_path")"
  wal="${db_path}-wal"
  shm="${db_path}-shm"
  owner="$(stat -c %U "$db_path" 2>/dev/null || echo "?")"
  group="$(stat -c %G "$db_path" 2>/dev/null || echo "?")"
  fstype="$(fs_type "$dir")"

  echo ""
  echo "SQLite disk I/O error diagnostics:"
  echo "DB: $db_path"
  echo "Dir: $dir"
  echo "FS type: $fstype"
  echo "Owner: $owner  Group: $group"
  echo "DB user (dynamic): $svc_user"
  echo ""
  ls -la "$db_path" "$dir" 2>/dev/null || true
  [[ -e "$wal" ]] && ls -la "$wal" 2>/dev/null || true
  [[ -e "$shm" ]] && ls -la "$shm" 2>/dev/null || true
  echo ""
  echo "Writable checks for db user ($svc_user):"
  echo "  Dir writable: $(writable_path_as_svc "$dir" && echo yes || echo no)"
  echo "  DB writable:  $(writable_path_as_svc "$db_path" && echo yes || echo no)"
  echo "  WAL writable: $([[ -e "$wal" ]] && (writable_path_as_svc "$wal" && echo yes || echo no) || echo "(missing)")"
  echo "  SHM writable: $([[ -e "$shm" ]] && (writable_path_as_svc "$shm" && echo yes || echo no) || echo "(missing)")"
  echo ""
}

precheck_writeability() {
  local dir wal shm
  dir="$(dirname "$db_path")"
  wal="${db_path}-wal"
  shm="${db_path}-shm"

  if ! writable_path_as_svc "$dir"; then
    echo "DB directory is not writable for $svc_user: $dir" >&2
    diag_io
    exit 1
  fi

  if ! writable_path_as_svc "$db_path"; then
    echo "DB file is not writable for $svc_user: $db_path" >&2
    diag_io
    exit 1
  fi

  if [[ -e "$wal" ]] && ! writable_path_as_svc "$wal"; then
    echo "WAL file exists but is not writable for $svc_user: $wal" >&2
    diag_io
    exit 1
  fi

  if [[ -e "$shm" ]] && ! writable_path_as_svc "$shm"; then
    echo "SHM file exists but is not writable for $svc_user: $shm" >&2
    diag_io
    exit 1
  fi
}

has_events_table="$(as_svc sqlite3 "$db_path" "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Events' LIMIT 1;")"
if [[ "$has_events_table" != "1" ]]; then
  echo "Events table not found in database: $db_path" >&2
  exit 1
fi

render() {
  s="$1"
  prev=""
  while [[ "$prev" != "$s" ]]; do
    prev="$s"
    s="$(printf '%s' "$s" | sed -E "s/\[strong\]\(([^()]*)\)/${esc}[1m${esc}[95m\1${esc}[0m/g")"
  done
  prev=""
  while [[ "$prev" != "$s" ]]; do
    prev="$s"
    s="$(printf '%s' "$s" | sed -E "s/\[bold\]\(([^()]*)\)/${esc}[1m\1${esc}[0m/g")"
  done
  prev=""
  while [[ "$prev" != "$s" ]]; do
    prev="$s"
    s="$(printf '%s' "$s" | sed -E "s/\[([^][]+)\]\((https?:[^)]+)\)/${esc}]8;;\2${bel}\1${esc}]8;;${bel}/g")"
  done
  printf '%s' "$s"
}

printf 'Formatting examples\n\n'

printf 'Link\n'
printf 'Example: %s\n' "Visit [Longevity World Cup](https://longevityworldcup.com/) for the latest leaderboard."
printf 'Renders: '
render "Visit [Longevity World Cup](https://longevityworldcup.com/) for the latest leaderboard."
printf '\n\n'

printf 'Bold\n'
printf 'Example: %s\n' "This update highlights one [bold](important) change in the Longevity World Cup."
printf 'Renders: '
render "This update highlights one [bold](important) change in the Longevity World Cup."
printf '\n\n'

printf 'Strong\n'
printf 'Example: %s\n' "This is a [strong](major announcement) for the Longevity World Cup community."
printf 'Renders: '
render "This is a [strong](major announcement) for the Longevity World Cup community."
printf '\n\n'

read -r -p "Title: " title_raw
if [[ -z "${title_raw//[[:space:]]/}" ]]; then
  echo "Title is required" >&2
  exit 1
fi

printf '%s\n' "Content. End input with a single dot on its own line:"
content_raw=""
while IFS= read -r line; do
  [[ "$line" == "." ]] && break
  content_raw+="$line"$'\n'
done
content_raw="${content_raw%$'\n'}"

title_preview="$(render "$title_raw")"
content_preview="$(render "$content_raw")"

printf '\n'
printf 'Preview\n'
printf 'Title:\n%s\n\n' "$title_preview"
printf 'Content:\n%s\n\n' "$content_preview"

read -r -p "Proceed? [y/N] " ok
case "${ok:-}" in y|Y) :;; *) echo "Cancelled"; exit 0;; esac

read -r -p "Send to Slack? [Y/n] " send_slack
slack_processed=0
case "${send_slack:-}" in n|N) slack_processed=1;; esac

read -r -p "Send to X? [Y/n] " send_x
x_processed=0
case "${send_x:-}" in n|N) x_processed=1;; esac

combined_raw="$title_raw"$'\n\n'"$content_raw"

if command -v uuidgen >/dev/null 2>&1; then
  id="$(uuidgen | tr -d '-' | tr '[:upper:]' '[:lower:]')"
else
  id="$(cat /proc/sys/kernel/random/uuid 2>/dev/null | tr -d '-' | tr '[:upper:]' '[:lower:]' || date +%s%N)"
fi

esc_sql() { printf "%s" "$1" | sed "s/'/''/g"; }
txt="$(esc_sql "$combined_raw")"

precheck_writeability

is_retryable_err() {
  LC_ALL=C echo "$1" | grep -qiE "database is locked|database is busy|SQLITE_BUSY|SQLITE_LOCKED|disk I/O error"
}

set +e
err=""
attempt=0
delay_ms="$sqlite_retry_initial_ms"
while :; do
  out="$(as_svc sqlite3 -cmd ".timeout $sqlite_timeout_ms" "$db_path" "BEGIN IMMEDIATE; INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, SlackProcessed, XProcessed) VALUES ('$id', 6, '$txt', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 15, $slack_processed, $x_processed); COMMIT;" 2>&1)"
  rc=$?
  if [[ $rc -eq 0 ]]; then
    echo "Inserted $id into $db_path"
    set -e
    exit 0
  fi

  err="$out"

  if echo "$out" | grep -qi "UNIQUE constraint failed: Events.Id"; then
    echo "Inserted $id into $db_path"
    set -e
    exit 0
  fi

  if is_retryable_err "$out" && [[ $attempt -lt $sqlite_max_retries ]]; then
    attempt=$((attempt + 1))
    jitter=$((RANDOM % 25))
    sleep_s="$(awk -v ms="$((delay_ms + jitter))" 'BEGIN{printf "%.3f", ms/1000.0}')"
    sleep "$sleep_s"
    delay_ms=$((delay_ms * 2))
    if [[ $delay_ms -gt $sqlite_retry_max_ms ]]; then
      delay_ms="$sqlite_retry_max_ms"
    fi
    continue
  fi

  break
done
set -e

echo "$err" >&2
if echo "$err" | grep -qi "disk I/O error"; then
  diag_io
fi
exit 1
