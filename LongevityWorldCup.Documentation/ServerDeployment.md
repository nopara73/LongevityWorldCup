# Deployment

## SSH

### In a Hurry
```
sudo apt update && sudo apt upgrade -y && cd LongevityWorldCup/ && git pull && sudo systemctl stop longevityworldcup.service && sudo dotnet publish LongevityWorldCup.Website/LongevityWorldCup.Website.csproj --configuration Release --output /var/www/LongevityWorldCup/publish && sudo systemctl start longevityworldcup.service
```

### Step By Step
```sh
sudo apt update && sudo apt upgrade -y

cd LongevityWorldCup/ && git pull && sudo systemctl stop longevityworldcup.service

sudo dotnet publish LongevityWorldCup.Website/LongevityWorldCup.Website.csproj --configuration Release --output /var/www/LongevityWorldCup/publish && sudo systemctl start longevityworldcup.service

sudo systemctl status longevityworldcup.service
```

## Browser

1. Check https://www.longevityworldcup.com/ on Desktop
2. Check the website on mobile

## Subscriptions

### View
```sh
cat /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt && echo "Total Subscriptions: $(wc -l < /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt)"
```

### Delete/Unsubscribe

```sh
EMAIL="foo@bar.com"
FILE="/var/www/LongevityWorldCup/publish/AppData/subscriptions.txt"
if grep -Fxq "$EMAIL" "$FILE"; then
    grep -vFx "$EMAIL" "$FILE" > /tmp/subscriptions.txt && sudo mv /tmp/subscriptions.txt "$FILE"
    echo "Email '$EMAIL' has been removed."
else
    echo "Email '$EMAIL' not found in subscriptions."
fi
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


