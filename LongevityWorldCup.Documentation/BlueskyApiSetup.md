# Bluesky announcements

Bluesky uses the free AT Protocol API with an account app password. No developer application, billing account, OAuth callback, or platform review is needed. The implementation uses the existing server and .NET HTTP client.

## Account and credentials

The setup agent creates or connects the Longevity World Cup account, verifies its recovery email, and sets its display name, description, avatar, and `bot` self-label. Signup requires the account owner's birth date and agreement to Bluesky's terms; do not invent that information. An enforced browser approval or unavailable account verification must be reported explicitly.

Generate an app password named `Longevity World Cup announcements` without direct-message access. Keep the main account password for recovery, and use only the app password in the integration. Save issued credentials through the user's chosen secret store; never put them in Git, logs, screenshots, or chat output.

The agent configures these fields in the protected production `config.json`, preserving all other fields:

```json
{
  "BlueskyServiceUrl": "https://bsky.social",
  "BlueskyIdentifier": "<verified account handle or DID>",
  "BlueskyAppPassword": "<issued app password>"
}
```

The deployment workflow preserves production `config.json`. Bluesky access and refresh JWTs stay in memory. The client follows the account's PDS endpoint from the login response, refreshes an expired access session, and can authenticate again using the app password after a restart. The runtime sidecar does not override Bluesky credentials.

## Delivery

- The daily job selects one eligible announcement at **15:04 UTC** (23:04 Asia/Singapore). It shares the existing eligibility, seven-day freshness for athlete announcements, donation handling, and two-day athlete cooldown. It does not create filler posts.
- Selected Custom Events run through a separate one-minute queue. The two jobs share a process-wide dispatch gate and both disallow concurrent Quartz execution.
- The first application startup with this integration marks existing Events `ChannelIntroduced` in `SocialDeliveries`. It never automatically replays history. Events created afterwards can remain pending while account configuration is incomplete.
- `SocialDeliveries` stores each event and destination's status, attempts, retry time, SHA-256 content hash, complete prepared post, stable timestamp record key, remote AT URI, and CID. A failed send remains pending with exponential backoff capped at one day.
- Before a write, the client reads the saved record key. If a prior uncertain write succeeded, matching content recovers the receipt. Different content at that key is a conflict, never an overwrite. API success without the expected URI and a nonempty CID is insufficient.
- Text uses the 300-grapheme and 3000-byte limit. Link annotations use UTF-8 byte offsets. Text links get an external card; Custom Events and milestone memes can upload image blobs. Oversized PNGs are converted to JPEG before the 2 MB upload limit is enforced.

To check production without exposing secrets, inspect whether the identifier and app password are populated, read the integration's safe logs, and query delivery statuses. An absent credential means integration code is deployed but publishing is inactive. A registered Quartz trigger alone does not prove a configured account or a successful public post.

## References

- [Official bot tutorial](https://atproto.com/guides/bot-tutorial)
- [Post schema](https://github.com/bluesky-social/atproto/blob/main/lexicons/app/bsky/feed/post.json)
- [Record key specification](https://atproto.com/specs/record-key)
- [Create record API](https://github.com/bluesky-social/atproto/blob/main/lexicons/com/atproto/repo/createRecord.json)
