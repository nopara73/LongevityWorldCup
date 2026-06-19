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
- Longevitymaxxing Challenge daily check-ins continue indefinitely after Day 14 on the same live leaderboard. Keep adding Day 15, Day 16, and later daily grid columns; do not archive, freeze, split, or create a preserved winners board for the leaderboard.
- Longevitymaxxing Challenge live leaderboard performance metrics are rolling: total points rank first, then checked-in days, current streak, category leader badges, and leaderboard tie-breaks count only the latest 14 challenge days while the daily grid still shows the full check-in history.
- Longevitymaxxing Challenge Day 14 completion/result Events still emit after the existing grace window and may say participants completed the Longevitymaxxing Challenge, even though the live check-in leaderboard continues afterward.
- Longevitymaxxing Challenge signup stays open during the ongoing challenge. New signups join the same global leaderboard, see prior global days as empty/missed, and may only check in for days on or after their local signup date.
- Longevitymaxxing Challenge signup/profile identity asks whether the participant is already a Longevity athlete. Linked participants use the selected athlete profile as their identity and display name, and athlete profiles can only be linked once. Non-athletes choose a username that must not collide with challenge participants or Longevity athlete names.
- Each participant's first eligible Longevitymaxxing Challenge check-in is practice: it counts for checked-in days and streak, but not habit points, category leader badges, point tie-breaks, or missed-scored-day reminder stops.
- Longevitymaxxing Challenge call times are fixed by the generated weekly community-call schedule; participants do not vote on call availability.
- Longevitymaxxing Challenge community calls happen every Sunday at 08:30 GMT+2 / 06:30 UTC. The ongoing challenge generates future Sunday calls automatically; do not model calls as a finite kickoff/midpoint/finale schedule.
- Longevitymaxxing Challenge daily reminder emails default to 07:00 in each participant's local timezone and may catch up later that same local day if the exact hour is missed.
- Longevitymaxxing Challenge daily reminder emails continue indefinitely and stop after 3 consecutive missed scored days. Practice does not count, and days before a participant's local signup date do not count.
- Longevitymaxxing Challenge participant check-in notes and note photos submitted after the June 19, 2026 public-notes cutoff are public on the challenge page. Notes/photos from earlier check-ins stay hidden from unauthenticated public state, but authenticated challenge participants can see the shared participant notes feed.
- Longevitymaxxing Challenge habit points use a small day-weight ramp after practice: Day 2 starts at the raw 8-point maximum, the original Day 14 peak is 11 points, and later days stay capped at that peak unless scoring is explicitly redesigned.
- Longevitymaxxing Challenge habit points allow one daily slip only after an actually perfect previous check-in: either one `No` territory or one/two `Somewhat` territories still score that day's maximum, but a saved slip is not perfect for saving the next day.
- Longevitymaxxing Challenge commitment payments require each participant to configure a USD amount of at least `$1`.
- Longevitymaxxing Challenge existing participants without a commitment amount do not see or get blocked by commitment setup until the original 14-day challenge has concluded. On Day 15 and later, they must configure an amount before continuing.
- Longevitymaxxing Challenge commitment payments trigger only when a submitted scored check-in scores below the exact average of the participant's previous scored check-ins: the 4th scored check-in uses the previous 3, then later checks use up to the previous 7.
- Longevitymaxxing Challenge commitment payment blocks save the triggering check-in, lock the owed amount, and block the participant panel until the participant pays through BTCPay or edits the still-eligible triggering check-in enough to pass its original average threshold.
- Longevitymaxxing Challenge payment reminders send only while the triggering check-in remains editable. If the block remains unpaid after that window, challenge notifications stop and the participant is hidden as inactive until payment later reactivates notifications.
- Longevitymaxxing Challenge public leaderboard rows may show `Commitment due`, but commitment amounts are private and must not be exposed in public state.
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
