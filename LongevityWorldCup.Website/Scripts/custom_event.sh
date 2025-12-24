#!/usr/bin/env bash

if [[ "${LWC_CR_STRIPPED:-0}" != "1" ]] && LC_ALL=C grep -q $'\r' "$0"; then
  export LWC_CR_STRIPPED=1
  tmp="$(mktemp)"
  tr -d '\r' < "$0" > "$tmp"
  chmod +x "$tmp"
  exec bash "$tmp" "$@"
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

command -v sqlite3 >/dev/null 2>&1 || { echo "sqlite3 is not installed" >&2; exit 1; }

if [[ ! -f "$db_path" ]]; then
  echo "Database file not found: $db_path" >&2
  exit 1
fi

sqlite_timeout_ms="${LWC_SQLITE_TIMEOUT_MS:-5000}"

fs_type() {
  stat -f -c %T "$1" 2>/dev/null || echo "unknown"
}

writable_path() {
  local p="$1"
  [[ -e "$p" ]] && [[ -w "$p" ]]
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
  echo ""
  ls -la "$db_path" "$dir" 2>/dev/null || true
  [[ -e "$wal" ]] && ls -la "$wal" 2>/dev/null || true
  [[ -e "$shm" ]] && ls -la "$shm" 2>/dev/null || true
  echo ""
  echo "Writable checks:"
  echo "  DB writable:  $(writable_path "$db_path" && echo yes || echo no)"
  echo "  Dir writable: $(writable_path "$dir" && echo yes || echo no)"
  echo "  WAL writable: $([[ -e "$wal" ]] && (writable_path "$wal" && echo yes || echo no) || echo "(missing)")"
  echo "  SHM writable: $([[ -e "$shm" ]] && (writable_path "$shm" && echo yes || echo no) || echo "(missing)")"
  echo ""
  echo "Fix guidance:"
  echo "  - Run the script as the same user that owns the DB/WAL files."
  echo "  - Ensure the DB directory and any existing -wal/-shm files are writable by that user."
  echo "  - Avoid /mnt/c (WSL) and network filesystems for SQLite DBs if possible."
  echo ""
}

precheck_writeability() {
  local dir wal shm
  dir="$(dirname "$db_path")"
  wal="${db_path}-wal"
  shm="${db_path}-shm"

  if [[ ! -w "$dir" ]]; then
    echo "DB directory is not writable: $dir" >&2
    diag_io
    exit 1
  fi

  if [[ -e "$wal" && ! -w "$wal" ]]; then
    echo "WAL file exists but is not writable: $wal" >&2
    diag_io
    exit 1
  fi

  if [[ -e "$shm" && ! -w "$shm" ]]; then
    echo "SHM file exists but is not writable: $shm" >&2
    diag_io
    exit 1
  fi
}

has_events_table="$(sqlite3 "$db_path" "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Events' LIMIT 1;")"
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

combined_raw="$title_raw"$'\n\n'"$content_raw"

if command -v uuidgen >/dev/null 2>&1; then
  id="$(uuidgen | tr -d '-' | tr '[:upper:]' '[:lower:]')"
else
  id="$(cat /proc/sys/kernel/random/uuid 2>/dev/null | tr -d '-' | tr '[:upper:]' '[:lower:]' || date +%s%N)"
fi

esc_sql() { printf "%s" "$1" | sed "s/'/''/g"; }

txt="$(esc_sql "$combined_raw")"

precheck_writeability

set +e
err=""
for attempt in 1 2 3; do
  out="$(sqlite3 -cmd ".timeout $sqlite_timeout_ms" "$db_path" "BEGIN IMMEDIATE; INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES ('$id', 6, '$txt', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 15); COMMIT;" 2>&1)"
  rc=$?
  if [[ $rc -eq 0 ]]; then
    echo "Inserted $id into $db_path"
    set -e
    exit 0
  fi
  err="$out"
  if echo "$out" | grep -qi "disk I/O error"; then
    sleep 0.2
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
