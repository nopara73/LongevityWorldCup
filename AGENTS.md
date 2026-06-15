# LongevityWorldCup Agent Notes

This file documents project-specific rules that agents commonly get wrong. It is intentionally focused on domain constraints, not generic development advice.

## Design Standards

Before making user-facing UI changes, read `DESIGN.md` and preserve its visual, layout, typography, and interaction rules.

## Browser Smoke Checks

This repository is a .NET solution, not a Node/npm-managed app. Browser smoke checks should use the `Microsoft.Playwright` package in `LongevityWorldCup.Tests`; do not add a `package.json` or npm install just to perform an agent-side smoke check unless the user explicitly asks for Node tooling.

For repeatable local UI verification, prefer the repo-owned .NET Playwright path or the Codex Browser plugin/in-app browser when available. After building `LongevityWorldCup.Tests`, install browser binaries with `pwsh LongevityWorldCup.Tests\bin\Debug\net10.0\playwright.ps1 install chromium` if needed. If using a separate Playwright runtime for ad hoc checks, first verify that Playwright and its browser binaries actually resolve in that runtime; do not assume the in-app Node REPL has Playwright on its module path.

## Local Agent Artifacts

When producing temporary files that are only for agent-side inspection or delivery drafts, write them under the repo-local ignored `.artifacts/` directory. This includes screenshots, rendered HTML previews, generated images, ad hoc reports, logs, exports, or smoke-test captures that are not intended to become source-controlled project assets.

Do not place disposable outputs in tracked project folders such as `output/`, `wwwroot/`, `docs/`, or test fixture directories unless the user explicitly asks for a committed artifact there.

## Production Server Access

Agents working in this repository may have direct SSH access to the production server through the local SSH alias `lwc-server`. When a task needs production inspection, logs, database checks, service status, or deployment verification, check whether `ssh lwc-server` is available before inventing indirect workarounds or asking the user to run server commands.

Before making production changes over SSH, read `LongevityWorldCup.Documentation/ServerDeployment.md` and follow its documented paths, service names, runtime config locations, and preservation rules. Prefer read-only SSH checks unless the task explicitly requires a server-side change.

## Domain Language

Before changing domain concepts, leaderboard/ranking logic, athlete onboarding, biological age calculators, badges, events, social posting, or user-facing competition copy, read `UBIQUITOUS_LANGUAGE.md`.

Use its canonical terms when naming UI text, code concepts, issues, and docs. If behavior or terminology changes, update `UBIQUITOUS_LANGUAGE.md` in the same change.

- Keep the Longevitymaxxing Challenge separate from Ultimate League ranking, biological age placements, and athlete badges, even when challenge results appear as Events/highlights.
- Longevitymaxxing Challenge signup may remain open during active challenge days when configured; late participants join the current challenge and catch up through normal eligible check-ins.
- Longevitymaxxing Challenge call times may be selected before signup closes when needed for 24-hour call reminders.
- Longevitymaxxing Challenge built-in call defaults use Sunday call dates for future competitions; the June 2026 kickoff keeps a one-off Sunday 08:30 GMT+2 override on the selected kickoff slot.
- Longevitymaxxing Challenge daily reminder emails default to 07:00 in each participant's local timezone and may catch up later that same local day if the exact hour is missed.
- Longevitymaxxing Challenge habit points use a small day-weight ramp after Day 1: Day 2 starts at the raw 8-point maximum and the final day peaks at 11 points.
- Longevitymaxxing Challenge leaderboard ties after challenge performance metrics prefer participants linked to a currently placed Longevity athlete profile, then better current placement, then older linked athletes by date of birth.
- Challenge uploads and Gravatar fallbacks are challenge-only profile pictures; linked Longevity athlete profile pictures stay the display priority and must not be created or modified from challenge images.

## Ranking Logic Must Stay Aligned

- Ranking is currently calculated in both the backend and the frontend.
- Do not assume one side is only presenting data from the other. Both sides contain logic that can affect ordering and placements.
- If you change ranking logic on one side, you must review and update the other side so the logic stays identical.
- The expected outcome is that the backend and frontend produce the same ordering and the same placements for the same data.
- Bioage calculator rank previews are clock-specific: pheno age shows the Pheno-only field rank, and bortz age shows the Bortz-only field rank.
- Ultimate League Pro/Amateur ordering is domain behavior: Pro entries must appear before Amateur entries.
- The crowd age leaderboard view is separate from Ultimate League ranking. Only athletes with at least 100 crowd guesses are eligible. It follows the same sign convention as other age-reduction columns: `CrowdAge - chronologicalAge`, with more negative values ranking higher, then higher `CrowdCount`, then older date of birth, then name.
- Improvement leaderboard views are separate from Ultimate League ranking. Pheno Improvement is exposed through filters/routes as `Improvement` and ranks `latest eligible pheno age - worst eligible pheno age`; Bortz Improvement is also available through filters/routes and ranks `latest eligible bortz age - worst eligible bortz age`. Lower and more negative values rank higher, then the same clock's age reduction, then older date of birth, then name.
- Pheno/Bortz Improvement top-10 placement changes emit Events after the initial stored placement snapshot; keep these separate from biological-age improvement Events.
- Guess My Age submissions are server-rate-limited before increasing `CrowdCount`: one accepted realistic guess per client IP and athlete during the configured short window.
- Existing Amateur athletes emit a went Pro Event when they first gain an eligible Bortz Age result; athletes who join already Pro only emit normal join/rank Events.
- Existing athletes emit a biological-age improvement Event when a changed result lowers their stored best pheno age or Bortz Age; this is separate from Pheno/Bortz Improvement leaderboard metrics.
- Crowd Age top-10 placement changes emit Events after the initial stored placement snapshot; they follow the separate Crowd Age leaderboard ordering and must not affect Ultimate League ranking.

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
- Homepage highlights are intentionally curated, not a raw Event feed; preserve same-athlete de-duplication for fresh Events, keep stale historical Events from suppressing newer Events by the same athlete, and preserve the fourth-visit highlights-before-podium layout behavior when changing `index.html` or event-board rendering.

## Social API Token Maintenance

- Threads access token maintenance is part of the daily Threads social post job, even on days with no postable content.
- `ThreadsAccessTokenExpiresAtUtc` and `ThreadsAccessTokenLastRefreshAttemptAtUtc` are runtime config metadata used to avoid silent token expiry; keep them in sync when manually replacing `ThreadsAccessToken`. Runtime social token updates may be persisted in `/var/www/.longevityworldcup/runtime-config.json` when `publish/config.json` is not writable, so update or remove that sidecar if you need a manual config replacement to take precedence immediately.
- Expired Threads tokens cannot be recovered in code. Generate a fresh token in Meta, then let the app refresh it proactively before future expiry.

## Keep This File Updated

- If you modify behavior in any of the areas described above, update this file as part of the same change.
- If a rule here stops being accurate, do not leave the file stale. Adjust it together with the code change.
