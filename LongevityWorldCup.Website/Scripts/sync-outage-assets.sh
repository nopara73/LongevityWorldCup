#!/bin/sh
set -eu

if [ "$#" -ne 3 ]; then
  echo "Usage: sync-outage-assets.sh <published-wwwroot> <error-root> <nginx-snippet>" >&2
  exit 1
fi
source_root="$(realpath -e "$1")"
error_root="$(realpath -m "$2")"
snippet="$(realpath -e "$3")"
script_root="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
if [ "$(basename "$source_root")" != wwwroot ] ||
   [ "$(basename "$error_root")" != longevityworldcup-errors ] ||
   [ "$(basename "$snippet")" != lwc-error-page.conf ]; then
  echo "Expected published wwwroot, longevityworldcup-errors and lwc-error-page.conf paths." >&2
  exit 1
fi

# Stage the complete independent response before touching nginx's current files.
stage="$(mktemp -d "${error_root}.stage.XXXXXX")"
previous="${stage}.previous"
previous_snippet="${stage}.conf"
changed=0
succeeded=0
cleanup() {
  status=$?
  trap - EXIT
  if [ "$changed" -eq 1 ] && [ "$succeeded" -ne 1 ]; then
    rm -rf "$error_root"
    if [ -d "$previous" ]; then mv "$previous" "$error_root"; fi
    cp -p "$previous_snippet" "$snippet"
    nginx -t && systemctl reload nginx || true
  fi
  rm -rf "$stage" "$previous"
  rm -f "$previous_snippet"
  exit "$status"
}
trap cleanup EXIT
for code in 502 503 504; do
  install -m 644 "$source_root/error/$code.html" "$stage/$code.html"
done
install -m 644 "$source_root/error/herold.png" "$stage/herold.png"
install -m 644 "$source_root/assets/favicon-128x128.png" "$stage/favicon-128x128.png"
install -m 644 "$source_root/css/error-system.css" "$stage/error-system.css"
for weight in Regular Bold; do
  install -m 644 "$source_root/assets/fonts/Roboto-$weight.woff2" "$stage/Roboto-$weight.woff2"
done
# mktemp's private directory must become readable by the nginx worker.
chmod 755 "$stage"
cp -p "$snippet" "$previous_snippet"
if [ -d "$error_root" ]; then mv "$error_root" "$previous"; fi
changed=1
mv "$stage" "$error_root"
install -m 644 "$script_root/lwc-error-page.conf" "$snippet"
nginx -t
systemctl reload nginx
systemctl is-active --quiet nginx
succeeded=1
