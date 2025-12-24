## 1. Run the script

```bash
sudo bash -lc 'cd /var/www/.longevityworldcup && bash /home/user/LongevityWorldCup/LongevityWorldCup.Website/Scripts/custom_event.sh "LongevityWorldCup.db"'
```

## 2. Enter the Title and Content

1. When prompted for `Title`, type a single line and press Enter.
2. When prompted for `Content`, type multiple lines.
   - Content is optional. 
   - End the content input by typing a single dot on its own line.

Example input:

Title
```text
New community update is live
```

Content
```text
We have added an expandable CustomEvent row in Highlights.
Check out the [Longevity World Cup](https://longevityworldcup.com/) site.
End of message.
.
```
_Note: For No Content events, just press `.` then `Enter` when the script asks for it._

## Supported formatting

The renderer supports three inline formats inside both Title and Content.

### Link

```text
Visit [Longevity World Cup](https://longevityworldcup.com/) for the latest leaderboard.
```
Renders as a clickable link in the UI.

### Bold

```text
This update highlights one [bold](important) change.
```
Renders with bold font weight.

### Strong

```text
This is a [strong](major announcement) for the community.
```
Renders with bold font weight and uses the accent color.

## Delete an event by ID

When the script inserts a new event, it prints the inserted `Id`. You can delete that row later using SQLite.

Run:

```bash
sudo sqlite3 /var/www/.longevityworldcup/LongevityWorldCup.db "DELETE FROM Events WHERE Id = 'ID_GOES_HERE';"
```
