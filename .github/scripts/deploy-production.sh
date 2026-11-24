#!/bin/sh
set -eu

verified_sha="${VERIFIED_SHA:-${1:?Expected an exact release commit}}"
case "$verified_sha" in *[!0-9a-f]*) echo "Invalid release commit." >&2; exit 1 ;; esac
test "${#verified_sha}" -eq 40
frontend_stage="/tmp/longevityworldcup-frontend-$verified_sha"
if [ -n "${VERIFIED_SHA:-}" ]; then frontend_stage=/tmp/longevityworldcup-frontend; fi
publish_root=/var/www/LongevityWorldCup/publish
cd ~/LongevityWorldCup
# Manual and automatic releases share the checkout and the systemd service.
exec 9>.git/production-deploy.lock
flock -x 9
work="$(mktemp -d)"
trap 'rm -rf "$work" "$frontend_stage"' EXIT
deploy_source="$work/source"
publish_output="$work/published"
mkdir -p "$deploy_source" "$publish_output"
source_manifest="$work/source-js.sha256"
published_manifest="$work/published-js.sha256"
frontend_manifest() {
  root="$1"
  (
    cd "$root"
    find . -type f -name '*.js' -print0 \
      | LC_ALL=C sort -z \
      | xargs -0 -r sha256sum
  )
}

test -f "$frontend_stage/frontend-assets.tar.gz"
test -f "$frontend_stage/frontend-assets.tar.gz.sha256"
(
  cd "$frontend_stage"
  sha256sum --check frontend-assets.tar.gz.sha256
)

git fetch origin master
if [ -n "${VERIFIED_SHA:-}" ] && [ "$(git rev-parse origin/master)" != "$verified_sha" ]; then
  echo "Skipping superseded deployment $verified_sha."
  exit 0
fi
git reset --hard "$verified_sha"
git clean -fd
test "$(git rev-parse HEAD)" = "$verified_sha"

git ls-files -z | rsync -a --from0 --files-from=- ./ "$deploy_source"/
if ! dotnet_version="$(cd "$deploy_source" && dotnet --version 2>&1)"; then
  echo "The production host cannot resolve the SDK required by global.json:" >&2
  echo "$dotnet_version" >&2
  echo "Installed SDKs:" >&2
  (cd /tmp && dotnet --list-sdks) >&2
  exit 1
fi
dotnet_major="${dotnet_version%%.*}"
if [ "$dotnet_major" != "10" ]; then
  echo "Expected .NET SDK 10.x on production server, found $dotnet_version."
  exit 1
fi

tar -xzf "$frontend_stage/frontend-assets.tar.gz" \
  --no-same-owner \
  --no-same-permissions \
  -C "$deploy_source/LongevityWorldCup.Website/wwwroot"
frontend_manifest "$deploy_source/LongevityWorldCup.Website/wwwroot/js" > "$source_manifest"
test -s "$source_manifest"
# The isolated source tree has no .git metadata to stamp the assembly.
dotnet publish "$deploy_source/LongevityWorldCup.Website/LongevityWorldCup.Website.csproj" --configuration Release --output "$publish_output" -p:BuildFrontend=false -p:SourceRevisionId="$verified_sha"
if ! grep -a -Fq "$verified_sha" "$publish_output/LongevityWorldCup.Website.dll"; then
  echo "Published assembly is missing the verified revision." >&2
  exit 1
fi
frontend_manifest "$publish_output/wwwroot/js" > "$published_manifest"
if ! cmp -s "$source_manifest" "$published_manifest"; then
  echo "Published frontend assets differ from the verified artifact." >&2
  diff -u "$source_manifest" "$published_manifest" || true
  exit 1
fi

# The application owns the contents of this directory across releases.
sudo install -d -m 755 -o www-data -g www-data "$publish_root/wwwroot/generated"
sudo python3 "$deploy_source/.github/scripts/activate_release.py" \
  "$publish_root" "$publish_output" "$published_manifest" \
  sudo -u "$(id -un)" -- sh "$deploy_source/.github/scripts/verify-production.sh" \
  "$verified_sha" "$work" "$publish_output" "$deploy_source"
