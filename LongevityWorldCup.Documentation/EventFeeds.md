# Event feeds

Public Highlights are available through two free, read-only feeds:

- RSS 2.0: <https://longevityworldcup.com/feeds/events.rss>
- Atom 1.0: <https://longevityworldcup.com/feeds/events.atom>

Readers can subscribe using either URL or discover both formats from any page using the shared HTML head. The Highlights page includes a compact RSS link beside its title. No account, credentials, scheduled job, or third-party service is required.

Both formats contain the latest 100 readable website-visible competition Events, newest first. Hidden/social-only Events, athlete-profile-only accepted tests and season results, and malformed structured payloads are excluded. Public announcements, joining/Pro milestones, badge awards, age improvements, placement changes, donations, athlete-count milestones, and Challenge results use human-readable text and public display names. Custom announcement wording, Unicode, and safe HTTP/HTTPS links are preserved; no private delivery state, participant IDs, or raw structured tokens are serialized.

The original Event ID determines the permanent item identity in both formats: `https://longevityworldcup.com/events?event=<escaped-id>`. Ties in occurrence time use ordinal Event IDs for deterministic ordering. Changes to wording or athlete names do not create another identity. Feeds retain each Event's recorded UTC occurrence time rather than assigning a new date during a deployment, request, or first subscription. This historical date is not evidence of when the Event was first published. Atom's required `updated` field uses that same recorded date because Events do not have a separate edit timestamp; content changes are detected by the HTTP ETag. An empty Atom feed uses the fixed feed-introduction date, 7 October 2026 UTC.

GET and HEAD return the appropriate XML media type with UTF-8 encoding, a content-derived ETag, and a 60-second public revalidation cache. Matching `If-None-Match` returns 304. Corrections, visibility changes, removals, and new Events change the ETag; repeated reads of unchanged content do not. No misleading Last-Modified timestamp is inferred from Event occurrence times. The RSS `ttl` advises a five-minute polling interval.

This is syndication of existing public history. Fetching the feeds neither creates Events nor modifies social queues or their processed flags. Website-visible Custom Events enter the feed automatically, including ones that excluded Slack or other destinations. A previously hidden Custom Event enters only when it becomes website-visible.

Formats follow the [RSS 2.0 specification](https://www.rssboard.org/rss-specification) and [Atom RFC 4287](https://www.rfc-editor.org/rfc/rfc4287).
