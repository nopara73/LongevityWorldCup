## Run from the designer page payload

Open:

```text
/internal/custom-event-designer.html
```

Fill in:

- `Title`
- `Content`
- target platforms

Then copy the generated server command and run it on the server.

Example:

```bash
sudo bash -lc 'cd /var/www/.longevityworldcup && bash /home/user/LongevityWorldCup/LongevityWorldCup.Website/Scripts/custom_event.sh "LongevityWorldCup.db" --payload "BASE64URL_JSON_PAYLOAD"'
```

## Supported formatting

The renderer supports four inline formats inside both Title and Content.

### Link

Renders as a clickable link in the UI.

```text
[Longevity World Cup](https://longevityworldcup.com/)
```

- `[Longevity World Cup]` -> the text that will be clickable
- `(https://longevityworldcup.com/)` -> the link

Example:

```text
Visit [Longevity World Cup](https://longevityworldcup.com/) for the latest leaderboard.
```

### Bold

Renders with bold font weight.

```text
[bold](important)
```

- `[bold]` -> this is the keyword, won't be visible.
- `(important)` -> the text that will be bold.

Example:
```text
This update highlights one [bold](important) change.
```

### Strong

Renders with bold font weight and uses the accent color.

```text
[strong](major announcement)
```

- `[strong]` -> this is the keyword, won't be visible.
- `(major announcement)` -> the text that will have the strong style applied to.

Example:
```text
This is a [strong](major announcement) for the community.
```

### Mention

Resolves an athlete slug to platform-specific mention text when posting to social media.

```text
[mention](athlete_slug)
```

- `[mention]` -> this is the keyword, won't be visible.
- `(athlete_slug)` -> the athlete slug to resolve.

Behavior:

- X: if the athlete `MediaContact` points to X/Twitter, the post uses the corresponding `@handle`
- Threads: if the athlete `MediaContact` points to Threads or Instagram, the post uses the corresponding `@handle`
- Facebook: falls back to the athlete name
- if no platform-specific social handle can be resolved, the fallback is always the athlete name

Example:

```text
Shoutout to [mention](nopara73) for shipping this update.
```

## Delete an event by ID

When the script inserts a new event, it prints the inserted `Id`. You can delete that row later using SQLite.

Run:

```bash
sudo sqlite3 /var/www/.longevityworldcup/LongevityWorldCup.db "DELETE FROM Events WHERE Id = 'ID_GOES_HERE';"
```
