# Longevity World Cup
[![Watch the video](https://img.youtube.com/vi/Kq0VkLF3Z4Q/0.jpg)](https://www.youtube.com/shorts/Kq0VkLF3Z4Q)

Longevity World Cup is an open competition where longevity athletes rank by improving biological age measures. Athletes submit biomarker data, biological aging clocks turn those results into biological age, and the leaderboard ranks athletes by Age Reduction.

For more context, read the [project story](LongevityWorldCup.Documentation/About.md), the [competition rules](LongevityWorldCup.Documentation/Ruleset.md), and the [history of longevity as a sport](LongevityWorldCup.Documentation/LongevitySportHistory.md).

Created by [Ádám Ficsór (nopara73)](https://adamficsor.com/) and built with the community.

## Website

https://www.longevityworldcup.com/

## Roadmap
- [x] 2024 Sept: Inception
- [x] 2024 Oct: Design
- [x] 2024 Nov-Dec: Code
- [x] 2025 Jan - Sept: Beta Testing
- [x] 2025 Sept 16st: Launch
- [x] 2026 Jan 15st: Season 1 End
- [x] 2026 Feb: Season 2 Start
- [ ] 2027 Jan 15st: Season 2 End

## Build From Source Code

### Get The Requirements

1. Get Git: https://git-scm.com/downloads
2. Get .NET 10 SDK: https://dotnet.microsoft.com/download
3. Disable .NET's telemetry by executing in the terminal `export DOTNET_CLI_TELEMETRY_OPTOUT=1` on Linux and macOS or `setx DOTNET_CLI_TELEMETRY_OPTOUT 1` on Windows.
4. Get Visual Studio with ASP.NET web development installed: https://visualstudio.microsoft.com/

### Clone

```sh
git clone https://github.com/nopara73/LongevityWorldCup.git
```

### Run

Open the `.sln` file with Visual Studio & run the project.

### Update

```sh
git pull
```

## [Deployment](LongevityWorldCup.Documentation/ServerDeployment.md)

## Tests

Run `dotnet test` from the repository root. SEO regression assertions run automatically
inside the existing route, calculator, and sitemap tests, including in CI. They reuse
responses and parsed JSON already obtained by those tests, adding no HTTP requests,
application hosts, browser launches, or subprocesses.

Browser tests should wait for a locator, state, request/response, or fixture signal
instead of guessing readiness with a sleep. CI rejects new or edited
`WaitForTimeoutAsync`, `Task.Delay`, `Thread.Sleep`, and common inline JavaScript
promise/`setTimeout` sleeps in Playwright test sources and browser helpers. Unchanged
legacy waits and ordinary backend tests are left alone.

If elapsed time is part of the test (an observation window, slow fixture, gesture,
deadline, or bounded polling interval), give each wait its own reason immediately
above it or in a trailing comment:

```csharp
// Intentional wait: Outlive the dismissal timer to catch an unwanted reopen.
await page.WaitForTimeoutAsync(550);
```

Use the same comment immediately above an inline JavaScript sleep promise. Bare markers
are rejected. Locator/signal waits, virtual-clock operations, and zero-delay JavaScript
queue checkpoints need no opt-in. Indirect scripts and custom sleep wrappers still
need review. To check a committed range locally (not uncommitted edits), run
`python3 -B .github/scripts/check_browser_waits.py --base <base-commit> --head HEAD`.

