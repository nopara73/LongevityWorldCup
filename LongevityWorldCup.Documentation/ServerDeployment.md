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

dotnet_version="$(dotnet --version)"
dotnet_major="${dotnet_version%%.*}"
if [ "$dotnet_major" != "10" ]; then
  echo "Expected .NET SDK 10.x on production server, found $dotnet_version."
  exit 1
fi

git ls-files -z | rsync -a --from0 --files-from=- ./ "$deploy_source"/
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

Before changing the live publish tree, deployment stops the service and creates a same-filesystem hard-link snapshot. A failed sync, health check, or byte-for-byte script probe restores that prior release before restarting the service. Every master push schedules the workflow; stale runs skip only when a newer run exists, so an otherwise ignored documentation or test commit cannot strand an earlier website change undeployed.

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
#### Check Age Guesses
```sh
sqlite3 LongevityWorldCup.db "SELECT Key, AgeGuesses FROM Athletes WHERE Key = 'athlete_key';"
```
#### Reset Age Guesses
```sh
sqlite3 LongevityWorldCup.db "UPDATE Athletes SET AgeGuesses = '[]' WHERE Key = 'athlete_key';"
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
- The server creates invoices and sets redirect to: `https://www.longevityworldcup.com/onboarding/application-review.html`.

