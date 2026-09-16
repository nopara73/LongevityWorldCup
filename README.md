# Longevity World Cup
[![Watch the video](https://img.youtube.com/vi/Kq0VkLF3Z4Q/0.jpg)](https://www.youtube.com/shorts/Kq0VkLF3Z4Q)

Longevity World Cup is an open competition where longevity athletes rank by improving biological age measures. Athletes submit biomarker data, biological aging clocks turn those results into biological age, and the leaderboard ranks athletes by Age Reduction.

For more context, read the [project story](LongevityWorldCup.Documentation/About.md), the [competition rules](LongevityWorldCup.Documentation/Ruleset.md), and the [history of longevity as a sport](LongevityWorldCup.Documentation/LongevitySportHistory.md).

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

## Manual SEO check

From the repository root, using the PowerShell 7 runtime already used by the repository:

```sh
pwsh -NoProfile -File Scripts/check-seo.ps1 -BaseUrl https://longevityworldcup.com
```

The command checks `/`, `/leaderboard`, and `/pheno-age` for HTTP 200, canonical URLs,
consistent public indexing directives, and valid JSON-LD with a matching page URL and name.
It also checks those URLs appear once in the sitemap and compares the sitemap's GET/HEAD
status, content type, and cache policy. It does not assert changing ranks, counts, or dates.

Expect five `PASS` lines and `SEO check: 0 failure(s), <seconds>s`. A failure identifies
the check and route; exit codes are 0 for success, 1 for a failed check or request, and 2
for an invalid base URL. Up to five read-only requests run sequentially, reuse their responses,
and each have a 10-second timeout and a 4 MiB response limit. There are no redirects,
retries, sitemap crawling, dependencies to install, browsers, or application launches.
A healthy live run normally takes a few seconds; network timeouts can take about 50 seconds.
For an already-running local or staging site, change `-BaseUrl` to its HTTP(S) origin;
the expected canonical origin remains `https://longevityworldcup.com`, matching the application.

This is intentionally excluded from normal tests, builds, CI, and deployment execution.
Nothing invokes it automatically: this trades automatic detection for no added test-run work.

