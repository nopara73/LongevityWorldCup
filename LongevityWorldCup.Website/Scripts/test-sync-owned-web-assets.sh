#!/bin/sh
set -eu
script_root="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
source_root="$fixture/published/wwwroot"
live_root="$fixture/live/wwwroot"
mkdir -p "$source_root/css" "$source_root/onboarding" \
  "$live_root/css" "$live_root/onboarding" "$live_root/research" "$live_root/helstab-kihivas" \
  "$live_root/generated" "$live_root/athletes" "$live_root/assets/content-images" \
  "$fixture/live/AppData"
printf current > "$source_root/css/main.css"
printf previous > "$live_root/css/main.css"
printf current > "$source_root/onboarding/pheno-age.html"
printf retired > "$live_root/css/mortality-age.css"
printf retired > "$live_root/css/helstab-kihivas.css"
printf retired > "$live_root/helstab-kihivas/helstab-kihivas.html"
printf retired > "$live_root/onboarding/mortality-age.html"
printf retired > "$live_root/research/mortality-age.pdf"
printf retired > "$live_root/research/mortality-age-model.json"
printf runtime > "$live_root/generated/proof.png"
printf runtime > "$live_root/athletes/upload.png"
printf runtime > "$live_root/assets/content-images/event.png"
printf runtime > "$fixture/live/AppData/runtime.json"
printf runtime > "$fixture/live/config.json"

sh "$script_root/sync-owned-web-assets.sh" "$source_root" "$live_root"
cmp "$source_root/css/main.css" "$live_root/css/main.css"
cmp "$source_root/onboarding/pheno-age.html" "$live_root/onboarding/pheno-age.html"
test ! -e "$live_root/css/mortality-age.css"
test ! -e "$live_root/css/helstab-kihivas.css"
test ! -e "$live_root/helstab-kihivas"
test ! -e "$live_root/onboarding/mortality-age.html"
test ! -e "$live_root/research"
for protected in "$live_root/generated/proof.png" "$live_root/athletes/upload.png" \
  "$live_root/assets/content-images/event.png" "$fixture/live/AppData/runtime.json" \
  "$fixture/live/config.json"; do
  test "$(cat "$protected")" = runtime
done
if sh "$script_root/sync-owned-web-assets.sh" "$source_root" "$source_root" 2>/dev/null; then
  echo "The sync must reject using the source as its destination." >&2
  exit 1
fi
printf 'Verified static updates, retired files, missing-source folders, and protected runtime data.\n'
