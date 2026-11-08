# Reddit announcements

The `longevityworldcup` Devvit app publishes ordinary text posts to `r/LongevityWorldCup` as its app account. The LWC server selects Events and stores immutable prepared posts in `SocialDeliveries`; the app polls an authenticated bridge once per minute. It does not use the personal account's rejected external Data API application.

The server's `RedditDailyPostJob` selects up to one ordinary eligible Event at 15:08 UTC (23:08 Asia/Singapore). Reddit's ordinary selection is limited to a new overall Ultimate League leader (`NewRank` at rank 1), still leading at selection and dispatch. Posts include the athlete's current submitted result and the ranking context. Donations, athlete-count milestones, badges, other rank changes, crowd-age updates, and routine profile updates are skipped for Reddit only. New ordinary Event types are excluded by default. Seven-day freshness and the two-day athlete cooldown remain in force. There is no filler.

Custom Events explicitly targeting Reddit remain available at the next poll, independently of their website visibility or topic. The editorial filter adds no approval step and does not change destination selection or the existing blocked-link checks. All Events present at the first authenticated poll are baselined, including Custom Events selected before activation.

The eligibility checks also cover previously prepared but unstarted ordinary posts. A skipped unstarted post releases its unused daily reservation. Already-started attempts keep their immutable copy, duplicate protections, and automatic receipt path, even when their Event type would now be excluded.

The Custom Event API rejects a selected Reddit destination while the bridge is disabled or has not activated, preserving the user's draft rather than promising a delivery that would be baselined. The command-line Event queue also requires the Reddit activation marker.

## Configuration and activation

- `RedditEnabled` defaults to `false`.
- `RedditBridgeSecretHash` contains a dedicated `SecretHashVerifier` PBKDF2-SHA256 verification hash. Do not reuse the Custom Event Designer secret or store the plaintext bridge key in website configuration.
- Devvit's encrypted global setting `lwcBridgeSecret` holds the matching plaintext key. Keep it out of source, public app settings, URLs, and logs. The operator's approved private backup is `A:\Integrations\reddit.txt`.
- Devvit requires approval for the exact HTTP fetch hostname `longevityworldcup.com`, plus app Terms and Privacy Policy links. Review these prerequisites before activation.
- The app policies are published at `https://longevityworldcup.com/reddit-terms.html` and `https://longevityworldcup.com/reddit-privacy.html`; save both URLs in the app's developer settings.
- Build and test `LongevityWorldCup.Reddit` with `npm ci` and `npm test`, then upload the app. The app has no sample-post installation trigger, custom post UI, or public mutation route. Install it only in the intended subreddit and configure the encrypted key. Installation alone cannot post while the key is missing or the LWC bridge is disabled.
- Enable the bridge only after the app and domain permissions are ready. Its first authenticated `POST /api/reddit/next` activates the channel and establishes the historical baseline. Confirm the installed version, saved scheduler job, authentication, and persisted delivery state before reporting it active.

Use the separately installed official Devvit CLI for upload and installation; it is not a dependency of the app's build or runtime. The app pins the SDK to `0.14.7` and overrides its `protobufjs` dependency with the compatible security patch `7.6.5`. CI runs type checks, delivery tests, the build, and npm audit.

## Delivery protocol

All bridge endpoints require `Authorization: Bearer <dedicated bridge secret>` and return uncached responses. No endpoint accepts arbitrary post text or an alternative subreddit.

1. `POST /api/reddit/next` returns one prepared `{deliveryId,eventId,subreddit,title,text}` or HTTP 204. This request does not mark the Event delivered or begin a Reddit submission.
2. Devvit uses atomic Redis `hSetNX` to persist a start marker, then calls `POST /api/reddit/begin` with `{deliveryId}`. The server durably starts each prepared delivery once and enforces the ordinary daily quota. A missing response must not be treated as permission to submit again.
3. The app submits a normal Reddit text post with `runAs: APP`, persists its `t3_…` receipt in Redis without expiration, and calls `POST /api/reddit/receipts` with `{deliveryId,postId}`. Repeating the same receipt is harmless; a conflicting receipt cannot overwrite an existing one. The server derives the Reddit URL from the validated post ID.
4. A lost acknowledgment retries the receipt only. An uncertain Reddit submission uses `POST /api/reddit/uncertain` and is held in `review`; a crashed start marker is held for review after 45 minutes. Both Redis and server start markers prevent a second submission, including after loss of local Redis state.

## IFTTT transport

IFTTT uses the same Event queue and submits text posts as `u/nopara73`. Ordinary Event selection stays at 15:08 UTC. A minute interval job takes the next prepared or explicitly targeted Custom Event.

- Use the Webhooks event `lwc_reddit_announcement` with the Reddit **Submit a new text post** action. Set the subreddit to `LongevityWorldCup`, the title to `Value1`, and the body to `Value2`.
- The receipt applet watches **Any new post in subreddit** for `LongevityWorldCup`. Filter for `nopara73` and send a JSON POST to `https://www.longevityworldcup.com/api/reddit/ifttt/receipts` with a dedicated bearer credential. The JSON fields are `subreddit`, `author`, `postUrl`, `title`, `text` (Reddit's Content ingredient), and `postedAtUnixSeconds` (the Reddit PostedAt ingredient converted to Unix seconds).
- Set `RedditUseIfttt` and the receipt credential's PBKDF2 hash in `RedditIftttReceiptSecretHash` in protected production configuration. The authenticated receiver can be checked with dispatch disabled. Store the IFTTT Webhooks key in `RedditIftttWebhookKey` when enabling dispatch. Back up both credentials in the operator's approved private backup.
- Enable `RedditEnabled` after confirming the saved applets, credit allowance, and authenticated receipt route. The first dispatch run establishes the historical baseline. A successful webhook request leaves the delivery pending until the receipt applet reports its Reddit permalink. IFTTT polling and credit limits affect when receipts arrive.
- The server records each attempt before calling IFTTT. It accepts a receipt only when the author, subreddit, post time, title, and body identify one recorded attempt. An unresolved attempt moves to review after two hours and can still accept a matching late receipt. Keep that attempt record when investigating a missing post.

## Operator review

Inspect Reddit before resolving an uncertain delivery. If the post exists, submit its confirmed `t3_…` receipt using the authenticated bridge; a late receipt can resolve a review row. If no post can be confirmed, retain the row for review. Do not clear start markers or resend automatically: Reddit's submit operation has no idempotency key.

Use the existing Event administration tools to remove an inappropriate pending Event. Preserve unrelated platform deliveries and credentials. Disabling the bridge stops further queue access and submission starts; uninstalling the Devvit app removes its scheduled polling. Previously published Reddit content is not deleted by disabling the integration.
