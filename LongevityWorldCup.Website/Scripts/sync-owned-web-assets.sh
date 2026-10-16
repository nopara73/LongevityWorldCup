#!/bin/sh
set -eu

if [ "$#" -ne 2 ]; then
  echo "Usage: sync-owned-web-assets.sh <published-wwwroot> <live-wwwroot>" >&2
  exit 1
fi

source_root="$(realpath -e "$1")"
live_root="$(realpath -e "$2")"
if [ ! -d "$source_root" ] || [ ! -d "$live_root" ] ||
   [ "$(basename "$source_root")" != "wwwroot" ] ||
   [ "$(basename "$live_root")" != "wwwroot" ] ||
   [ "$source_root" = "$live_root" ]; then
  echo "Expected distinct, existing published and live wwwroot directories." >&2
  exit 1
fi

# Only these checkout-owned directories are pruned. Excluded paths remain
# protected from deletion, including uploads and runtime-generated assets.
rsync -a --checksum --delete --no-owner --no-group \
  --include='/css/***' \
  --include='/helstab-kihivas/***' \
  --include='/onboarding/***' \
  --include='/research/***' \
  --exclude='*' \
  "$source_root"/ "$live_root"/
