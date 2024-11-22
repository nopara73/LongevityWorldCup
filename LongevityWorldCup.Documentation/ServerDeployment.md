# Deployment

## SSH

### In a Hurry
```
sudo apt update && sudo apt upgrade -y && cd LongevityWorldCup/ && git pull && sudo systemctl stop longevityworldcup.service && sudo dotnet publish LongevityWorldCup.Website/LongevityWorldCup.Website.csproj --configuration Release --output /var/www/LongevityWorldCup/publish && sudo systemctl start longevityworldcup.service
```

### Step By Step
```sh
#!/bin/bash

SUBSCRIPTIONS_FILE="/var/www/LongevityWorldCup/publish/AppData/subscriptions.txt"
BACKUP_DIR="/var/www/LongevityWorldCup/backups"
TIMESTAMP=$(date +%Y%m%d%H%M%S)
BACKUP_FILE="$BACKUP_DIR/subscriptions_$TIMESTAMP.txt"

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

# Get the number of subscriptions before deployment
PREV_SUB_COUNT=$(sudo wc -l < "$SUBSCRIPTIONS_FILE")
echo "Number of subscriptions before deployment: $PREV_SUB_COUNT"

# Save the previous subscription count to a temporary file
echo "$PREV_SUB_COUNT" | sudo tee /tmp/prev_sub_count.txt

# Assuming deployment steps occur here...

# Get the number of subscriptions after deployment
NEW_SUB_COUNT=$(sudo wc -l < "$SUBSCRIPTIONS_FILE")
echo "Number of subscriptions after deployment: $NEW_SUB_COUNT"

# Calculate the difference in subscription counts
DIFF=$((NEW_SUB_COUNT - PREV_SUB_COUNT))
echo "Difference in subscriptions: $DIFF"


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
```
cat /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt && echo "Total Subscriptions: $(wc -l < /var/www/LongevityWorldCup/publish/AppData/subscriptions.txt)"
```

### Delete/Unsubscribe

```
EMAIL="foo@bar.com"
FILE="/var/www/LongevityWorldCup/publish/AppData/subscriptions.txt"
if grep -Fxq "$EMAIL" "$FILE"; then
    grep -vFx "$EMAIL" "$FILE" > /tmp/subscriptions.txt && sudo mv /tmp/subscriptions.txt "$FILE"
    echo "Email '$EMAIL' has been removed."
else
    echo "Email '$EMAIL' not found in subscriptions."
fi
```
