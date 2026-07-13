using Microsoft.Playwright;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class OpenDataProfileBrowserTests
{
    [Fact]
    public async Task CommittedPublicDataDeepLink_LoadsItsModulesAndOpensTheNonCompetingModal()
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
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#modalProfilePic')?.naturalWidth > 0");
        Assert.True(await page.Locator("#modalProfilePic").IsVisibleAsync());
        Assert.Equal("Portrait of Ben Greenfield", await page.Locator("#modalProfilePic").GetAttributeAsync("alt"));
        Assert.Contains("CC BY-SA 4.0", await page.Locator("#openDataModalPhotoCredit").InnerTextAsync());
        Assert.Equal("noindex, follow", await page.Locator("meta[name='robots']").GetAttributeAsync("content"));
        Assert.Contains("/public-data/ben-greenfield", await page.Locator("link[rel='canonical']").GetAttributeAsync("href") ?? "");
        Assert.Empty(errors);

        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#detailsModal").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 30_000 });
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        await page.GoBackAsync();
        await page.WaitForTimeoutAsync(500);
        Assert.NotEqual("/public-data/ben-greenfield", new Uri(page.Url).AbsolutePath);

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
    public async Task Leaderboard_InterleavesHypotheticalRowsWithoutChangingOfficialRanks()
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
                Body = LeaderboardProfilesWithTiedReferencesJson()
            }));
        await context.RouteAsync("**/public-data/*/portrait*", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "image/webp",
                Path = GetCommittedPortraitPath()
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
        page.SetDefaultTimeout(30_000);
        page.SetDefaultNavigationTimeout(30_000);
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/data/leaderboard-profiles", StringComparison.OrdinalIgnoreCase))
                leaderboardProfileRequests.Add(request.Url);
        };

        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var publicDataSection = page.Locator("#openDataProfilesSection");
        var publicDataCards = page.Locator("#openDataProfilesGrid .open-data-card");
        var publicDataCard = page.Locator("#openDataProfilesGrid .open-data-card[data-profile-slug='public_browser_subject']");
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

        var officialRows = page.Locator(".leaderboard table tbody tr[data-athlete-name]:not(.open-data-leaderboard-row)");
        var publicDataRow = page.Locator(".leaderboard table tbody tr.open-data-leaderboard-row[data-athlete-name='Public Browser Subject']");
        var tiedPublicDataRow = page.Locator(".leaderboard table tbody tr.open-data-leaderboard-row[data-athlete-name='Second Public Browser Subject']");
        var belowFieldPublicDataRow = page.Locator(".leaderboard table tbody tr.open-data-leaderboard-row[data-athlete-name='Below Field Public Browser Subject']");
        await publicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await tiedPublicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await belowFieldPublicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal(4, await officialRows.CountAsync());
        Assert.Equal(new[] { "1", "2", "3", "4" }, await officialRows.Locator(".rank").AllTextContentsAsync());
        Assert.Equal("HYP.", await publicDataRow.Locator(".open-data-hypothetical-prefix").InnerTextAsync());
        Assert.Equal("#3", await publicDataRow.Locator(".open-data-hypothetical-number").InnerTextAsync());
        Assert.Equal("3", await publicDataRow.GetAttributeAsync("data-hypothetical-rank"));
        Assert.Equal("#3", await tiedPublicDataRow.Locator(".open-data-hypothetical-number").InnerTextAsync());
        Assert.Equal("3", await tiedPublicDataRow.GetAttributeAsync("data-hypothetical-rank"));
        Assert.Equal("#5", await belowFieldPublicDataRow.Locator(".open-data-hypothetical-number").InnerTextAsync());
        Assert.Equal("5", await belowFieldPublicDataRow.GetAttributeAsync("data-hypothetical-rank"));
        Assert.Equal(
            "Compared independently with official athletes; official athlete ranks stay unchanged.",
            await belowFieldPublicDataRow.Locator(".rank-td").GetAttributeAsync("title"));
        Assert.Equal(
            new[] { "Official Older Athlete", "Official Tied Athlete", "Public Browser Subject", "Second Public Browser Subject", "Official Browser Athlete", "Official Younger Athlete", "Below Field Public Browser Subject" },
            await page.Locator(".leaderboard table tbody tr[data-athlete-name]:visible").EvaluateAllAsync<string[]>(
                "rows => rows.map(row => row.getAttribute('data-athlete-name'))"));
        Assert.Equal("PUBLIC DATA · DID NOT APPLY", await publicDataRow.Locator(".open-data-row-token").InnerTextAsync());
        Assert.Contains(
            "Sponsor: not applicable for a non-competing public-data profile.",
            await publicDataRow.Locator(".sponsor-td").TextContentAsync());
        Assert.Contains(
            "Media contact: not provided for this public-data profile.",
            await publicDataRow.Locator(".media-contact-td").TextContentAsync());
        Assert.Contains("rgba(124, 58, 237", await publicDataRow.EvaluateAsync<string>(
            "row => getComputedStyle(row).backgroundImage"));
        Assert.Equal(3, await publicDataCards.CountAsync());
        Assert.True(await publicDataSection.IsVisibleAsync());
        Assert.Equal(1, await publicDataCard.CountAsync());
        Assert.Equal("PUBLIC DATA · DID NOT APPLY", await publicDataCard.Locator(".open-data-token").InnerTextAsync());
        Assert.Equal(
            "A globally recognized public figure with an established body of work.",
            await publicDataCard.Locator(".open-data-card-summary").InnerTextAsync());
        Assert.Contains("never change official ranks or prizes", await page.Locator("#openDataProfilesSection .open-data-explainer").InnerTextAsync());
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.open-data-card-portrait')?.naturalWidth > 0");
        Assert.Equal("Portrait of Public Browser Subject", await publicDataCard.Locator(".open-data-card-portrait").GetAttributeAsync("alt"));
        Assert.Contains("CC BY 4.0", await publicDataCard.Locator(".open-data-photo-credit").InnerTextAsync());
        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await publicDataCard.Locator(".open-data-photo-credit").EvaluateAsync<bool>(
            "element => element.scrollWidth <= element.clientWidth && element.scrollHeight <= element.clientHeight && getComputedStyle(element).whiteSpace !== 'nowrap'"),
            "The complete portrait credit should wrap without clipping at the mobile breakpoint.");
        Assert.True(await publicDataRow.Locator(".open-data-hypothetical-rank").IsVisibleAsync());
        Assert.True(await publicDataRow.Locator(".open-data-row-token").IsVisibleAsync());
        Assert.True(await publicDataRow.EvaluateAsync<bool>(
            "row => row.getBoundingClientRect().right <= document.documentElement.clientWidth && row.scrollWidth <= row.clientWidth"),
            "The hypothetical row should not overflow the mobile leaderboard.");
        await page.SetViewportSizeAsync(1280, 900);
        Assert.Equal("Reference Pheno difference", await publicDataCard.Locator(".open-data-card-metric-label").TextContentAsync());
        Assert.Equal(
            new[] { "Pheno Age − age at published panel", "Independent comparison; official ranks stay unchanged", "Uses a published assay boundary" },
            await publicDataCard.Locator(".open-data-card-metric-note").AllTextContentsAsync());
        Assert.Equal("Hypothetical Ultimate #3 · Pheno #3", await publicDataCard.Locator(".open-data-card-hypothetical").InnerTextAsync());

        var search = page.Locator("#athleteSearch");
        await search.FillAsync("not in this profile");
        await page.WaitForFunctionAsync("() => document.querySelector('.open-data-card')?.hidden === true && document.querySelector('.open-data-leaderboard-row')?.hidden === true");
        Assert.True(await page.Locator(".leaderboard table tbody tr.no-results").IsVisibleAsync());
        await search.FillAsync("Published Browser Alias");
        await page.WaitForFunctionAsync("() => document.querySelector(\".open-data-card[data-profile-slug='public_browser_subject']\")?.hidden === false && document.querySelector(\".open-data-leaderboard-row[data-profile-slug='public_browser_subject']\")?.hidden === false");
        Assert.Equal(0, await page.Locator(".leaderboard table tbody tr.no-results").CountAsync());
        Assert.False(await tiedPublicDataRow.IsVisibleAsync());
        await search.FillAsync("normalized from the published units");
        await page.WaitForFunctionAsync("() => document.querySelector(\".open-data-card[data-profile-slug='public_browser_subject']\")?.hidden === false && document.querySelector(\".open-data-leaderboard-row[data-profile-slug='public_browser_subject']\")?.hidden === false");
        await search.FillAsync("established body of work");
        await page.WaitForFunctionAsync("() => document.querySelector(\".open-data-card[data-profile-slug='public_browser_subject']\")?.hidden === false && document.querySelector(\".open-data-leaderboard-row[data-profile-slug='public_browser_subject']\")?.hidden === false");

        var openProfileButton = publicDataRow.GetByRole(AriaRole.Button, new() { Name = "View public-data profile for Public Browser Subject" });
        await openProfileButton.FocusAsync();
        await openProfileButton.ClickAsync();
        await page.Locator("#detailsModal .modal-content.open-data-profile").WaitForAsync();
        await page.Locator("#publicDataSourceList > li").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForURLAsync("**/public-data/public-browser-subject");
        await page.WaitForTimeoutAsync(700); // Let the pending 400 ms filter debounce finish.

        Assert.Equal(
            "/public-data/public-browser-subject",
            await page.EvaluateAsync<string>("() => window.location.pathname"));
        Assert.Equal("closeAthleteDetailsModal", await page.EvaluateAsync<string>("() => document.activeElement?.id || ''"));
        Assert.True(await page.Locator("#openDataProfileDisclosure").IsVisibleAsync());
        Assert.Equal(
            "A globally recognized public figure with an established body of work.",
            await page.Locator("#openDataNotabilitySummary").InnerTextAsync());
        Assert.Equal(
            "Hypothetical Ultimate #3 · Pheno #3 · official ranks stay unchanged",
            await page.Locator("#openDataHypotheticalSummary").InnerTextAsync());
        var disclosure = await page.Locator("#openDataProfileDisclosure").InnerTextAsync();
        Assert.Contains("Hypothetical positions are comparisons only", disclosure);
        Assert.Contains("inclusion is not endorsement", disclosure);
        Assert.Contains("Corrections", disclosure);
        Assert.DoesNotContain("globally recognized public figure", disclosure);
        Assert.Equal("openDataNotabilitySummary openDataHypotheticalSummary openDataProfileDisclosure", await page.Locator("#detailsModal").GetAttributeAsync("aria-describedby"));
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#modalProfilePic')?.naturalWidth > 0");
        Assert.True(await page.Locator("#modalProfilePic").IsVisibleAsync());
        Assert.Equal("Portrait of Public Browser Subject", await page.Locator("#modalProfilePic").GetAttributeAsync("alt"));
        Assert.Contains("Browser Photographer", await page.Locator("#openDataModalPhotoCredit").InnerTextAsync());
        Assert.Contains("cropped", await page.Locator("#openDataModalPhotoCredit").InnerTextAsync());
        Assert.Equal("Close public-data profile", await page.Locator("#closeAthleteDetailsModal").GetAttributeAsync("aria-label"));
        Assert.Equal("Age at published panel:", await page.Locator("#chronologicalAgeLabel").InnerTextAsync());
        Assert.Equal("47.7", await page.Locator("#chronologicalAge").InnerTextAsync());
        Assert.Equal("Reference Pheno Age:", await page.Locator("#lowestPhenoAgeLabel").InnerTextAsync());
        Assert.False(await page.Locator("#paceOfAgingContainer").IsVisibleAsync());
        Assert.True(await page.Locator("#openDataQualifierNotice").IsVisibleAsync());
        Assert.Contains("not an exact measurement", await page.Locator("#openDataQualifierNotice").InnerTextAsync());
        await page.Locator("#publicDataSources").ScrollIntoViewIfNeededAsync();
        Assert.True(await page.Locator("#publicDataSources").IsVisibleAsync());
        await page.Locator("#modalStickyHeader.visible").WaitForAsync();
        var sourceLink = page.Locator("#publicDataSourceList a").First;
        Assert.Equal("https://example.com/public-labs", (await sourceLink.GetAttributeAsync("href"))?.TrimEnd('/'));
        Assert.Equal("_blank", await sourceLink.GetAttributeAsync("target"));
        Assert.Contains("noopener", await sourceLink.GetAttributeAsync("rel") ?? string.Empty);
        Assert.Contains("Accessed Jul 10, 2026", await page.Locator("#publicDataSourceList").InnerTextAsync());
        Assert.Contains("Publication explicitly authorized by the subject", await page.Locator("#publicDataSourceList").InnerTextAsync());
        Assert.Contains("Supports notability context", await page.Locator("#publicDataSourceList").InnerTextAsync());
        var authorizationLink = page.GetByRole(AriaRole.Link, new() { Name = "View subject authorization evidence (opens in a new tab)" });
        Assert.Equal("https://example.com/authorization", (await authorizationLink.GetAttributeAsync("href"))?.TrimEnd('/'));
        Assert.Contains("normalized from the published units", await page.Locator("#publicDataTranscriptionNotes").InnerTextAsync());
        Assert.Contains("month precision; no day was inferred", await page.Locator("#publicDataReviewed").InnerTextAsync());
        Assert.Contains("uses the published report date", await page.Locator("#publicDataReviewed").InnerTextAsync());

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
        Assert.Equal("PUBLIC DATA · DID NOT APPLY", await page.Locator("#openDataStickyToken").InnerTextAsync());
        Assert.True(await page.Locator("#openDataStickyToken").EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().right <= document.documentElement.clientWidth && element.scrollWidth <= element.clientWidth"),
            "The longer did-not-apply token should fit in the mobile sticky header.");

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

        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.Locator("#athleteShareMenu").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.EvaluateAsync("() => history.back()");
        await page.WaitForFunctionAsync("() => window.location.pathname === '/leaderboard'");
        await page.Locator("#detailsModal").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 30_000 });
        await page.EvaluateAsync("() => history.forward()");
        await page.WaitForURLAsync("**/public-data/public-browser-subject");
        await page.Locator("#detailsModal .modal-content.open-data-profile").WaitForAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#athleteName')?.textContent === 'Public Browser Subject'");
        Assert.True(await page.Locator("#openDataProfileDisclosure").IsVisibleAsync());

        // A rapid Back/Forward pair must cancel the pending close animation
        // instead of letting its timer hide the newly restored profile.
        await page.EvaluateAsync("() => history.back()");
        await page.WaitForFunctionAsync("() => window.location.pathname === '/leaderboard'");
        await page.EvaluateAsync("() => history.forward()");
        await page.WaitForURLAsync("**/public-data/public-browser-subject");
        await page.WaitForTimeoutAsync(600);
        Assert.True(await page.Locator("#detailsModal .modal-content.open-data-profile").IsVisibleAsync());

        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#detailsModal").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 30_000 });
        Assert.Equal("/leaderboard", await page.EvaluateAsync<string>("() => window.location.pathname"));
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('aria-label') === 'View public-data profile for Public Browser Subject'");
        Assert.True(await openProfileButton.EvaluateAsync<bool>("button => button === document.activeElement"));
        await search.FillAsync(string.Empty);
        await page.SetViewportSizeAsync(1280, 900);
        await publicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.Locator("label[for='view-pheno']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#view-pheno')?.checked === true");
        await page.WaitForURLAsync("**/league/pheno*");
        await publicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.False(await publicDataSection.IsVisibleAsync());
        Assert.Equal("#3", await publicDataRow.Locator(".open-data-hypothetical-number").InnerTextAsync());
        Assert.Equal(new[] { "1", "2", "3", "4" }, await officialRows.Locator(".rank").AllTextContentsAsync());
        Assert.Contains("official ranks stay unchanged", await page.Locator("#rankingExplanation").InnerTextAsync());

        await page.Locator("label[for='view-bortz']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#view-bortz')?.checked === true");
        await page.WaitForURLAsync("**/league/bortz*");
        await page.WaitForFunctionAsync("() => document.querySelector('.open-data-leaderboard-row')?.hidden === true");
        Assert.False(await publicDataRow.IsVisibleAsync());
        Assert.False(await publicDataSection.IsVisibleAsync());

        await page.Locator("label[for='view-ultimate']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#view-ultimate')?.checked === true");
        await page.WaitForURLAsync("**/leaderboard*");
        await publicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.True(await publicDataSection.IsVisibleAsync());

        var divisionFilter = page.Locator("input[name='division'][value='Open']");
        await divisionFilter.EvaluateAsync(
            "input => { input.checked = true; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await page.WaitForURLAsync("**/league/open*");
        await page.WaitForFunctionAsync("() => document.querySelector('.open-data-leaderboard-row')?.hidden === true");
        Assert.False(await publicDataRow.IsVisibleAsync());
        Assert.False(await publicDataSection.IsVisibleAsync());
        await divisionFilter.EvaluateAsync(
            "input => { input.checked = false; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await page.WaitForURLAsync("**/leaderboard*");
        await publicDataRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.True(await publicDataSection.IsVisibleAsync());

        await page.EvaluateAsync("() => localStorage.setItem('gmaSkipAll', 'true')");
        await page.Locator(".leaderboard table tbody tr[data-athlete-name='Official Browser Athlete']").ClickAsync();
        await page.Locator("#detailsModal #personalLink").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        Assert.Equal(
            "/athlete/official-browser-athlete",
            await page.EvaluateAsync<string>("() => window.location.pathname"));
        Assert.Null(await page.Locator("#detailsModal").GetAttributeAsync("aria-describedby"));
        Assert.Equal("Close athlete details", await page.Locator("#closeAthleteDetailsModal").GetAttributeAsync("aria-label"));
        Assert.DoesNotContain(
            "open-data-profile",
            await page.Locator("#detailsModal .modal-content").GetAttributeAsync("class") ?? string.Empty);
        Assert.False(await page.Locator("#publicDataSources").IsVisibleAsync());
        Assert.False(await page.Locator("#openDataModalPhotoCredit").IsVisibleAsync());
        Assert.False(await page.Locator("#openDataQualifierNotice").IsVisibleAsync());
        Assert.True(await page.Locator("#detailsModal .official-profile-only").First.IsVisibleAsync());

        await page.Keyboard.PressAsync("Escape");
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".podium-item[data-athlete-name='Official Older Athlete']").WaitForAsync();
        Assert.Equal(0, await page.Locator(".open-data-leaderboard-row").CountAsync());
        Assert.Equal(0, await page.Locator("#openDataProfilesGrid .open-data-card").CountAsync());

        Assert.Empty(errors);
    }

    private static string LeaderboardProfilesWithTiedReferencesJson()
    {
        var profiles = JsonNode.Parse(LeaderboardProfilesJson)?.AsArray()
            ?? throw new InvalidOperationException("Could not parse the browser-test leaderboard profiles.");
        var original = profiles
            .Single(profile => profile?["ProfileType"]?.GetValue<string>() == "OpenData")
            ?? throw new InvalidOperationException("Could not find the browser-test OpenData profile.");
        var tiedReference = original.DeepClone().AsObject();
        tiedReference["AthleteSlug"] = "second_public_browser_subject";
        tiedReference["Name"] = "Second Public Browser Subject";
        tiedReference["DisplayName"] = "Second Public Browser Subject";

        var openData = tiedReference["OpenData"]?.AsObject()
            ?? throw new InvalidOperationException("Could not find the browser-test OpenData provenance.");
        openData["Aliases"] = new JsonArray("Independent Tie ZXQ");
        openData["Notability"]!["Summary"] =
            "A second globally recognized public figure used to verify independent comparisons.";
        openData["Portrait"]!["AssetUrl"] =
            "/public-data/second-public-browser-subject/portrait?v=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        profiles.Add(tiedReference);

        var belowFieldReference = original.DeepClone().AsObject();
        belowFieldReference["AthleteSlug"] = "below_field_public_browser_subject";
        belowFieldReference["Name"] = "Below Field Public Browser Subject";
        belowFieldReference["DisplayName"] = "Below Field Public Browser Subject";
        var belowFieldBiomarker = belowFieldReference["Biomarkers"]?.AsArray()[0]?.AsObject()
            ?? throw new InvalidOperationException("Could not find the browser-test OpenData biomarker record.");
        belowFieldBiomarker["AgeYears"] = 20.0;
        belowFieldBiomarker["RdwPc"] = 14.0;
        var belowFieldOpenData = belowFieldReference["OpenData"]?.AsObject()
            ?? throw new InvalidOperationException("Could not find the browser-test OpenData provenance.");
        belowFieldOpenData["Aliases"] = new JsonArray("Below Every Official ZXQ");
        belowFieldOpenData["Notability"]!["Summary"] =
            "A third globally recognized public figure used to verify a below-field comparison.";
        belowFieldOpenData["Portrait"]!["AssetUrl"] =
            "/public-data/below-field-public-browser-subject/portrait?v=cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        profiles.Add(belowFieldReference);
        return profiles.ToJsonString();
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
            "ProfileType": "Athlete",
            "AthleteSlug": "official_older_athlete",
            "Name": "Official Older Athlete",
            "DisplayName": "Official Older Athlete",
            "DateOfBirth": { "Year": 1940, "Month": 2, "Day": 3 },
            "Division": "Open",
            "Flag": "Hungary",
            "Why": "A better official result above the public-data comparison.",
            "MediaContact": "",
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
            "ProfileType": "Athlete",
            "AthleteSlug": "official_younger_athlete",
            "Name": "Official Younger Athlete",
            "DisplayName": "Official Younger Athlete",
            "DateOfBirth": { "Year": 2000, "Month": 2, "Day": 3 },
            "Division": "Open",
            "Flag": "Hungary",
            "Why": "A worse official result below the public-data comparison.",
            "MediaContact": "",
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
            "ProfileType": "Athlete",
            "AthleteSlug": "official_tied_athlete",
            "Name": "Official Tied Athlete",
            "DisplayName": "Official Tied Athlete",
            "DateOfBirth": { "Year": 1978, "Month": 5, "Day": 22 },
            "Division": "Open",
            "Flag": "Hungary",
            "Why": "An official exact-score tie that must remain ahead of public data.",
            "MediaContact": "",
            "ProfilePic": "/assets/content-images/headshot.jpg",
            "Biomarkers": [
              {
                "Date": "2026-02-01",
                "Wbc1000cellsuL": 4.8,
                "LymPc": 31.0,
                "McvFL": 89.0,
                "RdwPc": 12.2,
                "AlbGL": 46.0,
                "AlpUL": 62.0,
                "CreatUmolL": 77.0,
                "GluMmolL": 4.9,
                "CrpMgL": 0.6
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
                "DateBasis": "Report",
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
              "Portrait": {
                "SourcePageUrl": "https://example.com/photo-source",
                "OriginalUrl": "https://example.com/photo-original.webp",
                "Author": "Browser Photographer",
                "LicenseName": "CC BY 4.0",
                "LicenseUrl": "https://creativecommons.org/licenses/by/4.0",
                "EditNote": "Cropped, resized and converted to WebP for display.",
                "AssetUrl": "/public-data/public-browser-subject/portrait?v=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
              },
              "Aliases": ["Published Browser Alias"],
              "IdentitySourceIds": ["bloodwork-1"],
              "Sources": [
                {
                  "Id": "bloodwork-1",
                  "Kind": "Bloodwork",
                  "Title": "Published lab panel",
                  "Url": "https://example.com/public-labs",
                  "AccessedOn": "2026-07-10",
                  "SubjectAuthorization": {
                    "Kind": "ExplicitlyAuthorized",
                    "EvidenceUrl": "https://example.com/authorization",
                    "EvidenceNote": "The subject explicitly accepts publication in the source video."
                  }
                },
                {
                  "Id": "official-biography",
                  "Kind": "Identity",
                  "Title": "Official biography",
                  "Url": "https://example.com/biography",
                  "AccessedOn": "2026-07-10"
                }
              ],
              "Notability": {
                "Summary": "A globally recognized public figure with an established body of work.",
                "SourceIds": ["official-biography"]
              },
              "TranscriptionNotes": [
                "Values were normalized from the published units."
              ]
            }
          }
        ]
        """;

    private static string GetCommittedPortraitPath([CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Could not locate the tests directory.");
        return Path.GetFullPath(Path.Combine(
            testsDirectory,
            "..",
            "LongevityWorldCup.Website",
            "wwwroot",
            "public-data-profiles",
            "andrew-tate",
            "portrait.webp"));
    }
}
