## Run from the designer page direct post

Open:

```text
/internal/custom-event-designer.html
```

Direct posting is disabled until the server config contains a hash in `CustomEventDesignerSecretHash`.

To configure it on the Linux server:

1. Choose a temporary posting secret.
2. Enter it in the designer page `Secret` field.
3. Click `Generate Config Hash`.
4. Copy the generated hash into `/var/www/LongevityWorldCup/publish/config.json` as `CustomEventDesignerSecretHash`.
5. Restart the website so the config is reloaded:

```sh
sudo systemctl restart longevityworldcup.service
sudo systemctl status longevityworldcup.service
```

The config stores only the hash. Do not store the plain secret in config, logs, shell history, or documentation.

Fill in:

- `Title`
- `Content`
- target platforms
- `Webpage` if the post should also appear on `/events`
- `Secret`

Then click `Post Event`.

Behavior:

- if `Webpage` is enabled, the post is stored in the DB and also appears on the public event feed
- if `Webpage` is disabled, the post is still stored in the DB, but it stays hidden from the public website
- hidden website posts can still be sent to Slack / X / Threads / Facebook
- when `Webpage` is disabled, social post generation does not include a public event URL
- selected social destinations are stored as pending by setting their `Processed` column to `0`
- unselected social destinations are stored as already processed by setting their `Processed` column to `1`

## Safe live-test checklist

Use the smallest possible blast radius for the first live test.

1. Save the current `CustomEventDesignerSecretHash` value.
2. Generate a hash from a temporary secret and put that hash in `/var/www/LongevityWorldCup/publish/config.json`.
3. Restart the website with `sudo systemctl restart longevityworldcup.service`.
4. Open `/internal/custom-event-designer.html`.
5. Use a harmless title such as `Test event - delete me`.
6. Select `Webpage` only and clear Slack, X, Threads, and Facebook.
7. Click `Post Event`.
8. Confirm the response shows an event id.
9. Confirm the event appears on the public event feed.
10. Copy the cleanup SQL from the designer page and delete the test row if it should not remain public.
11. Roll back by restoring the previous `CustomEventDesignerSecretHash` value, or clearing it to disable direct posting.
12. Restart the website again so rollback takes effect.

Linux production paths:

- Config: `/var/www/LongevityWorldCup/publish/config.json`
- Database: `/var/www/.longevityworldcup/LongevityWorldCup.db`
- Service: `longevityworldcup.service`
- App logs:

```sh
sudo journalctl -u longevityworldcup.service -n 100 --no-pager
```

Cleanup SQL format:

```bash
sudo sqlite3 /var/www/.longevityworldcup/LongevityWorldCup.db "DELETE FROM Events WHERE Id = 'ID_GOES_HERE';"
```

If the test accidentally used a social destination, inspect the corresponding processed column before deleting the row. A selected social target is pending while its processed column is `0`.

## Run from the designer page payload

Open:

```text
/internal/custom-event-designer.html
```

Fill in:

- `Title`
- `Content`
- target platforms
- `Webpage` if the post should also appear on `/events`

Then copy the generated server command and run it on the server.

Behavior:

- if `Webpage` is enabled, the post is stored in the DB and also appears on the public event feed
- if `Webpage` is disabled, the post is still stored in the DB, but it stays hidden from the public website
- hidden website posts can still be sent to Slack / X / Threads / Facebook
- when `Webpage` is disabled, social post generation does not include a public event URL

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
- Threads: if the athlete `MediaContact` points to Threads, the post uses the corresponding `@handle`
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
