using Microsoft.Playwright;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardOrderingBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Ordered = "/leaderboard?sort=pheno&then=age&thenDir=desc";
    private static ILocator Rows(IPage page) => page.Locator(".leaderboard tbody tr[data-athlete-name]:visible");

    [Fact]
    public async Task EveryCatalogValue_MatchesThePublicServerCalculation()
    {
        using var client = App.CreateClient();
        var source = JsonNode.Parse(await client.GetStringAsync("/api/data/athletes"))!.AsArray();
        var snapshot = new PublicLeaderboardSnapshot(source, DateTime.UtcNow.Date);
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("""
            window.observedOrderValues = {};
            let orderingApi;
            Object.defineProperty(window, 'LwcLeaderboardOrder', {
                get() { return orderingApi; },
                set(api) {
                    orderingApi = api;
                    const extract = api.values;
                    api.values = (source, athlete) => {
                        const values = extract(source, athlete);
                        window.observedOrderValues[source.AthleteSlug] = values;
                        return values;
                    };
                }
            });
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Loaded(page);
        var actual = JsonNode.Parse(await page.EvaluateAsync<string>("JSON.stringify(window.observedOrderValues)"))!.AsObject();
        foreach (var athlete in snapshot.Athletes)
        foreach (var (key, value) in athlete.Row.OrderValues)
        {
            var item = actual[athlete.Row.Slug]?[key];
            var raw = item is JsonObject ? item["value"] : item;
            if (value.IsMissing) Assert.True(raw is null || raw is JsonValue text && text.TryGetValue<string>(out var missingText) && string.IsNullOrWhiteSpace(missingText), $"{athlete.Row.Slug}/{key}: expected missing, got {raw}");
            else if (value.Number.HasValue)
                Assert.True(raw is JsonValue && Math.Abs(raw.GetValue<double>() - value.Number.Value) < 0.0000001, $"{athlete.Row.Slug}/{key}: expected {value.Number}, got {raw}");
            else Assert.Equal(value.Text, raw?.GetValue<string>());
            if (value.TestDate is not null) Assert.Equal(value.TestDate, item?["date"]?.GetValue<string>());
        }
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const values = window.LwcLeaderboardOrder.values(
                    { DateOfBirth: { Year: 1980, Month: 1, Day: 1 } },
                    { crowdCount: 10, crowdAge: null, lowestBortzAge: null, chronoAtLowestBortzAge: 40 });
                return values.crowd === null && values.crowdReduction === null && values.pace === null;
            }
            """));
    }

    [Fact]
    public async Task SingleFieldOrder_HasOneValueColumnInServerHtmlAndAfterHydration()
    {
        foreach (var javaScriptEnabled in new[] { false, true })
        {
            await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = javaScriptEnabled });
            var page = await context.NewPageAsync();
            await page.GotoAsync("/leaderboard?sort=pheno");
            if (javaScriptEnabled) await Loaded(page);
            await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(1);
            await Assertions.Expect(Rows(page).First.Locator(".leaderboard-order-value")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("th[data-order-key=age], td[data-order-key=age]")).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task ServerAndHydration_AgreeOnOrderRanksMetricsAndTwoValueColumns()
    {
        await using var serverContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var server = await serverContext.NewPageAsync();
        await server.GotoAsync(Ordered);
        var expected = await Snapshot(server);
        Assert.True(expected.Length > 10);
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Ordered);
        await Loaded(page);
        Assert.Equal(expected, await Snapshot(page));
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("th[aria-sort]")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("th[data-order-key=pheno]")).ToHaveAttributeAsync("aria-sort", "ascending");
        var structuredNames = await page.EvaluateAsync<string[]>("JSON.parse(document.querySelector('#pageStructuredData').textContent)['@graph'].find(x=>x['@type']==='ItemList').itemListElement.map(x=>x.item.name)");
        var visibleNames = await Rows(page).Locator(".athlete-name").AllTextContentsAsync();
        Assert.Equal(visibleNames, structuredNames);
    }

    [Fact]
    public async Task ComposerAndHeaders_KeepIndependentLevelsAndRestoreHistoryAndFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Ordered + "#rank-1");
        await Loaded(page);
        var original = await Snapshot(page);
        var thenHeader = page.Locator("th[data-order-key=age] button");
        await thenHeader.ClickAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "asc");
        await Assertions.Expect(page.Locator("th[data-order-key=pheno]")).ToHaveAttributeAsync("aria-sort", "ascending");
        await Assertions.Expect(thenHeader).ToBeFocusedAsync();
        Assert.EndsWith("#rank-1", page.Url);
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "desc");
        Assert.Equal(original, await Snapshot(page));
        await page.GoForwardAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "asc");
        var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Order athletes", Exact = true });
        Assert.Equal("", (await toggle.InnerTextAsync()).Trim());
        Assert.True(await toggle.EvaluateAsync<bool>("e=>e.parentElement.previousElementSibling.classList.contains('search-container') && e.parentElement.nextElementSibling.classList.contains('sidebar-toggle')"));
        await toggle.ClickAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderPrimary")).ToBeFocusedAsync();
        await page.Locator("#leaderboardOrderSecondary").SelectOptionAsync("");
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).First.Locator(".leaderboard-order-value")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("th[data-order-key=age], td[data-order-key=age]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#leaderboardThenDirection")).ToBeDisabledAsync();
        Assert.DoesNotContain("then=", page.Url);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToBeHiddenAsync();
        await page.Locator("th[data-order-key=rank] button").ClickAsync();
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("td.leaderboard-order-value")).ToHaveCountAsync(0);
        Assert.DoesNotContain("sort=", page.Url);
        await Assertions.Expect(Rows(page).First.Locator(".rank")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task NativeSelectTransientFocusLoss_DoesNotDismissTheComposer()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?sort=vitd&dir=desc");
        await Loaded(page);
        var toggle = page.Locator("#leaderboardOrderToggle");
        var panel = page.Locator("#leaderboardOrderPanel");
        var secondary = page.Locator("#leaderboardOrderSecondary");
        await toggle.ClickAsync();
        await secondary.FocusAsync();
        // Windows' native picker can briefly move focus to body without a related target.
        await secondary.EvaluateAsync("element => element.blur()");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await secondary.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await toggle.ClickAsync();
        await page.Locator("#leaderboardTitle").ClickAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, true)]
    public async Task PointerAndTouch_CanChooseThenByAndPromoteItToPrimary(int width, bool touch)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, HasTouch = touch, IsMobile = touch
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?sort=vitd&dir=desc");
        await Loaded(page);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        var panel = page.Locator("#leaderboardOrderPanel");
        var secondary = page.Locator("#leaderboardOrderSecondary");
        if (touch) await secondary.TapAsync(); else await secondary.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(secondary).ToHaveValueAsync("age");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(2);
        await page.Locator("#leaderboardThenDirection").ClickAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "desc");
        // Selecting a current tie-breaker as the main field should not require clearing it first.
        await page.Locator("#leaderboardOrderPrimary").SelectOptionAsync("age");
        await Assertions.Expect(secondary).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("aria-sort", "ascending");
    }

    [Fact]
    public async Task SearchCohortAndByFlag_PreserveOrderingAndSelectedLeagueRanks()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?sort=age&dir=desc&then=albumin");
        await Loaded(page);
        var row = Rows(page).Nth(4);
        var name = (await row.GetAttributeAsync("data-athlete-name"))!;
        var rank = await row.Locator(".rank").InnerTextAsync();
        await page.Locator("#athleteSearch").FillAsync(name);
        var result = Rows(page).Filter(new() { HasText = name }).First;
        await Assertions.Expect(result.Locator(".rank")).ToHaveTextAsync(rank);
        Assert.Contains("sort=age", page.Url);
        await page.ReloadAsync();
        await Loaded(page);
        await Assertions.Expect(result.Locator(".rank")).ToHaveTextAsync(rank);
        await page.Locator("#athleteSearch").FillAsync("");
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator("#group-flags")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeHiddenAsync();
        Assert.Contains("sort=age", page.Url);
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator("#group-flags")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("aria-sort", "descending");
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(2);
        await page.Locator("#athleteSearch").FillAsync("no-athlete-matches-9081726354");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".leaderboard .no-results td")).ToHaveAttributeAsync("colspan", "7");
    }

    [Theory]
    [InlineData(1440, 1000, false)]
    [InlineData(1024, 900, false)]
    [InlineData(768, 1000, false)]
    [InlineData(844, 390, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 720, false)]
    public async Task ResponsiveComposerAndValues_FitAndHaveKeyboardAccess(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Ordered);
        await Loaded(page);
        var toggle = page.Locator("#leaderboardOrderToggle");
        await toggle.ClickAsync();
        var panel = page.Locator("#leaderboardOrderPanel");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        var bounds = (await panel.BoundingBoxAsync())!;
        Assert.InRange(bounds.X, 0, width);
        Assert.InRange(bounds.X + bounds.Width, 0, width + 1);
        Assert.InRange(bounds.Y, 0, height);
        Assert.InRange(bounds.Y + bounds.Height, 0, height + 1);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth>innerWidth"));
        await Assertions.Expect(Rows(page).First.Locator(".leaderboard-order-value")).ToHaveCountAsync(2);
        var screenshots = Environment.GetEnvironmentVariable("LWC_ORDER_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(screenshots))
        {
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"ordering-{width}-composer.png") });
        }
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        await page.Locator("#leaderboardTitle").ClickAsync();
        if (!string.IsNullOrWhiteSpace(screenshots)) await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"ordering-{width}-table.png") });
        await toggle.ClickAsync();
        await page.Locator("#leaderboardOrderSecondary").SelectOptionAsync("");
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).First.Locator(".leaderboard-order-value")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#leaderboardTitle").ClickAsync();
        if (!string.IsNullOrWhiteSpace(screenshots)) await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"ordering-{width}-single.png") });
        await page.EvaluateAsync("document.documentElement.style.fontSize='200%'");
        await toggle.ClickAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth>innerWidth"));
        bounds = (await panel.BoundingBoxAsync())!;
        Assert.InRange(bounds.X, 0, width);
        Assert.InRange(bounds.X + bounds.Width, 0, width + 1);
        Assert.InRange(bounds.Y, 0, height);
        Assert.InRange(bounds.Y + bounds.Height, 0, height + 1);
    }

    [Fact]
    public async Task HomepageCustomOrder_IncludesPodiumAthletesBeforeLimitingAndViewAllKeepsState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/?sort=name&then=age");
        await Loaded(page);
        await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(10);
        var url = await page.Locator("#viewAllAthletesBtn").GetAttributeAsync("href");
        Assert.Contains("sort=name", url);
        Assert.Contains("then=age", url);
        await page.Locator("th[data-order-key=rank] button").ClickAsync();
        await Assertions.Expect(page.Locator(".podium")).ToBeVisibleAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(7);
    }

    private static Task Loaded(IPage page) => Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.", new() { Timeout = 60000 });
    private static Task<string[]> Snapshot(IPage page) => Rows(page).EvaluateAllAsync<string[]>("rows=>rows.map(row=>[row.id,row.dataset.athleteName,row.querySelector('.rank').textContent.trim(),row.querySelector('.age-reduction').textContent.trim(),...Array.from(row.querySelectorAll('.leaderboard-order-value'),cell=>cell.textContent.trim())].join('|'))");
}
