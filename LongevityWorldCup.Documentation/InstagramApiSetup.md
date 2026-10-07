# Instagram announcements

The publisher uses the free **Instagram API with Instagram Login** for the existing
professional account `@longevityworldcup`. It needs only
`instagram_business_basic` and `instagram_business_content_publish`. Authorize the
account in the existing Meta application, accept its tester invitation when using
Standard Access, and verify the issued token's account, permissions, and expiry.
Dashboard-generated tester tokens can include extra permissions; inspect the actual
token rather than assuming it matches the app's selected features. Use the business
login flow with an explicit scope list when a narrower token is needed.
An existing broader grant must first be removed in Instagram's Apps and websites
settings; a narrower OAuth request alone can retain previously granted permissions.

The protected production `config.json` needs these fields:

```json
{
  "InstagramAccountId": "<verified Instagram user_id>",
  "InstagramAccessToken": "<long-lived account token>",
  "InstagramAccessTokenExpiresAtUtc": "<verified UTC expiry>",
  "InstagramAccessTokenLastRefreshAttemptAtUtc": "<UTC token creation or refresh time>"
}
```

Use `user_id` from the token's `/me` response, which can differ from its app-scoped
`id`. Verify the account before creating a container or publishing. The account
password and Instagram app secret are recovery/setup credentials and are not needed
by the running publisher. Keep the complete recovery record in the owner's designated
private store; never put real credentials in the repository, artifacts, or logs.
Preserve newer token fields in `runtime-config.json` during manual server edits as
described in [ServerDeployment.md](ServerDeployment.md).

Both posting jobs check token maintenance even with no eligible Events. A long-lived
token is refreshed within fourteen days of expiry, at most once per twenty-five
hours. Refresh requires an unexpired token at least twenty-four hours old. The new
token's identity is verified before its value, expiry, and attempt time are saved.
The normal runtime sidecar supports the same three rotating fields. An expired
token requires account authorization again. HTTP request logging and redirects are
disabled for this client because Instagram's refresh endpoint requires the token
in a query parameter.

The daily job selects at most one eligible Event at **15:12 UTC**. The minute job
handles selected Custom Events and retries of prepared announcements. Both share a
dispatch gate. Athlete eligibility, seven-day freshness, and two-day subject cooldown
follow the established posting rules. Custom Events and donation acknowledgments do
not expire under the athlete freshness rule. There are no filler posts.

Every post uses a JPEG announcement card or an existing approved milestone meme,
plus a caption and alt text. Images are public, immutable content-addressed files
in `wwwroot/generated/instagram/`, which deployment preserves. Unsupported image
dimensions are padded to a supported square. Captions have a conservative 2,200
UTF-16-unit budget and alt text a 1,000-unit budget; truncation preserves graphemes.
If a URL alone exceeds the caption budget, the caption uses readable title/body
copy instead of a partial URL.
The Custom Event Designer includes an independent image-first Instagram preview,
checkbox, direct queue target, and exported `sendToInstagram` payload flag.
Other-platform athlete contacts resolve to names.

`SocialDeliveryChannels` baselines old Events once, preventing a history backfill.
`SocialDeliveries` stores the immutable caption/image request and dispatch state.
`InstagramPublishing` separately persists the container ID before publishing and
the returned media ID before fetching its permalink. A processing container is
polled on subsequent minute runs for up to five minutes. A known media ID permits
read-only receipt retries across restart. Delivery becomes `sent` only after the
media ID, caption, image type, and Instagram permalink match the API readback.

Instagram does not provide an idempotency key for `media_publish`. The ledger records
the public-write attempt before making that call. A lost response, uncertain error,
or restart without a saved media ID sets `review` / `UnconfirmedPost`; automatic
resubmission stops. Explicit HTTP 401, 403, and 429 rejections are safely retryable.
Review uncertain deliveries against the account's posts and reconcile the remote
receipt before resetting anything. Do not clear a write marker blindly.

Official references: [content publishing](https://developers.facebook.com/documentation/instagram-platform/content-publishing),
[image and caption limits](https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-user/media),
[token refresh](https://developers.facebook.com/documentation/instagram-platform/reference/refresh_access_token),
[account identity](https://developers.facebook.com/documentation/instagram-platform/reference/me),
and [container status](https://developers.facebook.com/documentation/instagram-platform/instagram-graph-api/reference/ig-container).
