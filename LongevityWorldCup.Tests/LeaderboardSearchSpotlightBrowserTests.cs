using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardSearchSpotlightBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Rows = ".leaderboard tbody tr[data-athlete-name]";

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 568, false)]
    [InlineData(768, 900, true)]
    [InlineData(844, 390, false)]
    [InlineData(195, 422, true)]
    public async Task Reason_DisclosesByKeyboardAndTouchWithoutOpeningProfile_AndFits(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, HasTouch = true,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context, "mar");
        var reason = page.Locator(Rows + "[data-athlete-name='Keith Blondin'] .search-reason--why");
        await Assertions.Expect(reason).ToHaveAttributeAsync("type", "button");
        await reason.ScrollIntoViewIfNeededAsync();
        await reason.FocusAsync();
        await reason.PressAsync("Enter");
        var panel = page.Locator(".search-reason-popover");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(reason).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(panel.Locator(".highlight")).ToContainTextAsync("mar");
        await AssertFitsAsync(page, panel);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await Assertions.Expect(reason).ToBeFocusedAsync();
        await reason.TapAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await AssertFitsAsync(page, panel);
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator(".search-reason, .is-search-hit, .is-search-value")).ToHaveCountAsync(0);
        await Assertions.Expect(panel).ToBeHiddenAsync();
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Fact]
    public async Task Ordering_PreservesReasonsAndRankMarks_AndFlagTeamsExplainMemberMatches()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenAsync(context, "mar");
        await page.EvaluateAsync("window.savedReason = document.querySelector('[data-athlete-name=\"Keith Blondin\"] .search-reason--why')");
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.Locator("#leaderboardOrderPrimary").SelectOptionAsync("age");
        Assert.True(await page.EvaluateAsync<bool>("savedReason.isConnected"));
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#athleteSearch").FillAsync("47");
        await Assertions.Expect(page.Locator(Rows + ":visible .rank.is-search-value").First).ToBeVisibleAsync();
        await page.GotoAsync("/leaderboard?group=flags&search=mar");
        await LoadedAsync(page);
        await Assertions.Expect(page.Locator(".flag-team[data-flag='Biomarker Optimizers'] .flag-team-name .highlight")).ToHaveTextAsync("mar");
        await Assertions.Expect(page.Locator("#flagLeaderboard .search-reason").First).ToBeVisibleAsync();
        await page.Locator("#flagLeaderboard .search-reason").First.ClickAsync();
        await Assertions.Expect(page.Locator(".search-reason-popover")).ToBeVisibleAsync();
        await AssertFitsAsync(page, page.Locator(".search-reason-popover"));
    }

    [Fact]
    public async Task Badges_HighlightTheirOwnShape_AndHiddenBadgesRemainExplainableOnPhone()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var michael = athletes.First(a => a!["Name"]!.GetValue<string>() == "Michael Lustgarten")!;
            michael["Badges"] = System.Text.Json.Nodes.JsonNode.Parse("""
                [{"BadgeLabel":"Best domain – liver","Place":1}]
                """);
        });
        var page = await OpenAsync(context, "liver");
        var badge = page.Locator(Rows + ":visible .badge-family-domain.is-search-hit").First;
        await Assertions.Expect(badge).ToBeVisibleAsync();
        Assert.Equal("0px", await badge.EvaluateAsync<string>("e => getComputedStyle(e).borderRadius"));
        Assert.Equal("3px", await badge.EvaluateAsync<string>("e => getComputedStyle(e, '::before').top"));
        await page.SetViewportSizeAsync(390, 844);
        var reason = page.Locator(Rows + ":visible .search-reason--badge").First;
        await Assertions.Expect(reason).ToBeVisibleAsync();
        await reason.ClickAsync();
        await Assertions.Expect(page.Locator(".search-reason-popover")).ToContainTextAsync("Liver");
        await AssertFitsAsync(page, page.Locator(".search-reason-popover"));
    }

    [Fact]
    public async Task NumericAndCanonicalNameMatches_HaveEvidenceEvenWhenDisplayedDifferently()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync("""
            let ordering;
            Object.defineProperty(window, 'LwcLeaderboardOrder', {
                get() { return ordering; },
                set(api) {
                    ordering = api;
                    const render = api.renderValues;
                    api.renderValues = (entries, ...args) => { window.searchEntries = entries; return render(entries, ...args); };
                }
            });
            """);
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var athlete = athletes.First(a => a!["Name"]!.GetValue<string>() == "Keith Blondin")!;
            athlete["DisplayName"] = "Keith B.";
        });
        var page = await OpenAsync(context, "blondin");
        await Assertions.Expect(page.Locator(Rows + "[data-athlete-name='Keith Blondin'] .search-reason--name")).ToBeVisibleAsync();
        var metric = await page.EvaluateAsync<string>("() => searchEntries.find(e => e.athlete.name === 'Keith Blondin').athlete.ageReductionPercent.toFixed(1)");
        await page.GotoAsync("/leaderboard?view=pheno&search=" + Uri.EscapeDataString(metric));
        await LoadedAsync(page);
        var reason = page.Locator(Rows + "[data-athlete-name='Keith Blondin'] .search-reason--metric");
        await Assertions.Expect(reason).ToBeVisibleAsync();
        await reason.ClickAsync();
        await Assertions.Expect(page.Locator(".search-reason-popover")).ToContainTextAsync(metric);
    }

    [Fact]
    public async Task NativeZoomAndEnlargedText_KeepShortReasonsWhole()
    {
        var temporary = Directory.CreateTempSubdirectory("lwc-search-zoom-");
        try
        {
            var extension = Path.Combine(temporary.FullName, "extension");
            Directory.CreateDirectory(extension);
            await File.WriteAllTextAsync(Path.Combine(extension, "manifest.json"), """
                {"manifest_version":3,"name":"Local search zoom verification","version":"1.0","permissions":["tabs"],"background":{"service_worker":"background.js"}}
                """);
            await File.WriteAllTextAsync(Path.Combine(extension, "background.js"), "chrome.runtime.onInstalled.addListener(() => {});");
            await File.WriteAllTextAsync(Path.Combine(extension, "control.html"), "<!doctype html><title>Local zoom control</title>");
            using var playwright = await Playwright.CreateAsync();
            await using var context = await playwright.Chromium.LaunchPersistentContextAsync(Path.Combine(temporary.FullName, "profile"), new()
            {
                Channel = "chromium", Headless = true, BaseURL = App.BaseAddress.ToString(),
                ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce,
                IgnoreDefaultArgs = ["--hide-scrollbars"],
                Args = [$"--disable-extensions-except={extension}", $"--load-extension={extension}", "--disable-features=OverlayScrollbar,FluentOverlayScrollbar"]
            });
            await BrowserTestApp.RouteExternalResourcesAsync(context);
            var page = await OpenAsync(context, "mar");
            var cdp = await context.NewCDPSessionAsync(page);
            var targets = await cdp.SendAsync("Target.getTargets");
            var extensionId = System.Text.RegularExpressions.Regex.Match(targets?.ToString() ?? "", @"chrome-extension://([a-p]+)/background.js").Groups[1].Value;
            Assert.NotEmpty(extensionId);
            var control = await context.NewPageAsync();
            await control.GotoAsync($"chrome-extension://{extensionId}/control.html");
            foreach (var width in new[] { 1440, 390 })
            foreach (var dark in new[] { false, true })
            {
                await page.SetViewportSizeAsync(width, width == 1440 ? 900 : 844);
                await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
                Assert.Equal(2, await control.EvaluateAsync<double>("""
                    async url => {
                        const tab = (await chrome.tabs.query({})).find(t => t.url === url);
                        await chrome.tabs.setZoomSettings(tab.id, {mode:'automatic', scope:'per-tab'});
                        await chrome.tabs.setZoom(tab.id, 2);
                        return await chrome.tabs.getZoom(tab.id);
                    }
                    """, page.Url));
                await page.WaitForFunctionAsync("w => Math.abs(innerWidth - w / 2) <= 1 && devicePixelRatio === 2", width);
                foreach (var enlarged in new[] { false, true })
                {
                    await page.EvaluateAsync("large => document.documentElement.style.fontSize = large ? '200%' : ''", enlarged);
                    var reason = page.Locator(Rows + "[data-athlete-name='Sergey Vlasov'] .search-reason--flag");
                    await reason.ClickAsync();
                    await AssertFitsAsync(page, page.Locator(".search-reason-popover"));
                    Assert.True(await reason.EvaluateAsync<bool>("e => getComputedStyle(e).animationName === 'none'"));
                    var dir = Environment.GetEnvironmentVariable("LWC_SEARCH_CAPTURE_DIR");
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var shot = await cdp.SendAsync("Page.captureScreenshot", new() { ["format"] = "png", ["captureBeyondViewport"] = false });
                        await File.WriteAllBytesAsync(Path.Combine(dir, $"zoom-200-{width}-{dark}-{enlarged}.png"), Convert.FromBase64String(shot!.Value.GetProperty("data").GetString()!));
                    }
                    await page.Keyboard.PressAsync("Escape");
                    await Assertions.Expect(reason).ToBeFocusedAsync();
                }
                await page.EvaluateAsync("document.documentElement.style.fontSize = ''");
                await control.EvaluateAsync("async url => { const tab=(await chrome.tabs.query({})).find(t=>t.url===url); await chrome.tabs.setZoom(tab.id,1); }", page.Url);
            }
        }
        finally { temporary.Delete(true); }
    }

    [Fact]
    public async Task LongReason_RemainsKeyboardScrollable_AndUnicodeSnippetIncludesTheMatch()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 320, Height = 480 }, ReducedMotion = ReducedMotion.Reduce });
        var terms = Enumerable.Range(0, 12).Select(i => $"marker{i:00}").ToArray();
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var keith = athletes.First(a => a!["Name"]!.GetValue<string>() == "Keith Blondin")!;
            keith["Why"] = string.Join(" ", terms.Select(t => "🧭 Oﬃce " + new string('x', 100) + " " + t + " " + new string('y', 100)));
        });
        var page = await OpenAsync(context, string.Join(' ', terms));
        var reason = page.Locator(Rows + "[data-athlete-name='Keith Blondin'] .search-reason--why");
        await reason.ClickAsync();
        var panel = page.Locator(".search-reason-popover");
        foreach (var term in terms) await Assertions.Expect(panel).ToContainTextAsync(term);
        Assert.True(await panel.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight && e.scrollWidth <= e.clientWidth"));
        await reason.PressAsync("Tab");
        await Assertions.Expect(panel).ToBeFocusedAsync();
        await panel.PressAsync("Control+End");
        await page.WaitForFunctionAsync("() => { const e=document.querySelector('.search-reason-popover'); return e.scrollTop + e.clientHeight >= e.scrollHeight - 1; }");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(reason).ToBeFocusedAsync();
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string query)
    {
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard?search=" + Uri.EscapeDataString(query));
        await LoadedAsync(page);
        return page;
    }

    private static Task LoadedAsync(IPage page) => Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");

    private static async Task AssertFitsAsync(IPage page, ILocator panel)
    {
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForFiniteAnimationsAsync(page);
        await Assertions.Expect(panel).ToBeVisibleAsync();
        Assert.True(await panel.EvaluateAsync<bool>("""
            e => {
                const r = e.getBoundingClientRect();
                return r.width > 0 && r.height > 0 && e.scrollWidth <= e.clientWidth && e.scrollHeight <= e.clientHeight
                    && r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight;
            }
            """), "Short matching snippets must fit without clipping or internal scrollbars.");
    }
}
