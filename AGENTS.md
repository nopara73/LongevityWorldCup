# LongevityWorldCup Agent Notes

This file is for project-specific routing and "do not break this" rules. Keep detailed domain behavior in source, tests, or focused documentation instead of expanding this file.

## Required Reading

- Before user-facing UI changes, read `DESIGN.md` and preserve its visual, layout, typography, and interaction rules.
- Before domain, leaderboard, ranking, athlete onboarding, biological age calculator, badge, Event, social posting, or competition-copy changes, read `UBIQUITOUS_LANGUAGE.md`.
- Before production changes over SSH, read `LongevityWorldCup.Documentation/ServerDeployment.md`.

## Browser Smoke Checks

This is a .NET solution, not a Node app. Use the repo-owned `Microsoft.Playwright` setup in `LongevityWorldCup.Tests` for repeatable browser checks.

Do not add `package.json` or run npm installs just for agent-side smoke testing unless the user explicitly asks for Node tooling. If browser binaries are missing after building tests, install Chromium with:

```powershell
pwsh LongevityWorldCup.Tests\bin\Debug\net10.0\playwright.ps1 install chromium
```

The Codex Browser plugin or in-app browser is also acceptable for local UI verification. If using any separate Playwright runtime, first verify both Playwright and browser binaries resolve there.

## Local Agent Artifacts

Put temporary agent-only outputs under the ignored `.artifacts/` directory. This includes screenshots, logs, rendered previews, generated images, reports, exports, and smoke-test captures.

Do not put disposable files in tracked folders such as `output/`, `wwwroot/`, `docs/`, or test fixture directories unless the user explicitly asks for a committed artifact there.

## Dependency Constraints

Do not merge major upgrades for `SixLabors.ImageSharp` or `SixLabors.ImageSharp.Drawing` unless the project has intentionally adopted the Six Labors v4+/Drawing v3+ licensing path or removed those direct dependencies. Patch and minor upgrades on the current major line are acceptable when CI and dependency review pass.

## Production Access

When a task needs production inspection, logs, database checks, service status, or deployment verification, check whether `ssh lwc-server` works before asking the user to run server commands.

Prefer read-only SSH checks. Make production changes only when explicitly required, and follow the documented server paths, service names, runtime config locations, and preservation rules.

## Domain Guardrails

Use `UBIQUITOUS_LANGUAGE.md` for canonical terms and core invariants. If a behavior or term changes, update that doc in the same change.

Critical reminders:

- Keep the Longevitymaxxing Challenge separate from Ultimate League ranking, biological age placements, and athlete badges.
- Challenge daily check-ins continue indefinitely on the same live leaderboard after Day 14.
- Challenge signup stays open during the ongoing challenge; new signups join the same global leaderboard and may only check in from their local signup date onward.
- Each participant's first eligible Challenge check-in is practice: it counts for checked-in days and streak, but not habit points, category leader badges, point tie-breaks, or missed-scored-day reminder stops.
- Challenge daily reminders continue indefinitely and stop only after 3 consecutive missed scored days; practice and days before local signup do not count.
- Challenge commitment payment blocks save the triggering check-in, lock the owed amount, and block the participant panel until payment or an eligible edit passes the original threshold.
- Ranking logic exists in both backend and frontend. If one side changes, review and align the other.
- Bioage calculator rank previews are clock-specific: pheno age shows the Pheno-only field rank, and bortz age shows the Bortz-only field rank.
- Ultimate League Pro/Amateur ordering is domain behavior: Pro entries appear before Amateur entries.
- Crowd Age requires at least 100 accepted guesses and ranks by `CrowdAge - chronologicalAge`, then `CrowdCount`, date of birth, and name.
- Improvement leaderboards are separate from Ultimate League and rank `latest eligible age - worst eligible age` for the selected clock.
- Biological-age improvement Events, Crowd Age placement Events, and Pheno/Bortz Improvement placement Events are separate behaviors.
- Challenge commitment amounts are private; public rows may show only that a commitment is due.
- Challenge uploads and Gravatar fallbacks are challenge-only profile pictures; linked Longevity athlete profile pictures remain the display priority.

## Static Assets

Injected HTML and partials must use the shared middleware asset-versioning flow. Do not add raw `/js/...`, `/css/...`, `/assets/...`, favicon, or manifest URLs to injected HTML unless there is a strong reason and cache behavior has been verified.

When adding or moving injected-page assets, wire placeholders through `HtmlInjectionMiddleware` and `AssetVersionProvider.AppendVersion(...)`, then check every page, modal, iframe, or embedded context that calls the changed helper.

Keep favicon, web manifest, shared header logo, bioage onboarding CSS, and bioage rank preview JS on the existing middleware-placeholder path. Athlete profile and proof asset URLs should be versioned when emitted from the data service.

Homepage highlights are curated, not a raw Event feed. Preserve same-athlete de-duplication for fresh Events, stale-event handling, and the fourth-visit highlights-before-podium layout behavior.

## Social Tokens

Threads access token maintenance is part of the daily Threads job even when there is no postable content. Keep `ThreadsAccessTokenExpiresAtUtc` and `ThreadsAccessTokenLastRefreshAttemptAtUtc` in sync when replacing `ThreadsAccessToken`.

Runtime social token updates may persist in `/var/www/.longevityworldcup/runtime-config.json`; update or remove that sidecar if a manual config replacement must take effect immediately. Expired Threads tokens cannot be recovered in code.

## Keep Current

If a rule here stops being accurate, update this file with the code change. Keep this file short; move detailed behavior to focused documentation or tests.
