# Calculator search discovery

- `/pheno-age` and `/bortz-age` are the public calculator destinations. They use the existing calculator screens, including "This is blood sport", and the existing onboarding and update flows.
- Discovery lives in response metadata, calculator-specific structured data, `robots.txt`, the sitemap, and the machine-readable discovery files. Do not rewrite visible product copy or add a separate screen for search engines.
- Only calculator URLs without query strings are indexable. Query strings may carry dates of birth, laboratory results, or flow instructions; all query variants retain `noindex, nofollow` in both HTML and `X-Robots-Tag`. This intentionally includes tracking-only query variants.
- Canonical URLs and structured data always use the clean calculator URL and never include supplied query values. The sitemap and discovery resources link only to those clean URLs.
- Leave query variants crawlable so crawlers can read their `noindex` directive. Existing `/onboarding/` aliases keep their redirects and crawl restrictions; other application, dashboard, and proof routes remain excluded.
- Keep calculator browser titles, body markup, styles, scripts, calculations, and navigation unchanged when changing discovery. Verify calculator and update flows with the existing browser tests and compare rendered production bodies before and after deployment.
