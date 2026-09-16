using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class PageStructuredDataBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/leaderboard")]
    [InlineData("/league/pheno")]
    [InlineData("/league/bortz")]
    [InlineData("/league/improvement")]
    [InlineData("/league/bortz-improvement")]
    [InlineData("/league/crowd")]
    [InlineData("/league/amateur")]
    [InlineData("/flag/hungary")]
    [InlineData("/leaderboard?view=pheno&filters=men%27s,millennials")]
    [InlineData("/leaderboard?view=bortz&filters=amateur")]
    public async Task ServerAndHydratedLists_MatchTheRenderedSelection(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 844 } });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(path);
        var initial = PageStructuredDataTests.Single(PageStructuredDataTests.ReadGraph(await response!.TextAsync()), "ItemList");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var hydrated = await AssertMatchesVisibleRows(page);
        Assert.Equal(Items(initial), Items(hydrated));
        Assert.Equal(initial.GetProperty("@id").GetString(), hydrated.GetProperty("@id").GetString());
    }

    [Fact]
    public async Task SearchAndFilters_UpdateTheListWithoutRenumberingOrKeepingStaleEntries()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 844 } });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var rows = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible");
        var last = rows.Last;
        var name = await last.GetAttributeAsync("data-athlete-name");
        var rank = int.Parse((await last.Locator(".rank").TextContentAsync())!);
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type is "warning" or "error") errors.Add(message.Text); };
        await page.Locator("#athleteSearch").FillAsync(name!);
        await Assertions.Expect(rows).ToHaveCountAsync(1);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        var filtered = await AssertMatchesVisibleRows(page);
        Assert.Equal(rank, filtered.GetProperty("itemListElement")[0].GetProperty("position").GetInt32());
        await page.Locator("#athleteSearch").FillAsync("no-matching-athlete-987654321");
        await Assertions.Expect(rows).ToHaveCountAsync(0);
        Assert.Equal(0, (await AssertMatchesVisibleRows(page)).GetProperty("numberOfItems").GetInt32());
        await page.Locator("#athleteSearch").FillAsync("");
        await page.Locator("label.view-badge[for='view-pheno']").ClickAsync();
        await Assertions.Expect(page.Locator("#view-pheno")).ToBeCheckedAsync();
        await Assertions.Expect(rows).Not.ToHaveCountAsync(0);
        await AssertMatchesVisibleRows(page);
    }

    [Fact]
    public async Task DirectProfile_KeepsItsPersonWhileTheBackdropHydrates()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/ron-lugbill");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var graph = await page.EvaluateAsync<JsonElement>("() => JSON.parse(document.getElementById('pageStructuredData').textContent)['@graph']");
        var profile = Assert.Single(graph.EnumerateArray(), n => n.GetProperty("@type").GetString() == "ProfilePage");
        Assert.Equal("https://longevityworldcup.com/athlete/ron-lugbill#person", profile.GetProperty("mainEntity").GetProperty("@id").GetString());
        Assert.DoesNotContain(graph.EnumerateArray(), n => n.GetProperty("@type").GetString() == "ItemList");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CrowdQualificationAndTies_UseTheVisibleCountAndNameTieBreaks()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 844 } });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var source = JsonNode.Parse(await response.TextAsync())!.AsArray().OfType<JsonObject>()
                .Single(a => a["Name"]?.GetValue<string>() == "Michael Lustgarten");
            var data = new JsonArray();
            foreach (var (name, count) in new[] { ("Beta", 100), ("Under", 99), ("Alpha", 100), ("Gamma", 101) })
            {
                var athlete = source.DeepClone().AsObject();
                athlete["Name"] = name;
                athlete["DisplayName"] = name;
                athlete["AthleteSlug"] = name.ToLowerInvariant();
                athlete["CrowdAge"] = 30;
                athlete["CrowdCount"] = count;
                data.Add(athlete);
            }
            await route.FulfillAsync(new() { ContentType = "application/json", Body = data.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/crowd");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var list = await AssertMatchesVisibleRows(page);
        Assert.Equal(new[] { "Gamma", "Alpha", "Beta" }, Items(list).Select(item => item.Name));
        Assert.Equal(new[] { 1, 2, 3 }, Items(list).Select(item => item.Position));
    }

    [Fact]
    public async Task FailedHydration_RemovesTheListWhenTheTableShowsAnError()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 844 } });
        await context.RouteAsync("**/api/data/athletes", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard could not load.");
        Assert.False(await page.EvaluateAsync<bool>("() => JSON.parse(document.getElementById('pageStructuredData').textContent)['@graph'].some(n => n['@type'] === 'ItemList')"));
    }

    private static (int Position, string? Id, string? Name)[] Items(JsonElement list) =>
        list.GetProperty("itemListElement").EnumerateArray().Select(i => (
            i.GetProperty("position").GetInt32(), i.GetProperty("item").GetProperty("@id").GetString(),
            i.GetProperty("item").GetProperty("name").GetString())).ToArray();

    private static async Task<JsonElement> AssertMatchesVisibleRows(IPage page)
    {
        var result = await page.EvaluateAsync<JsonElement>("""
            () => {
                const list = JSON.parse(document.getElementById('pageStructuredData').textContent)['@graph'].find(n => n['@type'] === 'ItemList');
                const rows = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')].filter(row => row.style.display !== 'none');
                return { list, visible: rows.map(row => ({ rank: Number(row.querySelector('.rank').textContent), name: row.querySelector('.athlete-name').textContent.replace(/\s+/g,' ').trim() })) };
            }
            """);
        var list = result.GetProperty("list");
        var visible = result.GetProperty("visible").EnumerateArray().ToArray();
        var items = list.GetProperty("itemListElement").EnumerateArray().ToArray();
        Assert.Equal(visible.Length, list.GetProperty("numberOfItems").GetInt32());
        Assert.Equal(visible.Select(r => r.GetProperty("rank").GetInt32()), items.Select(i => i.GetProperty("position").GetInt32()));
        Assert.Equal(visible.Select(r => r.GetProperty("name").GetString()), items.Select(i => i.GetProperty("item").GetProperty("name").GetString()));
        return list;
    }
}
