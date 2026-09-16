# IndexNow

The application notifies the free [IndexNow global endpoint](https://www.indexnow.org/documentation) about added, changed, and removed public canonical documents on `https://longevityworldcup.com`. Participating engines share submissions. HTTP 200 means received, not crawled or indexed. HTTP 202 means ownership validation is still pending.

## Operation and content boundaries

`IndexNowWorker` starts 30 seconds after application startup and compares public content once a minute. It submits at most one batch of 500 URLs per scan (configurable, capped at the protocol limit of 10,000), with at least five minutes between attempts for the same URL. Repeated changes coalesce to the latest content. The first enabled scan queues the existing eligible catalog once.

The catalog reuses the sitemap's canonical origin, static routes and league paths, the public leaderboard snapshot's athlete paths, and `FlagRouteCatalog`. Only these destinations can enter the queue. Athlete personal links, event links, proofs, generated images, private/admin routes, application/review flows, query strings, fragments, aliases and other hosts cannot become notification destinations. The public calculator landing pages are included; onboarding aliases are excluded.

Content dependencies include:

- Approved profile biography, result history/corrections, portrait identity, proof content, public badges, placements and profile highlights.
- Leaderboard rows and the athletes in each league/flag cohort. Old and new cohorts are compared when membership changes. Crowd count changes are grouped into progressively larger increments; median changes remain detectable.
- Website-visible published Events, including referenced athlete names and portraits. Accepted-test and season-result Events remain profile-only and do not create shared-announcement notifications. Social-only and future Events are excluded.
- Public Longevitymaxxing/Helstab challenge state; no participant access or submission URLs are queued.
- Published page templates, their nested partials, and relevant rendering/calculation/frontend source. `GenerateIndexNowContentManifest` writes content hashes into `indexnow-content.txt` for deployment without requiring source files or Node on the server. When adding a rendering dependency, add it to the appropriate manifest group and the page dependency mapping if needed.

Hashes exclude file/build times, generated Markdown timestamps, proof cache-version timestamps, and private contact email fields. Changing a file's timestamp alone or restarting unchanged code does not resubmit its pages. A missing build manifest aborts the scan and retains the ledger. Removed URLs retain a tombstone and are submitted even when their response is 404/410; an unchanged tombstone is not resubmitted after acceptance.

## Persistence and key verification

`indexnow-state.json` lives beside the application database, normally `/var/www/.longevityworldcup/indexnow-state.json`. It contains a randomly generated stable key, observed hashes, pending work, accepted hashes, attempt times, last response, and retry/pause state. It is outside the publish directory and survives deployment. Writes flush to disk and atomically replace the complete ledger. Back this file up alongside the SQLite database; the existing SQLite backup job does not copy it.

The app serves `/{key}.txt` as exact UTF-8 plain text, with no BOM or newline. GET and HEAD work without authentication or HTML injection. Every submission supplies this root `keyLocation`. Keep this endpoint publicly fetchable through the proxy/CDN. Do not commit the production key or ledger. Restore a corrupt/missing ledger from backup rather than discarding its key and accepted history.

There is a single worker in the existing single-instance hosting setup. An interrupted request remains pending across restart. If a process dies after the engine accepted a request but before the local acknowledgement was saved, a later duplicate is possible; the five-minute attempt interval survives that restart.

## Configuration

Real submission requires **both** `ASPNETCORE_ENVIRONMENT=Production` and `IndexNow__Enabled=true`. The default is disabled, including local runs whose environment was not explicitly set. Development, Test and Staging never submit even when `Enabled` is true. Tests use fake HTTP transport.

Enable on the existing systemd service with a dedicated drop-in, preserving the current service and configuration:

```ini
# /etc/systemd/system/longevityworldcup.service.d/indexnow.conf
[Service]
Environment=IndexNow__Enabled=true
```

Run `sudo systemctl daemon-reload` and restart `longevityworldcup.service` after verifying the deployed build. Optional environment settings: `IndexNow__BatchSize` (default 500) and `IndexNow__RetryVersion` (default empty). To disable submissions, set `IndexNow__Enabled=false` and restart. The key endpoint and saved ledger remain available.

## Responses and recovery

- **200:** save the submitted hashes and timestamps. Newer changes that appeared while sending stay pending.
- **202:** retain pending work and retry after at least 30 minutes; ownership validation is not reported as completed.
- **408, 429, 5xx, timeout or network error:** retain pending work; exponential backoff begins at five minutes and caps at 24 hours, with up to 10% jitter. Validation-pending retries use the same cap. Honor a longer `Retry-After` HTTP date or seconds value. Backoff applies to the entire host so newly queued URLs cannot bypass throttling.
- **400, 403, 422 or other unexpected response:** retain work and persist a paused state; log an error. Correct the format/key/host problem, verify the key endpoint, then set `IndexNow__RetryVersion` to a new value and restart. Unchanged restarts do not clear the pause. Do not continuously retry permanent validation failures.
- **Shutdown:** cancel in-flight HTTP calls, preserving pending hashes and attempt times. The worker never delays a public request for an engine response. A damaged ledger does not prevent ordinary pages from loading.

## Inspect and verify

Use read-only inspection on the server. Avoid printing the full ledger/key into shared logs:

```sh
sudo python3 - <<'PY'
import json
p = '/var/www/.longevityworldcup/indexnow-state.json'
s = json.load(open(p))
pending = [u for u, v in s['Urls'].items() if v['ContentHash'] != v.get('SubmittedHash')]
print({k: s.get(k) for k in ('LastScanUtc', 'LastSubmissionUtc', 'LastStatusCode', 'LastOutcome', 'LastBatchSize', 'NotBeforeUtc', 'PausedReason')})
print('known:', len(s['Urls']), 'pending:', len(pending))
PY
sudo journalctl -u longevityworldcup.service --since '1 hour ago' --no-pager | grep IndexNow
```

For a key probe, read `Key` locally from the ledger, GET `https://longevityworldcup.com/{key}.txt`, and compare the returned bytes to the UTF-8 key without adding a newline. The worker performs legitimate live submissions after enabling; verify its HTTP status and persisted submitted/pending state. Do not infer actual indexing from a successful submission.
