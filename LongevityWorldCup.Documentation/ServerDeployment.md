# Deployment

## SSH

### In a Hurry
Use the **Auto Deploy on Master** GitHub Actions workflow. It builds the frontend on the runner and transfers that exact artifact to the Node-free production host. For an emergency manual deploy, prepare and stage the artifact first, then use the server flow below instead of a hand-rolled one-liner.

### Prepare a Manual Frontend Artifact

Run this from a trusted workstation with the repository's pinned Node and npm versions. Build the exact commit that will be deployed:

```sh
git fetch origin master
git switch --detach origin/master
verified_sha="$(git rev-parse HEAD)"

cd LongevityWorldCup.Website
npm ci --ignore-scripts --no-audit --no-fund
npm run build
cd ..

artifact_dir=".artifacts/frontend-${verified_sha}"
mkdir -p "$artifact_dir"
tar -czf "$artifact_dir/frontend-assets.tar.gz" -C LongevityWorldCup.Website/wwwroot js
(
  cd "$artifact_dir"
  sha256sum frontend-assets.tar.gz > frontend-assets.tar.gz.sha256
)

ssh lwc-server "rm -rf /tmp/longevityworldcup-frontend-${verified_sha} && mkdir -p /tmp/longevityworldcup-frontend-${verified_sha}"
scp "$artifact_dir/frontend-assets.tar.gz" "$artifact_dir/frontend-assets.tar.gz.sha256" \
  "lwc-server:/tmp/longevityworldcup-frontend-${verified_sha}/"
printf 'Deploy commit: %s\n' "$verified_sha"
```

Use the printed commit for `verified_sha` in the server commands. Do not combine an artifact from one commit with another checkout.

### Step By Step
```sh
sudo apt update && sudo apt upgrade -y && sudo apt autoremove -y
set -eu

verified_sha="<exact commit printed while preparing the artifact>"
frontend_stage="/tmp/longevityworldcup-frontend-${verified_sha}"
deploy_source="$(mktemp -d)"
publish_output="$(mktemp -d)"
publish_root="/var/www/LongevityWorldCup/publish"
rollback_output="${publish_root}.rollback-${verified_sha}"
failed_output="${publish_root}.failed-${verified_sha}"
source_manifest="$frontend_stage/source-js.sha256"
published_manifest="$frontend_stage/published-js.sha256"
live_manifest="$frontend_stage/live-js.sha256"
service_stopped=0
deploy_started=0
deploy_succeeded=0
frontend_manifest() {
  root="$1"
  (
    cd "$root"
    find . -type f -name '*.js' -print0 \
      | LC_ALL=C sort -z \
      | xargs -0 -r sha256sum
  )
}
sudo_frontend_manifest() {
  root="$1"
  sudo sh -c '
    set -eu
    cd "$1"
    find . -type f -name "*.js" -print0 \
      | LC_ALL=C sort -z \
      | xargs -0 -r sha256sum
  ' sh "$root"
}
cleanup() {
  status=$?
  trap - EXIT
  if [ "$deploy_started" -eq 1 ] && [ "$deploy_succeeded" -ne 1 ] && [ -d "$rollback_output" ]; then
    echo "Deployment failed; restoring the previous published release."
    if [ "$service_stopped" -eq 0 ]; then
      sudo systemctl stop longevityworldcup.service || true
      service_stopped=1
    fi
    sudo rm -rf "$failed_output" || true
    if sudo mv "$publish_root" "$failed_output" && sudo mv "$rollback_output" "$publish_root"; then
      sudo rm -rf "$failed_output" || true
    else
      echo "Automatic release rollback failed; attempting to restore the interrupted release path." >&2
      if [ ! -d "$publish_root" ] && [ -d "$failed_output" ]; then
        sudo mv "$failed_output" "$publish_root" || true
      fi
    fi
  fi
  if [ "$service_stopped" -eq 1 ]; then
    sudo systemctl start longevityworldcup.service || true
  fi
  if [ "$deploy_started" -eq 0 ] || [ "$deploy_succeeded" -eq 1 ]; then
    sudo rm -rf "$rollback_output" "$failed_output" || true
  fi
  rm -rf "$frontend_stage" "$deploy_source" "$publish_output"
  exit "$status"
}
trap cleanup EXIT

test -f "$frontend_stage/frontend-assets.tar.gz"
test -f "$frontend_stage/frontend-assets.tar.gz.sha256"
(
  cd "$frontend_stage"
  sha256sum --check frontend-assets.tar.gz.sha256
)

cd ~/LongevityWorldCup
git fetch origin master
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
dotnet publish "$deploy_source/LongevityWorldCup.Website/LongevityWorldCup.Website.csproj" --configuration Release --output "$publish_output" -p:BuildFrontend=false
frontend_manifest "$publish_output/wwwroot/js" > "$published_manifest"
if ! cmp -s "$source_manifest" "$published_manifest"; then
  echo "Published frontend assets differ from the verified artifact." >&2
  diff -u "$source_manifest" "$published_manifest" || true
  exit 1
fi

sudo systemctl stop longevityworldcup.service
service_stopped=1
sudo rm -rf "$rollback_output" "$failed_output"
sudo cp -al "$publish_root" "$rollback_output"
deploy_started=1
sudo rsync -a --checksum --no-owner --no-group \
  --exclude='/config.json' \
  --exclude='/config.json.bak*' \
  --exclude='/AppData/***' \
  --exclude='/wwwroot/athletes/***' \
  --exclude='/wwwroot/generated/***' \
  --exclude='/wwwroot/js/***' \
  "$publish_output"/ /var/www/LongevityWorldCup/publish/
sudo rsync -a --checksum --delete --no-owner --no-group \
  "$publish_output/wwwroot/athletes"/ /var/www/LongevityWorldCup/publish/wwwroot/athletes/
sudo rsync -a --checksum --delete --no-owner --no-group \
  "$publish_output/wwwroot/js"/ /var/www/LongevityWorldCup/publish/wwwroot/js/
sudo_frontend_manifest "$publish_root/wwwroot/js" > "$live_manifest"
if ! cmp -s "$published_manifest" "$live_manifest"; then
  echo "Live frontend assets differ from the verified published assets." >&2
  diff -u "$published_manifest" "$live_manifest" || true
  exit 1
fi
sudo mkdir -p /var/www/LongevityWorldCup/publish/wwwroot/generated
sudo chown -R www-data:www-data /var/www/LongevityWorldCup/publish/wwwroot/generated
sudo find /var/www/LongevityWorldCup/publish/wwwroot/generated -type d -exec chmod 755 {} \;
sudo find /var/www/LongevityWorldCup/publish/wwwroot/generated -type f -exec chmod 644 {} \;
sudo systemctl start longevityworldcup.service
service_stopped=0

health_url="https://www.longevityworldcup.com/health"
health_body="/tmp/longevityworldcup-health.json"
for attempt in $(seq 1 24); do
  if curl -fsS --max-time 10 "$health_url" -o "$health_body"; then
    break
  fi

  if [ "$attempt" -eq 24 ]; then
    echo "Production health check failed: $health_url"
    sudo systemctl status longevityworldcup.service --no-pager -l || true
    exit 1
  fi

  sleep 5
done

grep -q '"status":"Healthy"' "$health_body"
rm -f "$health_body"

downloaded_script="$frontend_stage/deployed-script.js"
for script_path in "$publish_output"/wwwroot/js/*.js; do
  curl -fsS --max-time 10 \
    "https://www.longevityworldcup.com/js/$(basename "$script_path")?v=$verified_sha" \
    -o "$downloaded_script"
  expected_hash="$(sha256sum "$script_path")"
  expected_hash="${expected_hash%% *}"
  actual_hash="$(sha256sum "$downloaded_script")"
  actual_hash="${actual_hash%% *}"
  if [ "$expected_hash" != "$actual_hash" ]; then
    echo "Deployed script content differs from the verified artifact: $(basename "$script_path")" >&2
    exit 1
  fi
done
rm -f "$downloaded_script"

git status --short

sudo systemctl status longevityworldcup.service
deploy_succeeded=1
```

Publish from the temporary source, not from `~/LongevityWorldCup`. The website build regenerates documentation HTML during publish, and publishing from the checkout can dirty tracked files and break the next pull or deploy.

The production host intentionally does not need Node.js. Generated `wwwroot/js` files are ignored rather than committed. The automatic deploy runner builds and verifies them, packages an exact-commit artifact, checks its checksum after transfer, and injects it into the temporary source before publishing with `BuildFrontend=false`.

The production host must have an SDK compatible with the repository's `global.json`. When that pin advances, install the matching SDK before deployment. The deploy preflight resolves the SDK from the isolated source tree and prints both the resolution error and installed SDK list when the host is behind.

Before changing the live publish tree, deployment stops the service and creates a same-filesystem hard-link snapshot. A failed sync, health check, or byte-for-byte script probe restores that prior release before restarting the service. Every master push schedules the workflow; stale runs skip only when a newer run exists, so an otherwise ignored documentation or test commit cannot strand an earlier website change undeployed.

### Application submission proxy timeout

`POST /api/application/application` has a dedicated five-minute ASP.NET Core timeout because an accepted submission may contain up to 37 proof images that must be validated and packaged. The browser waits 310 seconds. Nginx must allow slightly more response-header time than both layers without extending every other public route.

In `/etc/nginx/sites-available/default`, add this exact-match location to the HTTPS `longevityworldcup.com` server. Sibling locations do not inherit proxy directives, so the block must remain complete:

```nginx
location = /api/application/application {
    proxy_pass http://127.0.0.1:5000;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection keep-alive;
    proxy_set_header Host $host;
    proxy_cache_bypass $http_upgrade;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 330s;

    add_header Onion-Location http://lwc7tszawiykmkjoq4u2yxramezkwbdys2wxr2fmf6sdr6ug5t36ckqd.onion$request_uri always;
}
```

Add the same exact-match location to both onion server blocks in `/etc/nginx/sites-available/tor`, but omit the `Onion-Location` response header. Do not raise `proxy_read_timeout` globally.

Before editing, confirm that the enabled symlinks resolve to those two files and create timestamped backups in `sites-available` (never `sites-enabled`, whose wildcard include would load them). After editing:

1. Run `sudo nginx -t`. If it fails, restore both backups and test again; do not reload invalid configuration.
2. Run `sudo systemctl reload nginx`, then `sudo systemctl is-active --quiet nginx`. If either fails, restore both backups, retest, and reload.
3. Inspect `sudo nginx -T`. There must be exactly three `location = /api/application/application` blocks and three `proxy_read_timeout 330s;` directives. The public exact block must include `Onion-Location`; the two onion exact blocks must not.
4. Verify `/health`, then send an empty JSON `POST /api/application/application` through the public host and both onion listeners (use `127.0.0.1` plus the onion `Host` header for local probes, because curl intentionally refuses `.onion` DNS resolution). Each submission probe must return `400`, proving that the exact location reaches ASP.NET Core validation rather than static handling.

Keep the backups until the application submission path has been verified after deployment.

### Reverse proxy CORS ownership

ASP.NET Core owns the route-specific CORS policies. The nginx reverse-proxy location must pass those response headers through unchanged: do not add `Access-Control-Allow-*` or `Access-Control-Expose-Headers` directives at the proxy layer, and do not intercept `OPTIONS` requests. Adding CORS headers in both layers produces duplicate values that browsers reject; applying wildcard headers in nginx also bypasses the application's restricted policy for non-public routes.

The automatic deployment probes production after each release. It requires exactly one wildcard `Access-Control-Allow-Origin` header on public API GET and preflight responses, validates the requested preflight method and header, and rejects an arbitrary-origin CORS header on `/health`.

Configure the repository's `SSH_FINGERPRINT` Actions secret with the production host-key fingerprint to enforce host verification for both artifact transfer and remote deployment. The workflow remains compatible with the existing secret set when it is absent, but then host identity is not pinned.

The temporary source is copied with `rsync -a` from tracked Git files instead of `git archive` so unchanged athlete media keeps its original modification time. Startup uses those timestamps to decide whether profile thumbnails are stale; resetting every athlete image timestamp can force hundreds of thumbnail regenerations before Kestrel starts listening.

The final sync preserves production-owned runtime paths:
- `config.json`
- `config.json.bak*`
- `AppData/`
- `wwwroot/generated/`

Deletion is scoped to `wwwroot/athletes/` and the generated-only `wwwroot/js/` directory. Removed athlete proofs and obsolete scripts disappear from production without turning the deploy into a broad cleanup of unrelated server files.

Social API token refreshes first try to persist updated token state in `config.json`. If the service account can read but not write that file, the app writes the runtime token fields to `/var/www/.longevityworldcup/runtime-config.json` instead. On startup, that sidecar is applied only when it is newer than `config.json`, so a fresh manual edit to `config.json` takes precedence. Delete or update the sidecar when intentionally resetting social tokens.

## Check Website
https://www.longevityworldcup.com/

1. Desktop, wide screen
2. Desktop, smallest width screen
3. Mobile, portrait
4. Mobile, landscape

## Athletes

### Get Into Position

```sh
sudo su
/var/www/.longevityworldcup
```

### List Athlete Keys
```sh
sqlite3 LongevityWorldCup.db "SELECT Key FROM Athletes;"
```

### Show Athlete Record

```sh
sqlite3 LongevityWorldCup.db "SELECT * FROM Athletes WHERE Key = 'athlete_key';"
```

### Delete Athlete Record
```sh
sqlite3 LongevityWorldCup.db "DELETE FROM Athletes WHERE Key = 'athlete_key';"
```

### Age Guesses

Crowd Age guesses are retained per profile-image content identity. `CrowdAgeProfileImageId` is the athlete's current image identity, and each item in `AgeGuesses` has its own `ProfileImageId`. A byte-identical re-upload reuses its history; a byte-different image starts with zero active guesses while older image histories remain stored.

Content-addressed public portrait and thumbnail files that are no longer active are retained locally for seven days to keep recently opened pages stable, then pruned on a subsequent athlete reload. The guess history retains only image hashes. A privacy removal may also require purging upstream caches that already hold an immutable URL.

On the first deployment of image-bound guessing, unversioned legacy guesses are assigned to the profile image that is live during migration. If no profile image exists then, they stay historical and are not attached to a later upload. Do not bulk-rewrite historical `ProfileImageId` values to the current ID, because that would mix guesses made against different pictures.

The new binary can safely recover unversioned guesses written by a rollback only while `CrowdAgeProfileImageId` still matches the live image. If a rollback is necessary after a profile picture changes, restore the database backup from the same release before bringing the image-aware binary forward again; otherwise those ambiguous rollback-era guesses intentionally remain outside the active aggregate.

#### Check Current Image and Total Historical Guesses

```sh
sqlite3 LongevityWorldCup.db "SELECT Key, CrowdAgeProfileImageId, json_array_length(AgeGuesses) AS HistoricalGuessCount FROM Athletes WHERE Key = 'athlete_key';"
```

#### Count Guesses by Profile Image

```sh
sqlite3 LongevityWorldCup.db "SELECT COALESCE(json_extract(guess.value, '$.ProfileImageId'), '<legacy>') AS ProfileImageId, COUNT(*) AS GuessCount FROM Athletes AS athlete, json_each(athlete.AgeGuesses) AS guess WHERE athlete.Key = 'athlete_key' GROUP BY ProfileImageId ORDER BY GuessCount DESC;"
```

#### Reset All Age Guess History

This is a destructive reset across every profile image, not only the current one. Stop the service before changing the database so its in-memory athlete snapshot cannot continue serving stale Crowd Age state, then restart it to recompute public data, placements, and badges.

```sh
sudo systemctl stop longevityworldcup.service
sqlite3 LongevityWorldCup.db "UPDATE Athletes SET AgeGuesses = '[]' WHERE Key = 'athlete_key';"
sudo systemctl start longevityworldcup.service
```

## Events

### All events
```sh
sqlite3 LongevityWorldCup.db "SELECT * FROM Events ORDER BY OccurredAt DESC;"
```

### All Joined events
```sh
sqlite3 LongevityWorldCup.db "SELECT * FROM Events WHERE Type=1 ORDER BY OccurredAt DESC;"
```

### All New Rank events
```sh
sqlite3 LongevityWorldCup.db "SELECT * FROM Events WHERE Type=2 ORDER BY OccurredAt DESC;"
```

### Delete all events related to a specific slug
```sh
printf "Enter slug: " && read -r SLUG && sqlite3 LongevityWorldCup.db "DELETE FROM Events WHERE instr(Text,'slug['||'$SLUG'||']')>0 OR instr(Text,'prev['||'$SLUG'||']')>0;"
```

## Delete Test Athlete
```sh
printf "Enter slug: " && read -r SLUG && sqlite3 LongevityWorldCup.db "BEGIN; DELETE FROM Events WHERE instr(Text,'slug['||'$SLUG'||']')>0 OR instr(Text,'prev['||'$SLUG'||']')>0; DELETE FROM Athletes WHERE Key='$SLUG'; COMMIT;"
```

## Merge DB files
```sh
sudo sqlite3 /var/www/.longevityworldcup/LongevityWorldCup.db ".backup '/var/www/.longevityworldcup/LongevityWorldCup_merged.db'"
```

## Subscriptions

### View
```sh
cat /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt && echo "Total Subscriptions: $(wc -l < /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt)"
```

### Delete/Unsubscribe

```sh
EMAIL2UNSUB="foo@bar.com"
sudo sed -i "/$EMAIL2UNSUB/d" /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt
```

### Backup
```sh
SUBSCRIPTIONS_FILE="/var/www/LongevityWorldCup/publish/AppData/subscriptions.txt"
BACKUP_DIR="/var/www/LongevityWorldCup/backups"
COUNT_LOG="/var/www/LongevityWorldCup/backups/subscription_counts.log"
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
BACKUP_FILE="$BACKUP_DIR/subscriptions_$(date +%Y%m%d%H%M%S).txt"

# Ensure the backup directory exists
sudo mkdir -p "$BACKUP_DIR"

# Check the size of the backup directory in bytes
BACKUP_SIZE=$(du -sb "$BACKUP_DIR" | awk '{print $1}')
MAX_SIZE=$((10 * 1024 * 1024))  # 10MB in bytes

if [ "$BACKUP_SIZE" -le "$MAX_SIZE" ]; then
    # Proceed to create backup
    sudo cp "$SUBSCRIPTIONS_FILE" "$BACKUP_FILE"
    echo "Backup created at $BACKUP_FILE"
else
    # Do not create backup, write out warning
    echo "Warning: Backup directory exceeds 10MB, backup not created. This might be an attack."
fi

# Get the current number of subscriptions
CURRENT_SUB_COUNT=$(sudo wc -l < "$SUBSCRIPTIONS_FILE")

# Read the previous subscription count from the log
if [ -f "$COUNT_LOG" ] && [ "$(wc -l < "$COUNT_LOG")" -gt 0 ]; then
    PREV_SUB_COUNT=$(tail -n 1 "$COUNT_LOG" | awk '{print $NF}')
else
    PREV_SUB_COUNT=0
fi

# Calculate the difference in subscriptions
DIFF=$((CURRENT_SUB_COUNT - PREV_SUB_COUNT))

# Log the current subscription count with a human-readable timestamp
echo "$TIMESTAMP $CURRENT_SUB_COUNT" | sudo tee -a "$COUNT_LOG"

# Display results
echo "Current number of subscriptions: $CURRENT_SUB_COUNT"
echo "Difference in subscriptions: $DIFF"
```

## Configure

Before first run on Linux ensure you give permission to the data folder.
```
sudo mkdir -p /var/www/.longevityworldcup
sudo chown -R www-data:www-data /var/www/.longevityworldcup
sudo chmod 700 /var/www/.longevityworldcup
```

After first run, config file is created: 
```sh
sudo nano /var/www/LongevityWorldCup/publish/config.json
```

The app may also create `/var/www/.longevityworldcup/runtime-config.json` for rotated X, Threads, and Facebook tokens when `publish/config.json` is read-only to `www-data`.

Make sure to publish the app at the unisable google website if it's a new setup. Otherwise refresh token expires in 7 days: https://console.cloud.google.com/auth/audience  
Publish before generating refresh token!

Copy google SMTP credentials into the config file:
- Find the client ID and secret at https://console.cloud.google.com/apis/credentials
- Generate refresh token at https://developers.google.com/oauthplayground/

### Integrations
#### Slack

Add webhook entry to config file:
```
SlackWebhookUrl": ""
```

Add separate error webhook entry to config file:
```
SlackErrorWebhookUrl": ""
```

#### BTCPay Server

Add BTCPay entries to `config.json`:
```
"BTCPayBaseUrl": "https://pay.longevityworldcup.com/",
"BTCPayStoreId": "HdMuY1SVeGgWomYAphnMQfnfhigQUcpSCmpbMegrVLNg",
"BTCPayGreenfieldApiKey": ""
```

Notes:
- Keep `BTCPayGreenfieldApiKey` secret, same handling as SMTP/Google secrets.
- Required API key permissions: `btcpay.store.cancreateinvoice`, `btcpay.store.canviewinvoices`.
- The server creates invoices and sets redirect to: `https://www.longevityworldcup.com/review`. Existing invoices using `/onboarding/application-review.html` remain supported by a permanent redirect.

