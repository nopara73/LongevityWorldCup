# LongevityWorldCup Agent Notes

This file documents project-specific rules that agents commonly get wrong. It is intentionally focused on domain constraints, not generic development advice.

## Design Standards

Before making user-facing UI changes, read `DESIGN.md` and preserve its visual, layout, typography, and interaction rules.

## Browser Smoke Checks

This repository is a .NET solution, not a Node/npm-managed app. Browser smoke checks should use the `Microsoft.Playwright` package in `LongevityWorldCup.Tests`; do not add a `package.json` or npm install just to perform an agent-side smoke check unless the user explicitly asks for Node tooling.

For repeatable local UI verification, prefer the repo-owned .NET Playwright path or the Codex Browser plugin/in-app browser when available. After building `LongevityWorldCup.Tests`, install browser binaries with `pwsh LongevityWorldCup.Tests\bin\Debug\net8.0\playwright.ps1 install chromium` if needed. If using a separate Playwright runtime for ad hoc checks, first verify that Playwright and its browser binaries actually resolve in that runtime; do not assume the in-app Node REPL has Playwright on its module path.

## Domain Language

Before changing domain concepts, leaderboard/ranking logic, athlete onboarding, biological age calculators, badges, events, social posting, or user-facing competition copy, read `UBIQUITOUS_LANGUAGE.md`.

Use its canonical terms when naming UI text, code concepts, issues, and docs. If behavior or terminology changes, update `UBIQUITOUS_LANGUAGE.md` in the same change.

## Ranking Logic Must Stay Aligned

- Ranking is currently calculated in both the backend and the frontend.
- Do not assume one side is only presenting data from the other. Both sides contain logic that can affect ordering and placements.
- If you change ranking logic on one side, you must review and update the other side so the logic stays identical.
- The expected outcome is that the backend and frontend produce the same ordering and the same placements for the same data.
- Bioage calculator rank previews are clock-specific: pheno age shows the Pheno-only field rank, and bortz age shows the Bortz-only field rank.
- The crowd age leaderboard view is separate from Ultimate League ranking. Only athletes with at least 100 crowd guesses are eligible. It follows the same sign convention as other age-reduction columns: `CrowdAge - chronologicalAge`, with more negative values ranking higher, then higher `CrowdCount`, then older date of birth, then name.

## Ultimate League Ordering

- Inside the Ultimate League, Pro and Amateur are distinct categories.
- Pro always has priority over Amateur in lists and ordering.
- This is not an optional presentation detail. It is part of the expected sorting behavior.
- Current concrete example: Bortz is Pro, Pheno Age is Amateur.
- If you touch any sorting, ranking, or listing logic that affects Ultimate League entries, explicitly verify that Pro entries still appear before Amateur entries.

## Static Asset Loading Must Use Middleware Versioning

- This project injects HTML and partials through `HtmlInjectionMiddleware`, and static JS/CSS files are expected to use the shared asset versioning flow.
- Do not add raw script or stylesheet URLs such as `/js/foo.js` or `/css/foo.css` directly into injected HTML/partials unless there is a strong reason and you have verified cache behavior.
- When a new static asset must be loaded from injected HTML/partials, add a placeholder and replace it through `AssetVersionProvider.AppendVersion(...)` in the middleware so clients receive a cache-busted URL.
- Favicon and web manifest links in `wwwroot/partials/head.html` also use middleware placeholders; keep any new light or dark icon variants on that versioned path.
- Shared header logo images also use middleware asset placeholders. Keep header/partial static assets on the placeholder path instead of hard-coded `/assets/...` URLs.
- Bioage onboarding pages load `wwwroot/css/bioageform.css` through the `{{ASSET_BIOAGEFORM_CSS}}` middleware placeholder; do not hard-code `/css/bioageform.css` in those injected pages.
- Bioage onboarding rank previews load `wwwroot/js/bioage-rank-preview.js` through `HtmlInjectionMiddleware` module paths; keep it versioned with the rest of the injected page assets.
- Athlete profile and proof asset URLs should be versioned when emitted from the data service. Unversioned `/athletes/...` and `/generated/...` requests are intentionally cached briefly only as a fallback.
- When changing the API of an existing external JS file, review every injected HTML/partial caller in the repo and ensure the versioned URL path still goes through the middleware replacement flow.
- If a helper is moved from inline script to a separate JS file, verify that the file is loaded in every page, modal, iframe, or embedded context that uses that helper.

## Keep This File Updated

- If you modify behavior in any of the areas described above, update this file as part of the same change.
- If a rule here stops being accurate, do not leave the file stale. Adjust it together with the code change.
