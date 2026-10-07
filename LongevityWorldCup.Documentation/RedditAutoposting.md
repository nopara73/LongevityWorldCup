# Reddit announcements

The `longevityworldcup` Devvit app publishes ordinary text posts to `r/LongevityWorldCup` as its app account. The LWC server selects Events and stores immutable prepared posts in `SocialDeliveries`; the app polls an authenticated bridge once per minute. It does not use the personal account's rejected external Data API application.

The server's `RedditDailyPostJob` selects up to one ordinary eligible Event at 15:08 UTC (23:08 Asia/Singapore). Custom Events explicitly targeting Reddit are available at the next app poll, independently of their website visibility. Eligibility, seven-day freshness where applicable, and two-day athlete cooldowns match Mastodon/Nostr. There is no filler. All Events present at the first authenticated poll are baselined, including Custom Events selected before activation.

The Custom Event API rejects a selected Reddit destination while the bridge is disabled or has not activated, preserving the user's draft rather than promising a delivery that would be baselined. The command-line Event queue also requires the Reddit activation marker.

## Configuration and activation

- `RedditEnabled` defaults to `false`.
- `RedditBridgeSecretHash` contains a dedicated `SecretHashVerifier` PBKDF2-SHA256 verification hash. Do not reuse the Custom Event Designer secret or store the plaintext bridge key in website configuration.
- Devvit's encrypted global setting `lwcBridgeSecret` holds the matching plaintext key. Keep it out of source, public app settings, URLs, and logs. The operator's approved private backup is `A:\Integrations\reddit.txt`.
- Devvit requires approval for the exact HTTP fetch hostname `longevityworldcup.com`, plus app Terms and Privacy Policy links. Review these prerequisites before activation.
- Build and test `LongevityWorldCup.Reddit` with `npm ci` and `npm test`, then upload the app. The app has no sample-post installation trigger, custom post UI, or public mutation route. Install it only in the intended subreddit and configure the encrypted key. Installation alone cannot post while the key is missing or the LWC bridge is disabled.
- Enable the bridge only after the app and domain permissions are ready. Its first authenticated `POST /api/reddit/next` activates the channel and establishes the historical baseline. Confirm the installed version, saved scheduler job, authentication, and persisted delivery state before reporting it active.

Use the separately installed official Devvit CLI for upload and installation; it is not a dependency of the app's build or runtime. The app pins the SDK to `0.14.7` and overrides its `protobufjs` dependency with the compatible security patch `7.6.5`. CI runs type checks, delivery tests, the build, and npm audit.

## Delivery protocol

All bridge endpoints require `Authorization: Bearer <dedicated bridge secret>` and return uncached responses. No endpoint accepts arbitrary post text or an alternative subreddit.

1. `POST /api/reddit/next` returns one prepared `{deliveryId,eventId,subreddit,title,text}` or HTTP 204. This request does not mark the Event delivered or begin a Reddit submission.
2. Devvit uses atomic Redis `hSetNX` to persist a start marker, then calls `POST /api/reddit/begin` with `{deliveryId}`. The server durably starts each prepared delivery once and enforces the ordinary daily quota. A missing response must not be treated as permission to submit again.
3. The app submits a normal Reddit text post with `runAs: APP`, persists its `t3_…` receipt in Redis without expiration, and calls `POST /api/reddit/receipts` with `{deliveryId,postId}`. Repeating the same receipt is harmless; a conflicting receipt cannot overwrite an existing one. The server derives the Reddit URL from the validated post ID.
4. A lost acknowledgment retries the receipt only. An uncertain Reddit submission uses `POST /api/reddit/uncertain` and is held in `review`; a crashed start marker is held for review after 45 minutes. Both Redis and server start markers prevent a second submission, including after loss of local Redis state.

## Operator review

Inspect Reddit before resolving an uncertain delivery. If the post exists, submit its confirmed `t3_…` receipt using the authenticated bridge; a late receipt can resolve a review row. If no post can be confirmed, retain the row for review. Do not clear start markers or resend automatically: Reddit's submit operation has no idempotency key.

Use the existing Event administration tools to remove an inappropriate pending Event. Preserve unrelated platform deliveries and credentials. Disabling the bridge stops further queue access and submission starts; uninstalling the Devvit app removes its scheduled polling. Previously published Reddit content is not deleted by disabling the integration.
