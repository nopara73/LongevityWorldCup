## Queue from the designer page

Open:

```text
/internal/custom-event-designer.html
```

Direct queueing is disabled until the server config contains a hash in `CustomEventDesignerSecretHash`.

To configure it on the Linux server:

1. Choose a temporary queueing secret.
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

Then click `Queue Event`.

Behavior:

- if `Webpage` is enabled, the post is stored in the DB and also appears on the public event feed
- if `Webpage` is disabled, the post is still stored in the DB, but it stays hidden from the public website
- hidden website posts can still be queued for Slack / X / Threads / Facebook
- when `Webpage` is disabled, social post generation does not include a public event URL
- selected social destinations are queued by setting their `Processed` column to `0`
- unselected social destinations are stored as already processed by setting their `Processed` column to `1`
- the designer/API response reports `queuedTargets`; it also returns `selectedTargets` for backwards compatibility with older callers
- a queued social target is not confirmation that the platform accepted the post; platform send success is only known after dispatch completes and the corresponding `Processed` column becomes `1` without a skip reason
- if dispatch cannot or should not send a queued target, the platform-specific skip reason column records why, for example `PlatformNotConfigured`, `EmptyMessage`, or a terminal platform policy reason

## Safe live-test checklist

Use the smallest possible blast radius for the first live test.

1. Save the current `CustomEventDesignerSecretHash` value.
2. Generate a hash from a temporary secret and put that hash in `/var/www/LongevityWorldCup/publish/config.json`.
3. Restart the website with `sudo systemctl restart longevityworldcup.service`.
4. Open `/internal/custom-event-designer.html`.
5. Use a harmless title such as `Test event - delete me`.
6. Select `Webpage` only and clear Slack, X, Threads, and Facebook.
7. Click `Queue Event`.
8. Confirm the response shows an event id.
9. Confirm the event appears on the public event feed.
10. Copy the cleanup SQL from the designer page and delete the test row if it should not remain public.
11. Roll back by restoring the previous `CustomEventDesignerSecretHash` value, or clearing it to disable direct queueing.
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

If the test accidentally used a social destination, inspect the corresponding processed and skip reason columns before deleting the row. A selected social target is still queued while its processed column is `0`; it is processed when the column is `1`. If the processed column is `1` and the skip reason column is non-empty, no platform post was sent for that destination.

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
- hidden website posts can still be queued for Slack / X / Threads / Facebook
- when `Webpage` is disabled, social post generation does not include a public event URL

The payload path writes the same event flags as the designer/API path. Selected social destinations are queued with their platform `Processed` column set to `0`; unselected destinations are marked processed with `1`.

## Social dispatch and skip reasons

Daily social jobs process pending rows where the platform `Processed` column is `0`.

- X and Threads can post supported fresh primary Events and Custom Events. Stale primary Events, unsupported event types, unsupported payloads, and non-postable badge variants are marked processed with a skip reason.
- Facebook daily posting currently supports Custom Events. Non-custom Facebook rows are terminal skips and are marked processed with `FacebookSupportsCustomEventsOnly`.
- Subject cooldown for X and Threads leaves the row unprocessed so it can be retried on a later run.
- Platform send failures leave the row unprocessed so the next job run can retry.
- Successful sends mark the row processed and clear any old skip reason for that platform.
- If a queued Custom Event targets an unconfigured platform during immediate dispatch, it is marked processed with `PlatformNotConfigured`.

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
