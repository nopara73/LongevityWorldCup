using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class OpenDataProfileBrowserTests
{
    [Fact]
    public async Task CommittedPublicDataDeepLink_LoadsItsModulesAndOpensTheUnrankedModal()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        var response = await page.GotoAsync(
            "/public-data/ben-greenfield",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);

        await page.Locator("#detailsModal .modal-content.open-data-profile").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });
        Assert.Equal("/public-data/ben-greenfield", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Ben Greenfield", await page.Locator("#athleteName").InnerTextAsync());
        Assert.True(await page.Locator("#openDataProfileDisclosure").IsVisibleAsync());
        Assert.Equal("noindex, follow", await page.Locator("meta[name='robots']").GetAttributeAsync("content"));
        Assert.Contains("/public-data/ben-greenfield", await page.Locator("link[rel='canonical']").GetAttributeAsync("href") ?? "");
        Assert.Empty(errors);

        await page.GotoAsync(
            "/public-data/ben-greenfield?athlete=siim-land",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#detailsModal .modal-content.open-data-profile").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });
        Assert.Equal("Ben Greenfield", await page.Locator("#athleteName").InnerTextAsync());

        await page.EvaluateAsync("() => localStorage.setItem('gmaSkipAll', 'true')");
        await page.GotoAsync(
            "/athlete/siim-land?publicData=ben-greenfield",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        try
        {
            await page.WaitForFunctionAsync(
                "() => document.querySelector('#athleteName')?.textContent?.startsWith('Siim Land')",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
        }
        catch (TimeoutException exception)
        {
            var state = await page.EvaluateAsync<string>(
                "() => JSON.stringify({ url: location.href, title: document.title, name: document.querySelector('#athleteName')?.textContent, modalClass: document.querySelector('#detailsModal .modal-content')?.className, body: document.body?.innerText.slice(0, 500) })");
            throw new InvalidOperationException($"Typed athlete route did not win over the legacy query parameter: {state}", exception);
        }
        Assert.Equal("/athlete/siim-land", new Uri(page.Url).AbsolutePath);
        Assert.Equal(string.Empty, new Uri(page.Url).Query);
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('open-data-profile')"));

        await page.GotoAsync(
            "/athlete/ben-greenfield",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForURLAsync("**/error/404.html");
        Assert.Equal("404 not found", await page.Locator("h1").InnerTextAsync());
    }

    [Fact]
    public async Task Leaderboard_SeparatesSearchesAndOpensUnrankedPublicDataProfiles()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/data/leaderboard-profiles", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = LeaderboardProfilesJson
            }));

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        var leaderboardProfileRequests = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        page.SetDefaultTimeout(5_000);
        page.SetDefaultNavigationTimeout(10_000);
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/data/leaderboard-profiles", StringComparison.OrdinalIgnoreCase))
                leaderboardProfileRequests.Add(request.Url);
        };

        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var publicDataCard = page.Locator("#openDataProfilesGrid .open-data-card");
        try
        {
            await publicDataCard.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        }
        catch (TimeoutException exception)
        {
            var publicDataSectionState = await page.Locator("#openDataProfilesSection").EvaluateAsync<string>(
                "section => JSON.stringify({ hidden: section.hidden, childCount: section.querySelectorAll('.open-data-card').length })");
            throw new InvalidOperationException(
                $"Expected a public-data card. Requests: {string.Join(", ", leaderboardProfileRequests)}; section: {publicDataSectionState}; errors: {string.Join(" | ", errors)}",
                exception);
        }

        Assert.Equal(1, await page.Locator(".leaderboard table tbody tr[data-athlete-name]").CountAsync());
        Assert.Equal(1, await publicDataCard.CountAsync());
        Assert.Equal("PUBLIC DATA · UNRANKED", await publicDataCard.Locator(".open-data-token").InnerTextAsync());
        Assert.Contains("never affect rankings, awards, or prizes", await page.Locator("#openDataProfilesSection .open-data-explainer").InnerTextAsync());
        Assert.Equal("Reference Pheno difference", await publicDataCard.Locator(".open-data-card-metric-label").TextContentAsync());
        Assert.Equal(
            new[] { "Pheno Age − age at published draw", "Uses a published assay boundary" },
            await publicDataCard.Locator(".open-data-card-metric-note").AllTextContentsAsync());
        Assert.Equal(0, await page.Locator(".leaderboard table tbody tr[data-athlete-name='Public Browser Subject']").CountAsync());

        var search = page.Locator("#athleteSearch");
        await search.FillAsync("not in this profile");
        await page.WaitForFunctionAsync("() => document.querySelector('.open-data-card')?.hidden === true");
        await search.FillAsync("Published Browser Alias");
        await page.WaitForFunctionAsync("() => document.querySelector('.open-data-card')?.hidden === false");
        await search.FillAsync("normalized from the published units");
        await page.WaitForFunctionAsync("() => document.querySelector('.open-data-card')?.hidden === false");

        var openProfileButton = publicDataCard.GetByRole(AriaRole.Button, new() { Name = "View unranked public-data profile for Public Browser Subject" });
        await openProfileButton.FocusAsync();
        await openProfileButton.ClickAsync();
        await page.Locator("#detailsModal .modal-content.open-data-profile").WaitForAsync();
        await page.WaitForURLAsync("**/public-data/public-browser-subject");

        Assert.Equal("/public-data/public-browser-subject", new Uri(page.Url).AbsolutePath);
        Assert.Equal("closeAthleteDetailsModal", await page.EvaluateAsync<string>("() => document.activeElement?.id || ''"));
        Assert.True(await page.Locator("#openDataProfileDisclosure").IsVisibleAsync());
        var disclosure = await page.Locator("#openDataProfileDisclosure").InnerTextAsync();
        Assert.Contains("did not apply to the Longevity World Cup", disclosure);
        Assert.Contains("inclusion does not imply endorsement", disclosure);
        Assert.Contains("Request a correction or removal", disclosure);
        Assert.Equal("openDataProfileDisclosure", await page.Locator("#detailsModal").GetAttributeAsync("aria-describedby"));
        Assert.Equal("Close public-data profile", await page.Locator("#closeAthleteDetailsModal").GetAttributeAsync("aria-label"));
        Assert.Equal("Age at published draw:", await page.Locator("#chronologicalAgeLabel").InnerTextAsync());
        Assert.Equal("47.7", await page.Locator("#chronologicalAge").InnerTextAsync());
        Assert.Equal("Reference Pheno Age:", await page.Locator("#lowestPhenoAgeLabel").InnerTextAsync());
        Assert.False(await page.Locator("#paceOfAgingContainer").IsVisibleAsync());
        Assert.True(await page.Locator("#openDataQualifierNotice").IsVisibleAsync());
        Assert.Contains("not presented as an exact measurement", await page.Locator("#openDataQualifierNotice").InnerTextAsync());
        await page.Locator("#publicDataSources").ScrollIntoViewIfNeededAsync();
        Assert.True(await page.Locator("#publicDataSources").IsVisibleAsync());
        await page.Locator("#modalStickyHeader.visible").WaitForAsync();
        var sourceLink = page.Locator("#publicDataSourceList a");
        Assert.Equal("https://example.com/public-labs", (await sourceLink.GetAttributeAsync("href"))?.TrimEnd('/'));
        Assert.Equal("_blank", await sourceLink.GetAttributeAsync("target"));
        Assert.Contains("noopener", await sourceLink.GetAttributeAsync("rel") ?? string.Empty);
        Assert.Contains("Accessed Jul 10, 2026", await page.Locator("#publicDataSourceList").InnerTextAsync());
        Assert.Contains("normalized from the published units", await page.Locator("#publicDataTranscriptionNotes").InnerTextAsync());
        Assert.Contains("month precision; no day was inferred", await page.Locator("#publicDataReviewed").InnerTextAsync());

        await page.EvaluateAsync(
            """
            () => {
                const modal = document.querySelector('#detailsModal');
                const focusable = Array.from(modal.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
                    .filter(element => !element.hidden && element.getAttribute('aria-hidden') !== 'true' && element.offsetParent !== null && getComputedStyle(element).visibility !== 'hidden');
                focusable[focusable.length - 1]?.focus();
            }
            """);
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("stickyCloseBtn", await page.EvaluateAsync<string>("() => document.activeElement?.id || ''"));

        await page.SetViewportSizeAsync(390, 844);
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("element => { element.scrollTop = element.scrollHeight; }");
        await page.Locator("#modalStickyHeader.visible").WaitForAsync();
        Assert.True(await page.Locator("#openDataStickyToken").IsVisibleAsync());
        Assert.Equal("PUBLIC DATA · UNRANKED", await page.Locator("#openDataStickyToken").InnerTextAsync());

        foreach (var selector in new[]
                 {
                     ".official-profile-only",
                     ".rank-annotation",
                     ".pro-badge",
                     "#modalBadgeStrip",
                     "#guessAgeContainer",
                     "#ageVisualization",
                     ".events-embed",
                     ".proofs-section",
                     "#personalLink",
                     "#mediaContact"
                 })
        {
            Assert.False(await page.Locator($"#detailsModal {selector}").First.IsVisibleAsync(),
                $"Competition-only UI remained visible for selector {selector}.");
        }

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForURLAsync("**/leaderboard**");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('aria-label') === 'View unranked public-data profile for Public Browser Subject'");
        Assert.True(await openProfileButton.EvaluateAsync<bool>("button => button === document.activeElement"));
        await search.FillAsync(string.Empty);
        await page.EvaluateAsync("() => localStorage.setItem('gmaSkipAll', 'true')");
        await page.Locator(".leaderboard table tbody tr[data-athlete-name='Official Browser Athlete']").ClickAsync();
        await page.WaitForURLAsync("**/athlete/official-browser-athlete");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('open-data-profile')");

        Assert.Null(await page.Locator("#detailsModal").GetAttributeAsync("aria-describedby"));
        Assert.Equal("Close athlete details", await page.Locator("#closeAthleteDetailsModal").GetAttributeAsync("aria-label"));
        Assert.False(await page.Locator("#publicDataSources").IsVisibleAsync());
        Assert.True(await page.Locator("#detailsModal .official-profile-only").First.IsVisibleAsync());

        Assert.Empty(errors);
    }

    private const string LeaderboardProfilesJson =
        """
        [
          {
            "ProfileType": "Athlete",
            "AthleteSlug": "official_browser_athlete",
            "Name": "Official Browser Athlete",
            "DisplayName": "Official Browser Athlete",
            "DateOfBirth": { "Year": 1984, "Month": 2, "Day": 3 },
            "Division": "Open",
            "Flag": "Hungary",
            "Why": "An approved athlete used to verify the ranked table boundary.",
            "PersonalLink": "https://example.com/official-athlete",
            "MediaContact": "official-athlete@example.com",
            "ProfilePic": "/assets/content-images/headshot.jpg",
            "Biomarkers": [
              {
                "Date": "2026-01-15",
                "Wbc1000cellsuL": 5.0,
                "LymPc": 30.0,
                "McvFL": 90.0,
                "RdwPc": 12.5,
                "AlbGL": 45.0,
                "AlpUL": 65.0,
                "CreatUmolL": 80.0,
                "GluMmolL": 5.0,
                "CrpMgL": 0.7
              }
            ]
          },
          {
            "ProfileType": "OpenData",
            "AthleteSlug": "public_browser_subject",
            "Name": "Public Browser Subject",
            "DisplayName": "Public Browser Subject",
            "Biomarkers": [
              {
                "Date": "2026-02-01",
                "DatePrecision": "Month",
                "AgeYears": 47.7,
                "Wbc1000cellsuL": 4.8,
                "LymPc": 31.0,
                "McvFL": 89.0,
                "RdwPc": 12.2,
                "AlbGL": 46.0,
                "AlpUL": 62.0,
                "CreatUmolL": 77.0,
                "GluMmolL": 4.9,
                "CrpMgL": 0.6,
                "MeasurementQualifiers": { "CrpMgL": "<" },
                "SourceIds": ["bloodwork-1"]
              }
            ],
            "OpenData": {
              "SubjectDidNotApply": true,
              "ReviewedAt": "2026-07-10",
              "Aliases": ["Published Browser Alias"],
              "IdentitySourceIds": ["bloodwork-1"],
              "Sources": [
                {
                  "Id": "bloodwork-1",
                  "Kind": "Bloodwork",
                  "Title": "Published lab panel",
                  "Url": "https://example.com/public-labs",
                  "AccessedOn": "2026-07-10",
                  "SelfPublishedBySubject": true
                }
              ],
              "TranscriptionNotes": [
                "Values were normalized from the published units."
              ]
            }
          }
        ]
        """;
}
