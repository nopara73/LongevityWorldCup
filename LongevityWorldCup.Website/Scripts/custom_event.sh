#!/usr/bin/env bash
set -e
esc=$'\033'
bel=$'\a'

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
printf 'Type: %s\n' "[link text](https://example.com)"
printf 'Renders: '
render "[link text](https://example.com)"
printf '\n\n'

printf 'Type: %s\n' "[bold](Text that will be bold)"
printf 'Renders: '
render "[bold](Text that will be bold)"
printf '\n\n'

printf 'Type: %s\n' "[strong](Text that will be bold and pink colored)"
printf 'Renders: '
render "[strong](Text that will be bold and pink colored)"
printf '\n\n'

read -r -p "Title: " title_raw
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

if [[ -n "${LWC_DB:-}" ]]; then
  command -v sqlite3 >/dev/null 2>&1 || { echo "sqlite3 is not installed"; exit 1; }
  mkdir -p "$(dirname "$LWC_DB")"
  sqlite3 "$LWC_DB" "CREATE TABLE IF NOT EXISTS CustomEvents (Id TEXT PRIMARY KEY, CreatedUtc TEXT NOT NULL, Title TEXT NOT NULL, Content TEXT NOT NULL)"
  if command -v uuidgen >/dev/null 2>&1; then id="$(uuidgen)"; else id="$(cat /proc/sys/kernel/random/uuid 2>/dev/null || date +%s%N)"; fi
  esc_sql() { printf "%s" "$1" | sed "s/'/''/g"; }
  t="$(esc_sql "$title_raw")"
  c="$(esc_sql "$content_raw")"
  sqlite3 "$LWC_DB" "INSERT INTO CustomEvents (Id, CreatedUtc, Title, Content) VALUES ('$id', strftime('%Y-%m-%dT%H:%M:%SZ','now'), '$t', '$c')"
  echo "Inserted $id into $LWC_DB"
else
  echo "Confirmed"
  printf 'Title:\n%s\n\n' "$title_raw"
  printf 'Content:\n%s\n' "$content_raw"
fi