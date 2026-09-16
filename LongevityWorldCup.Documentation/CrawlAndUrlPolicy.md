# Crawl and URL policy

## Public documents

`RouteCanonicalization` owns static page aliases and the common redirect rules. `CleanPathMiddleware` redirects aliases before rewriting canonical pages to their HTML templates. `PublicRouteMiddleware` validates athlete, league, and flag routes against application data and resolves their canonical paths before HTML rendering. Entity existence must not depend on social-preview image fonts or generated images.

Public documents on `www.longevityworldcup.com` redirect to `https://longevityworldcup.com`. Host and path normalization share a redirect, preserving the path base, query values, and duplicate query keys. GET and HEAD use 301; other methods use 308 to retain their bodies. API, health, asset, and explicitly embedded URLs retain their existing origins. Local development and the onion service do not redirect to the clearnet host.

Legacy league routes retain their client-side meanings: `ultimate` becomes `/leaderboard`, `pheno-improvement` becomes `/league/improvement`, and `professional` becomes `/leaderboard?filters=professional` unless a filters query already exists. The frontend's legacy normalization remains compatible with browser history and old in-page links.

Unknown documents and unknown athletes, leagues, or flags return HTTP 404. Status-code re-execution renders the existing `/error/404.html` template at the original requested URL; it must not redirect to a successful error page. The error template also returns 404 when requested directly, with `noindex, nofollow` and `Cache-Control: no-store`.

The reverse proxy must pass application 404 responses through unchanged. Its static gateway fallbacks apply only to 502, 503, and 504. [Server deployment guidance](ServerDeployment.md#reverse-proxy-error-responses) documents this boundary and the deployment probes that enforce it.

## Indexing and discovery

`HtmlInjectionMiddleware` owns each rendered page's indexing metadata. The privacy policy takes its robots and canonical metadata from this same source; it is indexable and listed in the sitemap. Private flows and embeds remain excluded.

Both robots.txt groups permit the exact public data feeds used by public pages, including query variants: athletes, flags, divisions, events, and public Bitcoin totals, exchange rate, and donation address. The broader `/api/` exclusion still applies to submissions, account flows, tracking, and other APIs. Robots rules are crawler guidance, not access control.

The sitemap and discovery documents use the final `/swagger/index.html` URL. Dynamic AI Markdown documents support GET and HEAD with matching content type, content length, cache validators, and indexing headers; HEAD has no body. Conditional requests retain the same ETag behavior.

Discovery documents are generated from a shared catalog, including all 17 ranking views and links to individual athlete summaries. Content revisions drive optional sitemap and structured-data modification dates; an unknown historical date remains omitted. See [Content freshness and AI summaries](ContentFreshnessAndAiSummaries.md) for the persistent revision ledger, date meanings, endpoints, and validation.

## Verification

`CrawlUrlConsistencyTests` checks missing pages, canonical redirects, legacy query behavior, unaffected non-document origins, crawler rules, privacy metadata, AI HEAD/conditional requests, and every sitemap URL's HEAD status. The existing legacy URL, CORS, preview metadata, embed, and browser route tests cover compatibility.

After deployment, check GET and HEAD without following redirects, follow representative aliases to their final pages, and scan every live sitemap URL for a 200 response without `noindex`. Verify `/health`, public API CORS, versioned assets, normal athlete/leaderboard navigation, the privacy policy, and a missing page in the browser.
