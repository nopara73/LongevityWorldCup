# Content freshness and AI summaries

## Dates and validators

Public freshness describes observed changes to public content. It does not use deployment times, HTML or athlete file modification times, or a five-minute cache regeneration timestamp.

`ContentRevisionStore` persists content fingerprints and observed change dates in `public-content-revisions.json`, beside the application database and outside the publish directory. The first observation establishes a baseline with **unknown modification history**. Until a subsequent change is observed, omit HTTP `Last-Modified`, sitemap `lastmod`, and WebPage `dateModified`; machine facts say `facts_changed_at_utc: unknown`. Never invent a historical publication date from the first request or startup time. Keep the ledger across releases and restarts.

Machine document ETags hash their complete response content. Identical facts and definitions retain the same representation and validator across cache intervals and restarts. The names feed changes only when its names or order change. `Last-Modified` and `facts_changed_at_utc` use the document's persisted revision. HTTP `Date` describes the response, and current-age evaluation dates identify the UTC day used in the age calculation.

`PublicContentFreshness` checks page dependencies in a background service, at most once every ten seconds while public pages are requested. Page rendering reads the last verified revision and never waits for a site-wide scan. Persisted dates are available immediately after restart; a failed scan retains them and is logged for retry. It shares the public content projection with IndexNow: templates and relevant rendering/calculator code, published athlete data and proof bytes, ranking fields, visible Events, and public challenge state. Its precise mode includes every public crowd count change; notification bucketing remains independent. Each HTML page's structured data and sitemap entry use the same persisted page revision. Removed pages leave a tombstone so republishing identical content receives a new observed date. This records when the application observes the change, not a reconstructed edit time between observations.

Private contact fields, asset cache-version parameters, file mtimes, response time, and unrelated pages do not establish a new public revision. A content change does not imply search-engine indexing.

## Shared discovery catalog

`AiDiscoveryCatalog` supplies `/llms.txt`, `/llms-full.txt`, `/ai/index.md`, and `/.well-known/agent-card.json`. They list the same resources and ranking views, link to canonical HTML sources, and describe `/events` as published highlights. These routes are controller endpoints; old static copies left by an incremental deployment cannot override them.

`LeaderboardViewCatalog` defines the 17 public ranking views. Its selectors use `CompetitionRanking`, preserve the existing field and tie breakers, and are shared with content tracking. Parity tests compare every view with the existing athlete service. The public Pheno field includes the existing zero score fallback for a missing pheno result, while the missing clock value and test date remain explicitly unavailable.

- `/ai/leaderboard.md`: full Ultimate League standings, top ten for every other view, definitions, eligibility, field sizes, and links to full views and athlete summaries.
- `/ai/league/{view}.md`: a complete field for each of the 16 other views; these URLs are also in the sitemap.
- `/ai/athlete/{canonical-athlete-slug}.md`: canonical HTML source, current ranks and qualification, crowd statistics for the current image, and public test history. The main facts feed links every athlete summary.
- `/ai/athlete-names.md`: the existing numbered-name format in Ultimate League order.

All documents support GET, HEAD, ETag conditional requests, and the existing five-minute public cache policy. Unknown athlete or view URLs return 404. Known aliases normalize to one canonical URL, including the public host. The legacy `/ai/athletes.md` alias continues to redirect to the main facts document.

## Result provenance

Test dates are laboratory measurement dates. A first public announcement date appears only when a persisted accepted-result Event records it. Historical baseline results keep that publication date unavailable. Each clock result is calculated independently for that test; partial or unavailable panels do not become zero-valued clock ages. Current ranks are not historical placements or completed-season winners. Improvement views compare the latest eligible age with the worst eligible age, while the distinct improvement badges use the first eligible result.

## Verification

`ContentRevisionTests`, `AiSummaryTests`, `AiFreshnessHttpTests`, and the IndexNow content tests cover stable reads/restarts, actual runtime changes, the 100-guess boundary, ranking parity, independent dates, missing values, privacy, canonical URLs, conditional GET/HEAD, retained static copies, and matching sitemap/structured metadata. Existing crawler, calculator, legacy URL, and browser tests protect the surrounding contracts.

After deployment, verify all discovery routes and ranking views, representative athlete histories, one missing summary, GET/HEAD/304 behavior, and the persisted ledger. Repeat an unchanged feed request after its cache interval and compare its ETag. Public activity during the interval may legitimately change the representation.
