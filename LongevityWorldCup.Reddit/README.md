# Longevity World Cup announcements

The `longevityworldcup` app publishes selected Longevity World Cup Events as ordinary text posts in `r/LongevityWorldCup`, using its app account `u/longevityworldcup`. It is intended only for that community, which the app owner `u/nopara73` moderates. Installing it elsewhere does not enable queue access or posting.

The existing Longevity World Cup server owns the Event data, announcement selection, and delivery ledger. It selects up to one ordinary eligible Event per day at 15:08 UTC. Custom Events require the operator to select Reddit explicitly. Existing freshness rules and athlete cooldowns apply; there is no filler or replay of historical Events. The first authenticated queue poll establishes the historical baseline.

## Fetch Domains

This app requests exactly one hostname: **`longevityworldcup.com`**.

The app polls an authenticated announcement queue on the community's own website once per minute. The website's existing Event system is the authoritative source for athlete milestones, ranking changes, and operator-selected announcements. Those Events originate outside Reddit; Devvit's storage cannot observe changes to that existing server database by itself. Moving a copy of the queue into Devvit would still require a supported way to receive those external Events.

The requested domain is a first-party domain, so access requires Reddit's explicit approval under the [HTTP Fetch Policy](https://developers.reddit.com/docs/capabilities/server/http-fetch-policy). This justification describes the requested use; it does not imply approval. The bridge must remain disabled until the app and its domain permissions are approved and activation has been verified.

### Requests and data flow

All requests use HTTPS and `POST`, with JSON bodies and a dedicated bearer credential in the `Authorization` header. The credential is stored in an encrypted Devvit global secret setting; the website stores its verification hash. It is never included in URLs or logs.

| Endpoint | Request body | Purpose and response |
| --- | --- | --- |
| `https://longevityworldcup.com/api/reddit/next` | `{}` | Returns HTTP 204 when there is no prepared announcement, or an immutable `{deliveryId, eventId, subreddit, title, text}` payload. It does not begin a Reddit submission. |
| `https://longevityworldcup.com/api/reddit/begin` | `{deliveryId}` | Durably starts a delivery once and enforces the ordinary daily quota. Returns HTTP 200 when started, or HTTP 409 with `AlreadyStarted`, `DailyLimit`, or `NotPending`. |
| `https://longevityworldcup.com/api/reddit/receipts` | `{deliveryId, postId}` | Records the public Reddit post ID after a successful submission. Repeating the same receipt is safe; a conflicting receipt is rejected. |
| `https://longevityworldcup.com/api/reddit/uncertain` | `{deliveryId}` | Holds an ambiguous or interrupted submission for operator review instead of submitting it again. |

The website sends the app only the prepared announcement and its delivery identifiers. The app sends back those identifiers and, after publication, the resulting public Reddit post ID. It does not collect Reddit users' private messages or private profile data. Only `r/LongevityWorldCup` is an accepted publication target.

The scheduled handler uses a ten-second timeout for each bridge request. It makes no Reddit submission when authentication or queue access fails. An atomic Redis marker and the website's durable ledger prevent duplicate starts; saved receipts are retained and retried independently of posting. An uncertain submission requires review, including a start marker left unresolved for 45 minutes.

## Permissions

- **Reddit:** creates ordinary text posts as the app account in the intended subreddit.
- **Redis:** stores atomic delivery markers and public post receipts.
- **HTTP Fetch:** accesses only the four bridge endpoints on `longevityworldcup.com` described above.
- **Scheduler:** invokes the internal announcement handler once per minute. The website selects ordinary Events at 15:08 UTC; polling does not create additional ordinary announcements.

The app has no sample-post installation trigger, public mutation endpoint, or user interface that accepts arbitrary post text or an alternative subreddit. External Endpoints access is being requested separately; this version implements the polling bridge described here.

## Installation and operation

This is an integration for the existing Longevity World Cup server, not a general-purpose announcement bot. Only the app owner and the operators of that server can configure it.

1. Obtain Reddit's approval for the app and the exact fetch hostname. Install the reviewed app only in `r/LongevityWorldCup` through its app listing or the official Devvit CLI.
2. Configure the encrypted global setting `lwcBridgeSecret` with the dedicated bridge credential, and configure its matching verification hash as `RedditBridgeSecretHash` on the LWC server. Keep the website's `RedditEnabled` setting false until the permissions and matching credentials have been checked.
3. Enable the website bridge last. Verify the installed app version and its recurring scheduler, then verify a successful authenticated poll and the persisted historical baseline before expecting delivery. Events already present at activation are not replayed.
4. Ordinary eligible Events are selected automatically at the daily time above. For a Custom Event, select Reddit explicitly in the LWC Event administration tools after activation. Readers interact with the resulting ordinary Reddit posts normally; there is no separate app interface.
5. To pause posting, disable `RedditEnabled` on the website. An ambiguous submission remains held for operator review; inspect Reddit before resolving it and never resend automatically. To remove scheduled polling entirely, uninstall the app.

See the repository's [operator documentation](https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/RedditAutoposting.md) for the activation and delivery protocol.

## Policies and support

- [Terms and conditions](https://longevityworldcup.com/reddit-terms.html)
- [Privacy policy](https://longevityworldcup.com/reddit-privacy.html)
- [App listing](https://developers.reddit.com/apps/longevityworldcup)
- [Source and issue tracker](https://github.com/nopara73/LongevityWorldCup)

For an operational issue, contact the moderators of `r/LongevityWorldCup`. Disabling the website bridge stops further queue access and submission starts. Uninstalling the app removes its scheduled polling; previously published posts are retained.
