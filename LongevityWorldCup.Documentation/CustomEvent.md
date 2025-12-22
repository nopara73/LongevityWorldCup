## Run the script

```bash
./Scripts/custom_event.sh "/path/to/LongevityWorldCup.db"
```

## Enter the Title and Content

1. When prompted for `Title`, type a single line and press Enter.
2. When prompted for `Content`, type multiple lines.
3. End the content input by typing a single dot on its own line.

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

## Supported formatting

The renderer supports three inline formats inside both Title and Content.

### Link

Type
```text
Visit [Longevity World Cup](https://longevityworldcup.com/) for the latest leaderboard.
```

Renders as a clickable link in the UI.

### Bold

Type
```text
This update highlights one [bold](important) change.
```

Renders with bold font weight.

### Strong

Type
```text
This is a [strong](major announcement) for the community.
```

Renders with bold font weight and uses the accent color.

## Delete an event by ID

When the script inserts a new event, it prints the inserted `Id`. You can delete that row later using SQLite.

Run:

```bash
sqlite3 /path/to/LongevityWorldCup.db "DELETE FROM Events WHERE Id = 16e6c75b4e8646eab6ebb966503e6aa5;"
