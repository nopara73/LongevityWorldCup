# Nostr announcements

Nostr uses a signing key and public relays; it requires no account registration, OAuth
application, subscription, or paid API. The initial relay choices are `wss://relay.damus.io`,
`wss://relay.primal.net`, and `wss://nostr.mom`. Inspect their public NIP-11 information and
verify anonymous publishing before activation. Relay policies can change; replace unavailable relays with verified free
ones rather than paying for posting access.

Generate the identity with a cryptographically secure random number generator. Keep the
private key in memory during setup and give the owner the complete account record to save:

- `nsec` private key and its equivalent lowercase hexadecimal value.
- `npub` public identity and its equivalent lowercase hexadecimal value.
- Public profile link, website identifier, relay URLs, and profile metadata.

Never put an account private key in the repository, local setup files, screenshots, tests,
command arguments, or logs. Published BIP-340 and NIP-19 example keys are test data only.
The website's existing protected production `config.json` needs these runtime fields:

```json
{
  "NostrPrivateKeyHex": "<32-byte private key in lowercase hex>",
  "NostrPublicKeyHex": "<verified 32-byte public key in lowercase hex>",
  "NostrRelayUrls": ["wss://relay.damus.io", "wss://relay.primal.net", "wss://nostr.mom"]
}
```

When the owner reserves secret storage to themselves, obtain their explicit server-only
exception before persisting the signing key. Preserve the current social token state and
config ownership/permissions when applying it; see [ServerDeployment.md](ServerDeployment.md).
Restart and verify the running service after activation. Missing keys leave selected Events
pending. The derived public key must match the configured identity before any publication.

Publish a signed kind-0 profile with LWC's name, biography, public logo, website, and
`nip05: "_@longevityworldcup.com"`. Publish a kind-10002 relay list containing the configured
relay URLs. `/.well-known/nostr.json?name=_` and `?name=longevityworldcup` expose only the
public identity and relay list, allow cross-origin clients, and return directly without a
redirect. Verify this endpoint before relying on the website identifier in a client.

The daily job selects at most one eligible Event at **15:06 UTC**. The minute queue handles
selected Custom Events and retries of prepared announcements. Athlete eligibility,
seven-day freshness, and two-day subject cooldown match the existing announcement rules;
donation receipts and Custom Events do not expire with athlete-highlight freshness. There
are no filler posts. Historical Events are baselined once at channel introduction.

The designer has an independent Nostr destination, preview, and `sendToNostr` flag in both
the HTTP payload and exported shell command. Notes preserve established wording and human
athlete names. An application budget of 16,000 UTF-16 characters keeps ordinary notes below
the relay frame budget. Existing public meme/image URLs are included with NIP-92 `imeta`
alt text. Generated Nostr images have independent content-based filenames so another
platform cannot overwrite media referenced by a signed note.

Each prepared kind-1 event is signed using BIP-340 and saved to `SocialDeliveries` with its
timestamp, tags, event ID, and signature. That record is public data and contains no private
key. Retries resend exactly this event, including after restart or a lost response. They
never regenerate its timestamp or create a second note. At least two distinct configured
relays must both acknowledge acceptance and return the matching event with a valid
signature before delivery becomes `sent`. A duplicate acceptance is a successful receipt.
One relay's success alone leaves the event pending, and completed receipts and cooldowns
remain independent of Mastodon and other destinations. Backoff reaches ten minutes.

The cryptographic dependency `NBitcoin.Secp256k1` is MIT-licensed and has no transitive
packages. Tests cover published signature/identifier vectors, canonical Unicode JSON,
account mismatch, fragmented messages, invalid acknowledgments, readback validation,
independent queues, concurrent jobs, and immutable retries.

Official references: [event protocol](https://github.com/nostr-protocol/nips/blob/master/01.md),
[domain identity](https://github.com/nostr-protocol/nips/blob/master/05.md),
[relay information](https://github.com/nostr-protocol/nips/blob/master/11.md),
[identifiers](https://github.com/nostr-protocol/nips/blob/master/19.md),
[relay lists](https://github.com/nostr-protocol/nips/blob/master/65.md),
[media metadata](https://github.com/nostr-protocol/nips/blob/master/92.md), and
[BIP-340](https://github.com/bitcoin/bips/blob/master/bip-0340.mediawiki).
