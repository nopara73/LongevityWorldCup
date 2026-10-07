# Website announcement notifications

Visitors opt in with the bell beside RSS on `/events`. Permission is requested only after a click. The same bell turns notifications off. Browsers without the Push API hide the bell; on iOS/iPadOS, Web Push requires a supported Home Screen web app. No paid notification provider or visitor account is required.

The Custom Event designer has an independent **Website push** destination, initially unchecked, with a notification preview. The HTTP queue and `custom_event.sh` both persist `webpush` selection. Legacy callers, ordinary competition Events and historical Events do not select this destination. A notification displays up to 100 title and 200 body graphemes, with UTF-8 limits for the encrypted payload. Visible Events link to their existing `/events?event=...` detail; push-only Events open Highlights.

`WebPushPostJob` processes selected Custom Events once a minute. SQLite freezes the payload and eligible subscriptions on the first attempt. A visitor subscribing after queueing does not receive that Event. Successful provider acceptances are recorded per subscription; retries target only pending subscriptions. HTTP 404/410 removes the expired subscription. Other failures use bounded backoff, respect bounded `Retry-After` and stop after eight attempts. An Event with no subscribers is permanently skipped. Provider acceptance is not proof that a device displayed a notification. Notification tags and provider topics remain stable during retries; lost acknowledgements can still cause repeat delivery.

The worker handles notifications and clicks, without intercepting page requests or caching pages. Every valid message displays a visible notification. Clicks remain on the website's Highlights route. Worker assets use content versions; `Service-Worker-Allowed: /` grants its root scope, with `no-cache` and `updateViaCache: none` so updates are checked.

Three protected server settings enable the channel:

- `WebPushVapidSubject`: a `mailto:` or HTTPS contact URI.
- `WebPushVapidPublicKey`: the base64url uncompressed P-256 point (65 bytes).
- `WebPushVapidPrivateKey`: the base64url private P-256 scalar (32 bytes).

Keep this signing pair stable. Rotation requires visitors to resubscribe. Generate and install operational keys only with authorization for their protected storage; never place them in a checkout, temporary local artifact, command argument or log. Base-config updates must preserve any newer social-token sidecar values as described in [Deployment](ServerDeployment.md).

Subscription endpoints and encryption keys are private capabilities, stored in the protected production SQLite database. Unsubscribing removes the subscription and its device delivery rows. Inactive subscription records expire after 180 days; terminal device delivery rows expire after 30 days. A return visit with an active subscription refreshes its retention. The API validates same-origin intent, imposes a request size limit and rate limit, validates keys and permits HTTPS browser push-service endpoints only. Redirects and endpoint URL logging are disabled in the sender. Encryption/authentication follow [RFC 8291](https://www.rfc-editor.org/rfc/rfc8291) and [RFC 8292](https://www.rfc-editor.org/rfc/rfc8292), covered by the published encryption vector and signature verification tests.
