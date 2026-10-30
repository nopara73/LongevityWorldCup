using Microsoft.Playwright;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardOrderingBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Ordered = "/leaderboard?sort=pheno&then=age&thenDir=desc";
    private static ILocator Rows(IPage page) => page.Locator(".leaderboard tbody tr[data-athlete-name]:visible");

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task EveryPublicBiomarker_IsSelectableAtEitherLevelAndSortsItsActualValues(int width)
    {
        using var client = App.CreateClient();
        var source = JsonNode.Parse(await client.GetStringAsync("/api/data/athletes"))!.AsArray();
        var snapshot = new PublicLeaderboardSnapshot(source, DateTime.UtcNow.Date);
        var fields = LeaderboardOrdering.Fields.Where(field => field.SourceKey is not null).ToArray();
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?sort=age");
        await Loaded(page);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        var primary = page.Locator("#leaderboardOrderPrimary");
        var secondary = page.Locator("#leaderboardOrderSecondary");
        var markerKeys = fields.Select(field => field.Key).ToArray();
        foreach (var select in new[] { primary, secondary })
            Assert.Equal(markerKeys, await select.Locator("optgroup[label=Biomarkers] option").EvaluateAllAsync<string[]>("options => options.map(option => option.value)"));

        foreach (var field in fields)
        {
            await primary.SelectOptionAsync(field.Key);
            foreach (var direction in new[] { "asc", "desc" })
            {
                if (direction == "desc") await page.Locator("#leaderboardOrderDirection").ClickAsync();
                var expected = snapshot.Select("/leaderboard", new QueryString($"?sort={field.Key}&dir={direction}").ToQuery()).Snapshot.Rows;
                Assert.Equal(expected.Select(row => row.AthleteName), await Rows(page).EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.athleteName)"));
                var row = Rows(page).First;
                var cell = row.Locator($"td[data-order-key='{field.Key}']");
                await Assertions.Expect(cell).ToHaveTextAsync(LeaderboardOrdering.Format(expected[0].OrderValues[field.Key], field));
                await Assertions.Expect(page.Locator($"th[data-order-key='{field.Key}'] small")).ToHaveTextAsync(field.Unit);
            }
            await primary.SelectOptionAsync("age");
            await secondary.SelectOptionAsync(field.Key);
            await Assertions.Expect(page.Locator($"th[data-order-key='{field.Key}']")).ToHaveAttributeAsync("data-order-level", "2");
            await Assertions.Expect(Rows(page).First.Locator($"td[data-order-key='{field.Key}']")).ToHaveCountAsync(1);
            await page.Locator("#leaderboardOrderSecondaryClear").ClickAsync();
        }
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

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
        await NavigateAsync(page, "/leaderboard");
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
    public async Task SameMetricInYearsAndPercent_HasDistinctAccessibleNamesBeforeAndAfterHydration()
    {
        foreach (var javaScriptEnabled in new[] { false, true })
        {
            await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = javaScriptEnabled });
            var page = await context.NewPageAsync();
            await NavigateAsync(page, "/leaderboard?sort=phenoReduction&then=phenoPercent");
            if (javaScriptEnabled) await Loaded(page);
            await Assertions.Expect(page.Locator("th[data-order-key=phenoReduction] button")).ToHaveAccessibleNameAsync("Pheno age reduction (years), order by, ascending; reverse order");
            await Assertions.Expect(page.Locator("th[data-order-key=phenoPercent] button")).ToHaveAccessibleNameAsync("Pheno age reduction (%), then by, descending; reverse order");
            if (!javaScriptEnabled) continue;
            await page.Locator("#leaderboardOrderToggle").ClickAsync();
            await Assertions.Expect(page.Locator("#leaderboardOrderDirection")).ToHaveAttributeAsync("aria-label", "Pheno age reduction (years): lowest first; switch to highest first");
            await page.Locator("#leaderboardThenDirection").ClickAsync();
            await Assertions.Expect(page.Locator("#leaderboardOrderStatus")).ToHaveTextAsync("Ordered by Pheno age reduction (years), ascending, then by Pheno age reduction (%), ascending.");
        }
    }

    [Fact]
    public async Task SingleFieldOrder_HasOneValueColumnInServerHtmlAndAfterHydration()
    {
        foreach (var javaScriptEnabled in new[] { false, true })
        {
            await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = javaScriptEnabled });
            var page = await context.NewPageAsync();
            await NavigateAsync(page, "/leaderboard?sort=pheno");
            if (javaScriptEnabled) await Loaded(page);
            await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(1);
            await Assertions.Expect(Rows(page).First.Locator(".leaderboard-order-value")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("th[data-order-key=age], td[data-order-key=age]")).ToHaveCountAsync(0);
        }
    }

    [Theory]
    [InlineData(Ordered)]
    [InlineData("/leaderboard?sort=wbc&then=neutrophils")]
    [InlineData("/leaderboard?sort=cholesterol&dir=desc&then=cystatinC")]
    public async Task ServerAndHydration_AgreeOnOrderRanksMetricsAndTwoValueColumns(string url)
    {
        await using var serverContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var server = await serverContext.NewPageAsync();
        await NavigateAsync(server, url);
        var expected = await Snapshot(server);
        Assert.True(expected.Length > 10);
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, url);
        await Loaded(page);
        Assert.Equal(expected, await Snapshot(page));
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("th[aria-sort]")).ToHaveCountAsync(1);
        var ordering = LeaderboardOrderState.Parse(new QueryString(new Uri("https://localhost" + url).Query).ToQuery());
        await Assertions.Expect(page.Locator($"th[data-order-key='{ordering.Sort}']")).ToHaveAttributeAsync("aria-sort", ordering.Direction == "asc" ? "ascending" : "descending");
        var structuredNames = await page.EvaluateAsync<string[]>("JSON.parse(document.querySelector('#pageStructuredData').textContent)['@graph'].find(x=>x['@type']==='ItemList').itemListElement.map(x=>x.item.name)");
        var visibleNames = await Rows(page).Locator(".athlete-name").AllTextContentsAsync();
        Assert.Equal(visibleNames, structuredNames);
    }

    [Fact]
    public async Task ComposerAndHeaders_KeepIndependentLevelsAndRestoreHistoryAndFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, Ordered + "#rank-1");
        await Loaded(page);
        var original = await Snapshot(page);
        var thenHeader = page.Locator("th[data-order-key=age] button");
        await thenHeader.ClickAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "asc");
        await Assertions.Expect(page.Locator("th[data-order-key=pheno]")).ToHaveAttributeAsync("aria-sort", "ascending");
        await Assertions.Expect(thenHeader).ToBeFocusedAsync();
        Assert.EndsWith("#rank-1", page.Url);
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "desc");
        Assert.Equal(original, await Snapshot(page));
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("th[data-order-key=age]")).ToHaveAttributeAsync("data-order-direction", "asc");
        var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Order athletes", Exact = true });
        Assert.Equal("", (await toggle.InnerTextAsync()).Trim());
        Assert.True(await toggle.EvaluateAsync<bool>("""
            e => {
                const actions = e.closest('.leaderboard-actions');
                const grouping = actions.previousElementSibling;
                return grouping.classList.contains('leaderboard-group-switcher')
                    && grouping.previousElementSibling.classList.contains('search-container')
                    && e.parentElement.nextElementSibling.classList.contains('sidebar-toggle');
            }
            """), "The joined bar must keep Search, By Flag, Order and Filters in DOM order.");
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
        await NavigateAsync(page, "/leaderboard?sort=vitd&dir=desc");
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
    public async Task OutsideDismissal_DoesNotActivateTheAthleteOrHeaderUnderneath(int width, bool touch)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, HasTouch = touch, IsMobile = touch,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, Ordered);
        await Loaded(page);
        var original = await Snapshot(page);
        var toggle = page.Locator("#leaderboardOrderToggle");
        var panel = page.Locator("#leaderboardOrderPanel");
        await toggle.ClickAsync();
        var athlete = Rows(page).Nth(3).Locator(".athlete-name");
        if (touch) await athlete.TapAsync();
        else await athlete.ClickAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        Assert.Equal(original, await Snapshot(page));
        Assert.DoesNotContain("/athlete/", page.Url);

        // On phones the anchored panel covers this header, so test the header's
        // outside activation only where it is physically exposed.
        if (!touch)
        {
            await toggle.ClickAsync();
            await page.Locator("th[data-order-key=rank] button").ClickAsync();
            await Assertions.Expect(panel).ToBeHiddenAsync();
            Assert.Contains("sort=pheno", page.Url);
            Assert.Equal(original, await Snapshot(page));
        }

        // Once dismissed, the next deliberate action works normally.
        await athlete.ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task OrderingChanges_PreserveResultContentAndSidebarWithoutReplayingRowReveals(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, Ordered);
        await Loaded(page);
        await page.EvaluateAsync("async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));}");
        await page.EvaluateAsync("""
            () => {
                const rows = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')];
                window.orderingRetained = rows.map(row => ({row,
                    name: row.querySelector('.athlete-name').firstChild,
                    rank: row.querySelector('.rank').firstChild}));
                window.orderingExtraWork = [];
                const observe = new MutationObserver(records => {
                    for (const record of records) {
                        if (record.type === 'attributes' && record.target.matches('tr[data-athlete-name]')
                            && (record.attributeName === 'style' || record.target.classList.contains('fade-in')
                                || (record.oldValue || '').split(/\s+/).includes('fade-in')))
                            window.orderingExtraWork.push(`row ${record.attributeName}`);
                        if (record.type === 'childList' && record.target.closest('.filter-count'))
                            window.orderingExtraWork.push('sidebar count rebuilt');
                    }
                });
                observe.observe(document.querySelector('#athleteDialogRuntime'),
                    {subtree:true, childList:true, attributes:true, attributeOldValue:true, attributeFilter:['style','class']});
            }
            """);
        var before = await Snapshot(page);
        await page.Locator("th[data-order-key=pheno] button").ClickAsync();
        Assert.NotEqual(before, await Snapshot(page));
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.Locator("#leaderboardOrderSwap").ClickAsync();
        await page.Locator("#leaderboardOrderSecondaryClear").ClickAsync();
        await page.Locator("#leaderboardOrderPrimaryClear").ClickAsync();
        await Assertions.Expect(Rows(page).First.Locator(".rank")).ToHaveTextAsync("1");
        Assert.True(await page.EvaluateAsync<bool>("orderingRetained.every(x=>x.row.isConnected && x.row.querySelector('.athlete-name').firstChild===x.name && x.row.querySelector('.rank').firstChild===x.rank)"));
        Assert.Empty(await page.EvaluateAsync<string[]>("orderingExtraWork"));
        await Assertions.Expect(page.Locator("tr.fade-in")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task CrowdUpdate_InvalidatesTheMatchingCohortAndItsOrderingValues()
    {
        using var client = App.CreateClient();
        var source = JsonNode.Parse(await client.GetStringAsync("/api/data/athletes"))!.AsArray();
        var target = source.OfType<JsonObject>().First(athlete => athlete["DateOfBirth"] is not null
            && (athlete["CrowdCount"]?.GetValue<int>() ?? 0) < 100);
        var name = target["Name"]!.GetValue<string>();
        var slug = target["AthleteSlug"]!.GetValue<string>();
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?view=crowd&sort=crowdCount");
        await Loaded(page);
        var before = await Rows(page).CountAsync();
        Assert.DoesNotContain(name, await Rows(page).EvaluateAllAsync<string[]>("rows=>rows.map(row=>row.dataset.athleteName)"));

        // The real Guess My Age update changes a result in place, without
        // replacing the directory. Crossing 100 guesses also changes eligibility.
        await page.EvaluateAsync("slug=>window.updateAthleteCrowdAge(slug,25,100000)", slug);
        var header = page.Locator("th[data-order-key=crowdCount] button");
        await header.ClickAsync();
        await header.ClickAsync();
        await Assertions.Expect(Rows(page).First).ToHaveAttributeAsync("data-athlete-name", name);
        await Assertions.Expect(Rows(page).First.Locator("td[data-order-key=crowdCount]")).ToHaveTextAsync("100000");
        Assert.Equal(before + 1, await Rows(page).CountAsync());
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.Locator("#leaderboardOrderSecondary").SelectOptionAsync("crowd");
        await Assertions.Expect(Rows(page).First.Locator("td[data-order-key=crowd]")).ToHaveTextAsync("25.0");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImmediateFilterChanges_DoNotLetPendingSearchRewriteAnOpenedAthleteRoute(bool clearSearch)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, Ordered);
        await Loaded(page);
        // Freeze wall time before pausing timers; protocol latency must not
        // advance past the timestamp between these two calls.
        const string pausedTime = "2026-10-17T12:00:01Z";
        await page.Clock.SetFixedTimeAsync(pausedTime);
        await page.Clock.PauseAtAsync(pausedTime);
        await page.Locator("#athleteSearch").FillAsync("Michael Lustgarten");
        await page.Locator(clearSearch ? ".clear-icon" : "#leaderboardOrderReset").ClickAsync();
        var row = Rows(page).Filter(new() { HasText = "Michael Lustgarten" });
        await row.Locator(".athlete-name").FocusAsync();
        await row.EvaluateAsync("row => window.pendingSearchFocusedRow = row");
        await Assertions.Expect(row.Locator(".athlete-name")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Clock.RunForAsync(500);
        Assert.True(await row.EvaluateAsync<bool>("row => row === window.pendingSearchFocusedRow"),
            "A canceled search must not replace the rows again after the user has focused an athlete.");
        Assert.Equal("/athlete/michael-lustgarten", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync(clearSearch ? "" : "Michael Lustgarten");
    }

    [Fact]
    public async Task OrderingChanges_UseAPendingSearchAndKeepEmptyResultsConsistent()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAsync(page, Ordered);
        await Loaded(page);
        var targetName = await Rows(page).Filter(new() { HasText = "Michael Lustgarten" }).Locator(".athlete-name").InnerTextAsync();
        await page.Locator("#athleteSearch").FillAsync(targetName);
        await page.Locator("#leaderboardOrderReset").ClickAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).First.Locator(".athlete-name")).ToHaveTextAsync(targetName);
        await page.Locator("#athleteSearch").FillAsync("no-matching-athlete-982164");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(0);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.Locator("#leaderboardOrderPrimary").SelectOptionAsync("age");
        await Assertions.Expect(page.Locator(".leaderboard .no-results")).ToHaveCountAsync(1);
        await page.Locator("#leaderboardOrderDirection").ClickAsync();
        await Assertions.Expect(page.Locator(".leaderboard .no-results")).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task ClearOrdering_ResetsBothLevelsWithoutOpeningThePicker(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?sort=bortz&then=pace#rank-1");
        await Loaded(page);
        var reset = page.GetByRole(AriaRole.Button, new() { Name = "Clear ordering", Exact = true });
        await Assertions.Expect(reset).ToBeVisibleAsync();
        var target = (await reset.BoundingBoxAsync())!;
        Assert.True(target.Width >= 44 && target.Height >= 44);
        await page.Locator("#athleteSearch").FillAsync("Wen");
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToBeHiddenAsync();
        await reset.ClickAsync();
        await Assertions.Expect(reset).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("Wen");
        await Assertions.Expect(page.Locator("th.leaderboard-order-column, td.leaderboard-order-value")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#leaderboardOrderStatus")).ToHaveTextAsync("Official ranking restored.");
        Assert.DoesNotContain("sort=", page.Url);
        Assert.DoesNotContain("then=", page.Url);
        Assert.EndsWith("#rank-1", page.Url);
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(reset).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(2);
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, true)]
    [InlineData(320, true)]
    public async Task SwapOrdering_KeepsDirectionsWithFieldsAndRestoresHistory(int width, bool touch)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, HasTouch = touch, IsMobile = touch,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?sort=vitd&dir=desc&then=tests&thenDir=asc#rank-1");
        await Loaded(page);
        var original = await Snapshot(page);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        var swap = page.GetByRole(AriaRole.Button, new() { Name = "Swap order by and then by", Exact = true });
        var target = (await swap.BoundingBoxAsync())!;
        Assert.True(target.Width >= 44 && target.Height >= 44);
        if (touch) await swap.TapAsync();
        else
        {
            await page.Keyboard.PressAsync("Shift+Tab");
            await Assertions.Expect(swap).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
        }
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToBeVisibleAsync();
        await Assertions.Expect(swap).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderPrimary")).ToHaveValueAsync("tests");
        await Assertions.Expect(page.Locator("#leaderboardOrderSecondary")).ToHaveValueAsync("vitd");
        await Assertions.Expect(page.Locator("th[data-order-key=tests]")).ToHaveAttributeAsync("aria-sort", "ascending");
        await Assertions.Expect(page.Locator("th[data-order-key=vitd]")).ToHaveAttributeAsync("data-order-level", "2");
        await Assertions.Expect(page.Locator("th[data-order-key=vitd]")).ToHaveAttributeAsync("data-order-direction", "desc");
        Assert.EndsWith("#rank-1", page.Url);
        var swapped = await Snapshot(page);
        Assert.NotEqual(original, swapped);
        // A shared swapped URL must render exactly the same rows and values before JavaScript.
        await using var serverContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var server = await serverContext.NewPageAsync();
        await NavigateAsync(server, page.Url);
        Assert.Equal(swapped, await Snapshot(server));
        await swap.ClickAsync();
        Assert.Equal(original, await Snapshot(page));
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardOrderPrimary")).ToHaveValueAsync("tests");
        Assert.Equal(swapped, await Snapshot(page));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task QuickClear_PrimaryClearsBothAndSecondaryClearsOnlyItself(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?view=pheno&search=Wen&sort=bortz&dir=desc&then=age&thenDir=desc#rank-1");
        await Loaded(page);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        var panel = page.Locator("#leaderboardOrderPanel");
        var primary = page.Locator("#leaderboardOrderPrimary");
        var secondary = page.Locator("#leaderboardOrderSecondary");
        var primaryClear = page.GetByRole(AriaRole.Button, new() { Name = "Clear both ordering fields", Exact = true });
        var secondaryClear = page.GetByRole(AriaRole.Button, new() { Name = "Clear then by", Exact = true });
        var swap = page.Locator("#leaderboardOrderSwap");
        foreach (var button in new[] { primaryClear, secondaryClear })
        {
            var target = (await button.BoundingBoxAsync())!;
            Assert.True(target.Width >= 44 && target.Height >= 44);
        }
        await secondaryClear.Locator("svg").ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(secondary).ToBeFocusedAsync();
        await Assertions.Expect(primary).ToHaveValueAsync("bortz");
        await Assertions.Expect(secondary).ToHaveValueAsync("");
        await Assertions.Expect(swap).ToBeDisabledAsync();
        await Assertions.Expect(secondaryClear).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("th[data-order-key=bortz]")).ToHaveAttributeAsync("aria-sort", "descending");
        await Assertions.Expect(page.Locator("th[data-order-key=age], td[data-order-key=age]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("Wen");
        Assert.Contains("view=pheno", page.Url);
        Assert.EndsWith("#rank-1", page.Url);
        Assert.DoesNotContain("then", page.Url);

        await secondary.SelectOptionAsync("age");
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(2);
        await primaryClear.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(primary).ToBeFocusedAsync();
        await Assertions.Expect(primary).ToHaveValueAsync("rank");
        await Assertions.Expect(secondary).ToBeDisabledAsync();
        await Assertions.Expect(primaryClear).ToBeDisabledAsync();
        await Assertions.Expect(secondaryClear).ToBeDisabledAsync();
        await Assertions.Expect(swap).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("th.leaderboard-order-column, td.leaderboard-order-value")).ToHaveCountAsync(0);
        Assert.DoesNotContain("sort=", page.Url);
        Assert.DoesNotContain("then=", page.Url);
        Assert.Contains("search=Wen", page.Url);
        Assert.Contains("view=pheno", page.Url);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(762, 100, false)]
    [InlineData(762, 100, true)]
    [InlineData(912, 100, true)]
    [InlineData(1280, 100, true)]
    [InlineData(390, 100, true)]
    [InlineData(320, 100, true)]
    [InlineData(390, 200, true)]
    [InlineData(320, 200, true)]
    public async Task SelectedValues_FitBesideAthletesAndTakePriorityOverTheContextScore(int width, int textPercent, bool javaScript)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, JavaScriptEnabled = javaScript,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?sort=phenoPercent&dir=asc&then=bortzImprovement&thenDir=desc");
        if (javaScript) await Loaded(page);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        // Disabled JavaScript cannot deliver the animation-frame callbacks used by the live-page helper.
        if (javaScript) await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var score = page.Locator("th[data-order-key=metric]");
        if (width == 1280) await Assertions.Expect(score).ToBeVisibleAsync();
        else await Assertions.Expect(score).ToBeHiddenAsync();
        const string fitProbe = """
            argument => {
                const table = document.querySelector('#athleteLeaderboard > table');
                const row = table.querySelector('tr[data-athlete-name]');
                const athlete = row.querySelector('.athlete-td').getBoundingClientRect();
                const values = [...row.querySelectorAll('.leaderboard-order-value')];
                const problems = [];
                if (document.documentElement.scrollWidth > document.documentElement.clientWidth) problems.push('Horizontal page overflow.');
                if (getComputedStyle(row).display === 'grid') {
                    const name = document.createRange(); name.selectNodeContents(row.querySelector('.athlete-name-content') || row.querySelector('.athlete-name'));
                    const nameLeft = name.getBoundingClientRect().left;
                    const headingLeft = table.querySelector('th[data-order-key="name"] .order-heading-label').getBoundingClientRect().left;
                    if (Math.abs(nameLeft - headingLeft) > 1) problems.push(`Athlete name/heading alignment: ${nameLeft} / ${headingLeft}; portrait ${row.querySelector('.portrait-wrapper').getBoundingClientRect().width}; gap ${getComputedStyle(row.querySelector('.athlete-td')).gap}; padding ${getComputedStyle(row.querySelector('.athlete-name')).paddingInline}; cell left ${athlete.left} / ${table.querySelector('th[data-order-key="name"]').getBoundingClientRect().left}.`);
                }
                for (const element of [...table.querySelectorAll('thead th'), ...values, row.querySelector('.age-reduction-td')]) {
                    const cell = element.getBoundingClientRect();
                    if (!cell.width) continue;
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        const range = document.createRange(); range.selectNodeContents(walker.currentNode);
                        for (const rect of range.getClientRects()) if (rect.width && (rect.left < cell.left - 1 || rect.right > cell.right + 1)) problems.push(`Clipped text: ${walker.currentNode.textContent}`);
                    }
                }
                for (const cell of values) {
                    const header = table.querySelector(`th[data-order-key="${cell.dataset.orderKey}"] .order-heading-label`);
                    const value = document.createRange(); value.selectNodeContents(cell);
                    if (Math.abs(value.getBoundingClientRect().right - header.getBoundingClientRect().right) > 1) problems.push(`Header/value alignment: ${cell.dataset.orderKey}`);
                    if (argument.TextPercent === 100 && argument.Width >= 762 && argument.Width <= 912) {
                        const box = cell.getBoundingClientRect();
                        if (Math.abs((box.top + box.bottom - athlete.top - athlete.bottom) / 2) > 1) problems.push('Selected values should fit beside the athlete.');
                    }
                }
                if (argument.TextPercent === 100 && argument.Width >= 762 && argument.Width <= 912 && row.getBoundingClientRect().height > 76) problems.push('The row still reserves space for stacked values.');
                return problems;
            }
            """;
        var fitProblems = await page.EvaluateAsync<string[]>(fitProbe, new { Width = width, TextPercent = textPercent });
        var screenshots = Environment.GetEnvironmentVariable("LWC_ORDER_SCREENSHOTS");
        if (javaScript && !string.IsNullOrWhiteSpace(screenshots))
        {
            Directory.CreateDirectory(screenshots);
            await page.Locator("#athleteLeaderboard > table > thead").EvaluateAsync("element => element.scrollIntoView({ block:'start' })");
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"ordering-fit-{width}-{textPercent}-{(javaScript ? "hydrated" : "server")}.png") });
        }
        Assert.True(fitProblems.Length == 0, string.Join("\n", fitProblems));
        if (!javaScript) return;
        // An explicitly selected competition score must never disappear with its context column.
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.Locator("#leaderboardOrderPrimary").SelectOptionAsync("metric");
        await Assertions.Expect(score).ToBeVisibleAsync();
        await Assertions.Expect(Rows(page).First.Locator(".age-reduction-td")).ToBeVisibleAsync();
        Assert.Empty(await page.EvaluateAsync<string[]>(fitProbe, new { Width = width, TextPercent = textPercent }));
        await page.Locator("#leaderboardOrderPrimaryClear").ClickAsync();
        await Assertions.Expect(page.Locator("th.leaderboard-order-column")).ToHaveCountAsync(0);
        await Assertions.Expect(score).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(320, 100)]
    [InlineData(390, 100)]
    [InlineData(1024, 100)]
    [InlineData(1440, 100)]
    [InlineData(1024, 200)]
    [InlineData(1440, 200)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    public async Task MixedLengthHeadersAndMobileNames_StayReadable(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/leaderboard?sort=bortz&then=pace");
        await Loaded(page);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var problems = await page.EvaluateAsync<string[]>("""
            textPercent => {
                const board = document.querySelector('#athleteLeaderboard').getBoundingClientRect();
                const problems = [];
                for (const header of document.querySelectorAll('#athleteLeaderboard > table thead th')) {
                    const cell = header.getBoundingClientRect();
                    if (!cell.width) continue;
                    const walker = document.createTreeWalker(header, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        const range = document.createRange(); range.selectNodeContents(walker.currentNode);
                        for (const rect of range.getClientRects()) {
                            if (rect.width && (rect.left < Math.max(board.left,cell.left) - 1 || rect.right > Math.min(board.right,cell.right) + 1)) problems.push(`Clipped heading: ${walker.currentNode.textContent}`);
                        }
                    }
                }
                const headers = [...document.querySelectorAll('th.leaderboard-order-column')];
                const metric = document.querySelector('th[data-order-key="metric"]');
                const help = metric.querySelector('.leaderboard-rules-link').getBoundingClientRect();
                const score = metric.querySelector('button').getBoundingClientRect();
                if (metric.getBoundingClientRect().width && (Math.abs((help.top + help.bottom - score.top - score.bottom) / 2) > 1 || help.left < score.right - 1)) problems.push('The ranking help icon wrapped away from its heading.');
                if (innerWidth <= 768 && headers.length === 2 && textPercent === 100) {
                    const labels = headers.map(h => h.querySelector('.order-heading-label').getBoundingClientRect());
                    if (Math.abs(labels[0].top - labels[1].top) > 1) problems.push('Selected headings do not share a top edge.');
                    const row = document.querySelector('#athleteLeaderboard tr[data-athlete-name]');
                    for (const header of headers) {
                        const cell = row.querySelector(`td[data-order-key="${header.dataset.orderKey}"]`);
                        const value = document.createRange(); value.selectNodeContents(cell);
                        if (Math.abs(value.getBoundingClientRect().right - header.querySelector('.order-heading-label').getBoundingClientRect().right) > 1) problems.push(`Misaligned value: ${header.dataset.orderKey}`);
                    }
                }
                return problems;
            }
            """, textPercent);
        Assert.Empty(problems);
        if (width <= 390 && textPercent == 100)
        {
            var name = Rows(page).Locator(".athlete-name-content").Filter(new() { HasText = "Siim Land" }).First;
            Assert.Equal(1, await name.EvaluateAsync<int>("element => { const range = document.createRange(); range.selectNodeContents(element); return new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size; }"));
            var overlap = await name.EvaluateAsync<bool>("""
                element => {
                    const row = element.closest('tr');
                    const rank = row.querySelector('.rank').getBoundingClientRect();
                    const marker = row.querySelector('.new-bubble').getBoundingClientRect();
                    return marker.left < rank.right && marker.right > rank.left && marker.top < rank.bottom && marker.bottom > rank.top;
                }
                """);
            Assert.False(overlap, "The new-athlete marker must not overlap the competition medal.");
        }
        var screenshots = Environment.GetEnvironmentVariable("LWC_ORDER_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(screenshots)) await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"ordering-bortz-pace-{width}-{textPercent}.png") });
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
        await NavigateAsync(page, "/leaderboard?sort=vitd&dir=desc");
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
        await NavigateAsync(page, "/league/pheno?sort=age&dir=desc&then=albumin");
        await Loaded(page);
        var row = Rows(page).Nth(4);
        var name = (await row.GetAttributeAsync("data-athlete-name"))!;
        var rank = await row.Locator(".rank").InnerTextAsync();
        await page.Locator("#athleteSearch").FillAsync(name);
        var result = Rows(page).Filter(new() { HasText = name }).First;
        await Assertions.Expect(result.Locator(".rank")).ToHaveTextAsync(rank);
        Assert.Contains("sort=age", page.Url);
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.fonts.ready");
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
        await NavigateAsync(page, Ordered);
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
        await Assertions.Expect(page.Locator("#leaderboardOrderReset")).ToBeFocusedAsync();
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
        // The shared strip scrolls with the document; enlarged text keeps its focused field in view.
        var focused = (await page.Locator("#leaderboardOrderPrimary").BoundingBoxAsync())!;
        Assert.InRange(focused.Y, 0, height);
        Assert.InRange(focused.Y + focused.Height, 0, height + 1);
    }

    [Theory]
    [InlineData(1440, 1000, false)]
    [InlineData(1024, 900, false)]
    [InlineData(768, 1000, false)]
    [InlineData(844, 390, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 720, false)]
    public async Task HomepageComposer_UsesTheSharedStripAndKeepsFocusVisible(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/?sort=pheno&then=age&thenDir=desc");
        await Loaded(page);
        await page.Locator("#view-ultimate").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#athleteSearch")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#group-flags")).ToBeFocusedAsync();
        foreach (var textPercent in new[] { 100, 200 })
        {
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = `${size}%`", textPercent);
            await page.Locator("#leaderboardOrderToggle").ClickAsync();
            var panel = page.Locator("#leaderboardOrderPanel");
            await Assertions.Expect(panel).ToBeVisibleAsync();
            Assert.Equal("static", await panel.EvaluateAsync<string>("panel => getComputedStyle(panel).position"));
            Assert.Equal(2, await panel.Locator(".order-selected-value").CountAsync());
            var bounds = (await panel.BoundingBoxAsync())!;
            Assert.InRange(bounds.X, 0, width);
            Assert.InRange(bounds.X + bounds.Width, 0, width + 1);
            var focus = (await page.Locator("#leaderboardOrderPrimary").BoundingBoxAsync())!;
            var sticky = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height')) || 0");
            Assert.InRange(focus.Y, sticky, height);
            Assert.InRange(focus.Y + focus.Height, 0, height + 1);
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeFocusedAsync();
        }
    }

    [Fact]
    public async Task ToolbarContainerBoundaries_ReflowAtNormalAndEnlargedTextSizes()
    {
        var conditions = GetResponsiveContainerInventory().Where(condition => condition.Name == "lwc-bar").ToArray();
        Assert.NotEmpty(conditions);
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await AssertResponsiveContainerBoundariesAsync(page, conditions);
    }

    [Theory]
    [InlineData("/", false)]
    [InlineData("/", true)]
    [InlineData("/leaderboard", false)]
    [InlineData("/leaderboard", true)]
    public async Task EnlargedToolbarText_KeepsRankingLabelsClearOfSearch(string route, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAsync(page, route + "?sort=phenoPercent");
        await Loaded(page);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator("#athleteSearch").FocusAsync();
        await page.Locator(".leaderboard-controls").EvaluateAsync("element => scrollTo(0, scrollY + element.getBoundingClientRect().top - 120)");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var labels = page.Locator(".leaderboard-view-switcher .view-badge:visible");
        Assert.True(await labels.CountAsync() >= 2);
        var problems = await labels.EvaluateAllAsync<string[]>("""
            labels => {
                const search = document.querySelector('.search-container').getBoundingClientRect();
                const problems = [];
                for (const label of labels) {
                    const walker = document.createTreeWalker(label, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        if (!walker.currentNode.textContent.trim()) continue;
                        const range = document.createRange(); range.selectNodeContents(walker.currentNode);
                        for (const rect of range.getClientRects()) {
                            if (!rect.width || !rect.height) continue;
                            if (Math.min(rect.right, search.right) > Math.max(rect.left, search.left) + 1
                                && Math.min(rect.bottom, search.bottom) > Math.max(rect.top, search.top) + 1)
                                problems.push(`Search overlaps ranking text: ${label.textContent.trim()}`);
                            if (!label.contains(document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)))
                                problems.push(`Ranking text is obscured: ${label.textContent.trim()}`);
                        }
                    }
                }
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("\n", problems));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await page.Locator("#view-ultimate").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#athleteSearch")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task HomepageOrdering_NewlyVisibleAthletesKeepTheirSelectedLeagueRankAndScore()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await using var serverContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        var server = await serverContext.NewPageAsync();
        await NavigateAsync(page, "/?view=pheno");
        await Loaded(page);
        foreach (var direction in new[] { "asc", "desc" })
        {
            await page.Locator("th[data-order-key=name] button").ClickAsync();
            await Assertions.Expect(page.Locator("th[data-order-key=name]")).ToHaveAttributeAsync("data-order-direction", direction);
            await NavigateAsync(server, page.Url);
            Assert.Equal(await Snapshot(server), await Snapshot(page));
        }
    }

    [Fact]
    public async Task HomepageCustomOrder_IncludesPodiumAthletesBeforeLimitingAndViewAllKeepsState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAsync(page, "/?sort=name&then=age");
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

    private static async Task NavigateAsync(IPage page, string path)
    {
        // Ordering is ready independently of portrait downloads. Keep layout
        // prerequisites and the callers' explicit data/hydration assertions.
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet !== null)");
        await page.EvaluateAsync("() => document.fonts.ready");
    }

    private static Task Loaded(IPage page) => Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.", new() { Timeout = 60000 });
    private static Task<string[]> Snapshot(IPage page) => Rows(page).EvaluateAllAsync<string[]>("rows=>rows.map(row=>[row.id,row.dataset.athleteName,row.querySelector('.rank').textContent.trim(),row.querySelector('.age-reduction').textContent.trim(),...Array.from(row.querySelectorAll('.leaderboard-order-value'),cell=>cell.textContent.trim())].join('|'))");
}
