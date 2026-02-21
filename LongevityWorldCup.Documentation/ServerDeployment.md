# Deployment

## SSH

### In a Hurry
```sh
sudo apt update && sudo apt upgrade -y && sudo apt autoremove -y && cd LongevityWorldCup/ && git pull && sudo systemctl stop longevityworldcup.service && sudo rm -rf /var/www/LongevityWorldCup/publish/wwwroot/athletes/ && sudo dotnet publish LongevityWorldCup.Website/LongevityWorldCup.Website.csproj --configuration Release --output /var/www/LongevityWorldCup/publish && sudo systemctl start longevityworldcup.service && cd ..
```

### Step By Step
```sh
sudo apt update && sudo apt upgrade -y && sudo apt autoremove -y

cd LongevityWorldCup/ && git pull && sudo systemctl stop longevityworldcup.service

sudo rm -rf /var/www/LongevityWorldCup/publish/wwwroot/athletes/

sudo dotnet publish LongevityWorldCup.Website/LongevityWorldCup.Website.csproj --configuration Release --output /var/www/LongevityWorldCup/publish && sudo systemctl start longevityworldcup.service

sudo systemctl status longevityworldcup.service
```

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

