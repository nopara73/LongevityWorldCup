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

After first run, config file is created: 
```sh
sudo nano /var/www/LongevityWorldCup/publish/config.json
```

Copy google SMTP credentials into the config file:
- Find the client ID and secret at https://console.cloud.google.com/apis/credentials
- Generate refresh token at https://developers.google.com/oauthplayground/
