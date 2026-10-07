---
name: lwc-development
description: Develop, debug, review, and test Longevity World Cup's .NET 10 ASP.NET Core backend, TypeScript/classic JavaScript frontend, SQLite data, rankings, onboarding, calculators, and UI. Use for repository engineering tasks, build failures, bug fixes, or verification.
---

# Longevity World Cup development

Follow `AGENTS.md`, which `CLAUDE.md` imports. Read the required design, domain, and deployment documents when the task touches those areas. Treat source and existing tests as the authority for implementation details.

## Orient before changing code

1. Inspect the task's owning source, relevant tests, `git status`, and recent history when it helps identify a regression.
2. Confirm the SDK from `global.json` and Node version from `.node-version`; preserve `net10.0`, nullable annotations, and the frontend's strict TypeScript options.
3. Trace the complete path: page/partial, browser source, controller, service/calculator, and persistence. Avoid parallel implementations of the same rule.
4. Fix the underlying issue within its scope. Inspect sibling flows and changes introduced alongside a regression. Refactor when existing structure causes or conceals the bug.

## ASP.NET Core and C#

- Preserve the current controller-based API and HTML/partial rendering architecture. Do not introduce a SPA framework, ORM, or alternate hosting model just to implement a bounded feature.
- Use existing dependency injection, `ILogger<T>`, configuration, `IHttpClientFactory`, cancellation, and database patterns. Keep domain calculations out of controllers and middleware.
- Choose service lifetimes deliberately. Do not inject request-scoped state into singletons; pass request cancellation through asynchronous work and avoid blocking `.Result`/`.Wait()` in request paths.
- Preserve public route, JSON, status-code, and validation contracts. Follow existing authentication, authorization, rate-limit, and CORS boundaries.
- Middleware ordering affects HTML injection, routing, caching, static assets, redirects, and proxy headers. Read the actual pipeline before changing it and cover its callers.
- Use parameterized SQLite operations and existing schema/migration patterns. Keep integration tests isolated through `TestWebApplicationFactory`; do not aim tests at production or a user's live local database.
- Follow `HtmlInjectionMiddleware`, `HtmlAssetPlaceholders`, and `AssetVersionProvider.AppendVersion(...)` for injected assets. Preserve versioning across pages, modals, iframes, profile/proof data, and generated pages.
- Use the C# language server for definitions, references, and symbols when available. Build and test remain the authority for correctness.
- Verify unfamiliar or version-sensitive APIs in Microsoft's official .NET 10 documentation. Start with the smallest relevant reference:
  - [ASP.NET Core fundamentals](https://learn.microsoft.com/aspnet/core/fundamentals/?view=aspnetcore-10.0)
  - [Middleware](https://learn.microsoft.com/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0)
  - [Controller-based APIs](https://learn.microsoft.com/aspnet/core/web-api/?view=aspnetcore-10.0)
  - [Dependency injection](https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0)
  - [Integration tests](https://learn.microsoft.com/aspnet/core/test/integration-tests?view=aspnetcore-10.0)

## Frontend and product

- Edit `LongevityWorldCup.Website/Frontend`, shared CSS, source HTML/partials, or the owning page generator. Do not edit generated `wwwroot/js`, dialog CSS, or generated pages.
- Read `LongevityWorldCup.Website/Frontend/README.md` before changing loading, script extraction, modules, globals, versioning, validation, or hydration.
- Preserve classic-script scope, parser order, `window.modulesReady`, and route-specific initialization. Do not add imports/exports to classic scripts or bundle them without an explicit architecture task.
- Preserve server-rendered content during hydration and failed data requests. Check returning users, restored drafts/filters, direct links, modals, and embedded contexts when affected.
- Keep domain wording consistent with `UBIQUITOUS_LANGUAGE.md`. Preserve humor, warmth, established phrases, responsive layouts, focus, keyboard controls, and reduced-motion behavior.
- Prefer visual simplification over extra explanatory copy. Make UI solutions fit the existing product rather than rewriting adjacent copy.

## Build, test, and preview

From the repository root:

```sh
dotnet restore LongevityWorldCup.sln
dotnet build LongevityWorldCup.sln --no-restore
npm --prefix LongevityWorldCup.Website run check
dotnet test LongevityWorldCup.Tests/LongevityWorldCup.Tests.csproj --no-build --filter "FullyQualifiedName~RelevantTestClass"
```

After frontend changes, use `npm --prefix LongevityWorldCup.Website run build`; normal .NET builds compile it too. Use `BuildFrontend=false` only for the documented publish contract with the exact verified CI assets.

Select meaningful existing regression tests. Add tests for changed invariants and behavior when needed, avoiding tests that only mirror implementation. Run the broader suite when shared behavior or unresolved concerns justify it.

Use the repository's Microsoft.Playwright browser setup or Desktop Browser preview. If Chromium is missing after building tests:

```powershell
pwsh LongevityWorldCup.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Do not add npm packages or a separate Playwright project solely for smoke tests. Use `.artifacts/` for screenshots and logs. A local preview must run in Development with `EnableScheduledJobs=false`, `EnableStartupBadgeRefresh=false`, and `EnableApplicationPaymentReconciliation=false`; `.claude/launch.json` supplies these settings.

## Finish the authorized task

Review the final diff, generated/untracked outputs, relevant documentation, and checks. Follow the Git publication rules in `AGENTS.md`, preserving other agents' changes. Report what changed, what passed, the verified remote commit or PR, and any concrete limitation. Distinguish local verification, GitHub publication, CI, and deployed production behavior.
