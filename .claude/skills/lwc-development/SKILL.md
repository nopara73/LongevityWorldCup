---
name: lwc-development
description: Build, debug, review, and test Longevity World Cup's ASP.NET Core backend and frontend using the repository's existing tools.
---

# Longevity World Cup development

Follow `AGENTS.md`. Read the SDK pin in `global.json` and Node version in `.node-version`; consult source and existing tests before changing behavior. Use Microsoft's documentation for unfamiliar APIs in the pinned SDK.

From the repository root:

```sh
dotnet restore LongevityWorldCup.sln
dotnet build LongevityWorldCup.sln --no-restore
npm --prefix LongevityWorldCup.Website run check
dotnet test LongevityWorldCup.Tests/LongevityWorldCup.Tests.csproj --no-build --filter "FullyQualifiedName~RelevantTestClass"
```

Normal .NET builds compile the frontend; `npm --prefix LongevityWorldCup.Website run build` builds it separately. Use existing Microsoft.Playwright/browser tools for affected UI checks. C# language tools can help navigation when available; builds and tests verify correctness.

Use `.claude/launch.json` for preview. Keep Development, `EnableScheduledJobs=false`, `EnableStartupBadgeRefresh=false`, and `EnableApplicationPaymentReconciliation=false`. Follow the shared Git publication rules in `AGENTS.md`.
