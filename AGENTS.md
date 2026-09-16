# LongevityWorldCup Agent Notes

## Product Personality

- Preserve the product's personality across the UI, emails, notifications, and social copy. Humor, warmth, wordplay, informal phrasing, distinctive punctuation, established names, and playful visuals are intentional product features.
- Requests for cleanup, brevity, consistency, or less clutter do not authorize removing that personality or rewriting neighboring copy. Fix the specific problem while preserving the established voice. When in doubt, keep the existing wording.
- User-approved wording is authoritative. Keep "Questions, concerns, or signs of aging? Reply to this email." and "Update profile request..." exactly unless the user explicitly asks to change them.
- Apply visual simplicity and concise-copy guidance within these rules; do not use those guidelines to flatten the product's voice.

## Required Reading

- UI and product-copy changes: [DESIGN.md](DESIGN.md).
- Domain, ranking, onboarding, calculator, badge, Event, social-posting, or competition-copy changes: [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md).
- Production changes over SSH: [ServerDeployment.md](LongevityWorldCup.Documentation/ServerDeployment.md).

Update the relevant guidance when behavior changes. Keep domain rules in the glossary and implementation detail in source, tests, or focused docs.

## Implementation

- Fix the underlying invariant, inspect its other implementations, and refactor within that scope when structure causes or conceals bugs. Review both backend and frontend ranking logic when either changes.
- Put temporary agent outputs in ignored `.artifacts/`; keep disposable files out of tracked folders unless requested.
- Do not merge ImageSharp v4+ or ImageSharp.Drawing v3+ until the project adopts their licensing path or removes those direct dependencies. Current-major patch/minor upgrades require passing CI and dependency review.
- Frontend source is `LongevityWorldCup.Website/Frontend`; generated `wwwroot/js` is ignored and must never be committed. Normal builds compile it. Reserve `BuildFrontend=false` for the documented Node-free publish using the exact CI-built artifact. See [Frontend/README.md](LongevityWorldCup.Website/Frontend/README.md) for loading contracts.
- Injected HTML and partials use placeholders through `HtmlInjectionMiddleware` and `AssetVersionProvider.AppendVersion(...)`. Preserve versioning for scripts, CSS, assets, favicon, manifest, shared logo, and bioage onboarding/rank previews. A raw URL exception needs a verified cache rationale. Check every calling page, modal, iframe, and embedded context. The data service must also version athlete profile/proof URLs.

## Browser Checks

Use the repo's `Microsoft.Playwright` setup in `LongevityWorldCup.Tests`, or the Codex browser. Do not add `package.json` or install Node tooling solely for smoke tests unless requested. Verify any separate Playwright runtime and its browser binaries first.

If Chromium is missing after building tests:

```powershell
pwsh LongevityWorldCup.Tests\bin\Debug\net10.0\playwright.ps1 install chromium
```

## Production

- Try `ssh lwc-server` before asking the user to run server checks. Prefer read-only inspection; make only required production changes and follow the deployment doc's paths and preservation rules.
- Maintain Threads tokens during the daily job even without postable content. When replacing `ThreadsAccessToken`, synchronize `ThreadsAccessTokenExpiresAtUtc` and `ThreadsAccessTokenLastRefreshAttemptAtUtc`; expired tokens cannot be recovered in code.
- Manual social-token resets must account for `/var/www/.longevityworldcup/runtime-config.json`; update or remove the sidecar when it would override the intended config.
