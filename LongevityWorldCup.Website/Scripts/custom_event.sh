#!/usr/bin/env bash
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

sqlite3 "$db_path" "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES ('$id', 6, '$txt', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 15);"
echo "Inserted $id into $db_path"
