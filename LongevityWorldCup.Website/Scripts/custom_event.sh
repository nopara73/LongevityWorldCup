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
  echo "Usage: $0 /path/to/LongevityWorldCup.db --payload BASE64URL_JSON" >&2
  exit 1
}

db_path="${1:-}"
if [[ -z "${db_path//[[:space:]]/}" ]]; then
  usage
fi
shift || true

payload_arg=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --payload)
      [[ $# -ge 2 ]] || usage
      payload_arg="$2"
      shift 2
      ;;
    *)
      usage
      ;;
  esac
done

if [[ -z "$payload_arg" ]]; then
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

decode_payload() {
  command -v python3 >/dev/null 2>&1 || { echo "python3 is required for --payload mode" >&2; exit 1; }

  local payload="$1"
  mapfile -d '' -t payload_fields < <(
    python3 - "$payload" <<'PY'
import base64
import json
import sys

payload = sys.argv[1]
payload += "=" * (-len(payload) % 4)

try:
    raw = base64.urlsafe_b64decode(payload.encode("ascii"))
    data = json.loads(raw.decode("utf-8"))
except Exception as ex:
    print(f"Invalid payload: {ex}", file=sys.stderr)
    sys.exit(1)

title = data.get("title")
content = data.get("content", "")

if not isinstance(title, str) or not title.strip():
    print("Payload title is required", file=sys.stderr)
    sys.exit(1)

if content is None:
    content = ""

if not isinstance(content, str):
    print("Payload content must be a string", file=sys.stderr)
    sys.exit(1)

flags = []
for key in ("sendToSlack", "sendToX", "sendToThreads", "sendToFacebook"):
    flags.append("1" if bool(data.get(key)) else "0")

if not any(flag == "1" for flag in flags):
    print("Payload must enable at least one target platform", file=sys.stderr)
    sys.exit(1)

for value in (title, content, *flags):
    sys.stdout.buffer.write(str(value).encode("utf-8"))
    sys.stdout.buffer.write(b"\0")
PY
  )

  if [[ "${#payload_fields[@]}" -lt 6 ]]; then
    echo "Invalid payload fields" >&2
    exit 1
  fi

  title_raw="${payload_fields[0]}"
  content_raw="${payload_fields[1]}"
  send_slack="${payload_fields[2]}"
  send_x="${payload_fields[3]}"
  send_threads="${payload_fields[4]}"
  send_facebook="${payload_fields[5]}"
}

render() {
  local s prev
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

selected_platforms() {
  local items=()
  [[ "$send_slack" == "1" ]] && items+=("Slack")
  [[ "$send_x" == "1" ]] && items+=("X")
  [[ "$send_threads" == "1" ]] && items+=("Threads")
  [[ "$send_facebook" == "1" ]] && items+=("Facebook")

  local joined=""
  for item in "${items[@]}"; do
    if [[ -n "$joined" ]]; then
      joined+=", "
    fi
    joined+="$item"
  done
  printf '%s' "$joined"
}

decode_payload "$payload_arg"

printf '\n'
printf 'Preview\n\n'
printf 'Title:\n%s\n\n' "$(render "$title_raw")"
if [[ -n "${content_raw//[[:space:]]/}" ]]; then
  printf 'Content:\n%s\n\n' "$(render "$content_raw")"
else
  printf 'Content:\n(empty)\n\n'
fi
printf 'Send to:\n%s\n\n' "$(selected_platforms)"

read -r -p "Execute? [y/N] " ok
case "${ok:-}" in y|Y) : ;; *) echo "Cancelled"; exit 0 ;; esac

if [[ -n "${content_raw//[[:space:]]/}" ]]; then
  combined_raw="$title_raw"$'\n\n'"$content_raw"
else
  combined_raw="$title_raw"
fi

if command -v uuidgen >/dev/null 2>&1; then
  id="$(uuidgen | tr -d '-' | tr '[:upper:]' '[:lower:]')"
else
  id="$(cat /proc/sys/kernel/random/uuid 2>/dev/null | tr -d '-' | tr '[:upper:]' '[:lower:]' || date +%s%N)"
fi

esc_sql() { printf "%s" "$1" | sed "s/'/''/g"; }
txt="$(esc_sql "$combined_raw")"
slack_processed="$([[ "$send_slack" == "1" ]] && echo 0 || echo 1)"
x_processed="$([[ "$send_x" == "1" ]] && echo 0 || echo 1)"
threads_processed="$([[ "$send_threads" == "1" ]] && echo 0 || echo 1)"
facebook_processed="$([[ "$send_facebook" == "1" ]] && echo 0 || echo 1)"

precheck_writeability

is_retryable_err() {
  LC_ALL=C echo "$1" | grep -qiE "database is locked|database is busy|SQLITE_BUSY|SQLITE_LOCKED|disk I/O error"
}

set +e
err=""
attempt=0
delay_ms="$sqlite_retry_initial_ms"
while :; do
  out="$(as_svc sqlite3 -cmd ".timeout $sqlite_timeout_ms" "$db_path" "BEGIN IMMEDIATE; INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed) VALUES ('$id', 6, '$txt', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 15, $slack_processed, $x_processed, $threads_processed, $facebook_processed); COMMIT;" 2>&1)"
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
