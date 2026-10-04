# Longevitymaxxing API

The weekly community calls retired after the final scheduled gathering on 4 October 2026, 14:30–15:30 Asia/Singapore. The ongoing Challenge remains available at `/longevitymaxxing`.

`GET /api/longevitymaxxing/state` returns public Challenge dates, days, standings, podium, discussion notes, welcome threads, Slack links, and the scoring window. It no longer returns `calls` or `callSelectionClosesAtUtc`.

`POST /api/longevitymaxxing/participant` accepts an access token and returns the public state, participant summary, eligible check-in days, participant discussion notes, and habit garden. It no longer returns `calls`. Signup, confirmation, access-link requests, participant edits, check-ins, and discussion endpoints retain their existing contracts. Check-in text and replies remain limited to 240 characters.

`POST /api/longevitymaxxing/stop-emails` continues to stop ordinary Challenge reminder emails. The retired `/stop-community-call-emails` endpoint is absent. A page link containing a nonempty `scope` displays a retired-link notice without sending an unsubscribe request; it cannot fall through to ordinary email opt-out.

The hourly Challenge job continues start emails, daily reminders, discussion digests, missed-day processing, and Challenge result highlights. It has no call selection, reminder, calendar attachment, or call announcement work. Start and daily emails use plain text without call schedules or calendar attachments.

The active schema contains no call availability, selection, reminder, or announcement tables, and participants have no call-specific email preference. Existing historical records were archived privately before removal from production. The retirement procedure and verified archive location belong in the private operational audit, not public API responses.
