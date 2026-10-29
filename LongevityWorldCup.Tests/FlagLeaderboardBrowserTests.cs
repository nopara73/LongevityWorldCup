using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class FlagLeaderboardBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/leaderboard?group=flags", "Ultimate Flag League")]
    [InlineData("/league/pheno?group=flags", "Pheno Age by Flag")]
    [InlineData("/league/bortz?group=flags", "Bortz Age by Flag")]
    [InlineData("/league/crowd?group=flags", "Crowd Age by Flag")]
    [InlineData("/league/improvement?group=flags", "Pheno Improvement by Flag")]
    [InlineData("/league/bortz-improvement?group=flags", "Bortz Improvement by Flag")]
    [InlineData("/leaderboard?group=flags&filters=women%27s", "Women's Flag League")]
    [InlineData("/leaderboard?group=flags&view=pheno&filters=gen%2520x", "Gen X Pheno Age by Flag")]
    [InlineData("/flag/hungary?group=flags", "Ultimate Flag League")]
    [InlineData("/leaderboard?group=flags&view=pheno&filters=hungary", "Pheno Age by Flag")]
    [InlineData("/league/amateur?group=flags", "Amateur Flag League")]
    public async Task InitialHtmlAndHydration_ShowTheSameTeamsAndScores(string path, string title)
    {
        await using var plain = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var initial = await plain.NewPageAsync();
        await initial.GotoAsync(path);
        var expected = await initial.Locator(".flag-team summary").AllTextContentsAsync();
        var expectedHeadings = await initial.Locator(".flag-section-heading").AllTextContentsAsync();
        var explanation = await initial.Locator("#rankingExplanation").TextContentAsync();
        await Assertions.Expect(initial.Locator(".flag-team-name small")).ToHaveCountAsync(0);
        Assert.True(await initial.Locator("#group-flags").IsCheckedAsync());
        await Assertions.Expect(initial.Locator(".collapsed-title")).ToHaveTextAsync(title.ToUpperInvariant());
        await Assertions.Expect(initial.Locator(".collapsed-title")).ToHaveAttributeAsync("aria-label", title);
        Assert.Equal($"{title} | Longevity World Cup", await initial.TitleAsync());
        await Assertions.Expect(initial.Locator("#flag-filter-section")).ToHaveAttributeAsync("hidden", "");
        await Assertions.Expect(initial.Locator("#athleteSearch")).ToHaveAttributeAsync("placeholder", "Search flags or athletes");
        if (path.Contains("hungary")) Assert.True(expected.Count > 1, "An old flag selection must not restrict the competition.");
        await Assertions.Expect(initial.Locator(".leaderboard > table")).ToBeHiddenAsync();
        if (expected.Count > 0)
        {
            await initial.Locator(".flag-team summary").First.ClickAsync();
            await Assertions.Expect(initial.Locator(".flag-team-athletes").First).ToBeVisibleAsync();
        }

        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync(title.ToUpperInvariant());
        await Assertions.Expect(page.Locator("#rankingExplanation")).ToHaveTextAsync(explanation!);
        Assert.Equal($"{title} | Longevity World Cup", await page.TitleAsync());
        var actual = await page.Locator(".flag-team summary").AllTextContentsAsync();
        static string Normalize(string value) => System.Text.RegularExpressions.Regex.Replace(value, @"\s+", "").Replace("⌄", "");
        Assert.Equal(expected.Select(Normalize), actual.Select(Normalize));
        Assert.Equal(expectedHeadings, await page.Locator(".flag-section-heading").AllTextContentsAsync());
        await Assertions.Expect(page.Locator(".flag-team-name small")).ToHaveCountAsync(0);
        Assert.Empty(errors);
        var subjects = await page.EvaluateAsync<string[]>("() => JSON.parse(document.getElementById('pageStructuredData').textContent)['@graph'].find(x => x['@type'] === 'ItemList').itemListElement.map(x => x.item.name)");
        Assert.Equal(await page.Locator(".flag-team").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.flag)"), subjects);
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task GroupingAndClocks_SearchExpansionProfileAndReload_WorkTogether(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 1000 }, ReducedMotion = ReducedMotion.Reduce, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator(".flag-team").First).ToBeVisibleAsync();
        await page.Locator("label[for=view-pheno]").ClickAsync();
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync("PHENO AGE BY FLAG");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#flag-filter-section")).ToBeHiddenAsync();
        await page.Locator(".sidebar-close").ClickAsync();
        Assert.True(await page.Locator("#group-flags").IsCheckedAsync());
        Assert.Contains("group=flags", page.Url);
        Assert.All(await page.Locator(".flag-team").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.clock)"), clock => Assert.Equal("pheno", clock));
        var team = page.Locator(".flag-team").Nth(1);
        var name = (await team.GetAttributeAsync("data-flag"))!;
        var rank = await team.Locator(".flag-team-rank").TextContentAsync();
        var score = await team.Locator(".flag-team-score").TextContentAsync();
        await page.Locator("#athleteSearch").FillAsync(name);
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        team = page.Locator(".flag-team");
        await Assertions.Expect(team.Locator(".flag-team-rank")).ToHaveTextAsync(rank!);
        await Assertions.Expect(team.Locator(".flag-team-score")).ToHaveTextAsync(score!);
        await team.Locator("summary").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(team.Locator(".flag-team-athletes")).ToBeVisibleAsync();
        Assert.Equal(3, await team.Locator(".flag-athlete-link").CountAsync());
        var total = await team.Locator(".flag-team-score").EvaluateAsync<double>("cell => parseFloat(cell.firstChild.textContent)");
        var displayedAthleteTotal = await team.Locator(".flag-athlete-metric").EvaluateAllAsync<double>("cells => cells.reduce((sum, cell) => sum + parseFloat(cell.firstChild.textContent), 0)");
        Assert.InRange(Math.Abs(total - displayedAthleteTotal), 0, .150001);
        await team.Locator(".flag-athlete-link").First.ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("Pheno Age by Flag | Longevity World Cup", await page.TitleAsync());
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync("PHENO AGE BY FLAG");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".flag-team-score")).ToHaveTextAsync(score!);
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).Not.ToHaveTextAsync("1 flag");
        await page.Locator(".flag-team summary").First.ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        var output = Environment.GetEnvironmentVariable("LWC_FLAG_SCREENSHOTS");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            await page.Locator(".leaderboard-controls").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"flags-{width}-{(dark ? "dark" : "light")}.png") });
        }
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = ''");
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator(".leaderboard > table")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#view-pheno").IsCheckedAsync());
        Assert.DoesNotContain("group=flags", page.Url);
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync("PHENO AGE LEAGUE");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#flag-filter-section")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Homepage_GroupingUsesTenTeams_AndClearingSearchDoesNotRevealThePodium()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(10);
        await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
        await page.Locator("#athleteSearch").FillAsync("Hungary");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await page.Locator("button.clear-icon").ClickAsync();
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(10);
        await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
        var link = await page.EvaluateAsync<string>("() => window.getFullLeaderboardUrlWithCurrentState()");
        Assert.Contains("group=flags", link);
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator(".podium")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FailedHydration_KeepsExpandableServerTeamsAndOffersRetry()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { Status = 503, Body = "Unavailable" }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?group=flags");
        await Assertions.Expect(page.Locator("#flagLeaderboard .leaderboard-retry-button")).ToBeVisibleAsync();
        await page.Locator(".flag-team summary").First.ClickAsync();
        await Assertions.Expect(page.Locator(".flag-team-athletes").First).ToBeVisibleAsync();
        await context.UnrouteAsync("**/api/data/athletes");
        await page.Locator("#flagLeaderboard .leaderboard-retry-button").ClickAsync();
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".flag-team-athletes").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CrowdTeams_OnlyScoreAthletesWithEnoughGuesses()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var index = 0;
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["CrowdAge"] = 30;
                athlete["CrowdCount"] = index < 3 ? 100 : 99;
                athlete["Flag"] = index++ == 0 ? "USA" : "United States";
            }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/crowd?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveAttributeAsync("data-flag", "United States");
        await Assertions.Expect(page.Locator(".flag-team-rank")).ToHaveTextAsync("1");
        await page.Locator(".flag-team summary").ClickAsync();
        await Assertions.Expect(page.Locator(".flag-athlete-link")).ToHaveCountAsync(3);
    }

    [Theory]
    [InlineData(1280, 16)]
    [InlineData(976, 16)]
    [InlineData(976, 20)]
    [InlineData(800, 16)]
    [InlineData(390, 16)]
    [InlineData(390, 32)]
    public async Task OpenFilters_KeepFlagNamesAndScoresReadableAcrossClocks(int width, int rootFontSize)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 1100 }, ReducedMotion = ReducedMotion.Reduce });
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["CrowdAge"] = 30;
                athlete["CrowdCount"] = 150;
            }
        });
        var page = await context.NewPageAsync();
        foreach (var view in new[] { "ultimate", "pheno", "bortz", "crowd" })
        {
            await page.GotoAsync($"/leaderboard?group=flags&filters=women%27s&view={view}");
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", rootFontSize);
            await page.Locator(".sidebar-toggle").ClickAsync();
            await Assertions.Expect(page.Locator(".sidebar")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("expanded"));
            await page.EvaluateAsync("() => document.fonts.ready");
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            var failures = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const failures = [];
                    if (document.documentElement.scrollWidth > innerWidth) failures.push('Page overflows.');
                    for (const summary of document.querySelectorAll('.flag-team summary')) {
                        const name = summary.querySelector('.flag-team-name');
                        const text = name.lastElementChild;
                        const score = summary.querySelector('.flag-team-score');
                        const rowBox = summary.getBoundingClientRect();
                        const nameBox = name.getBoundingClientRect();
                        const scoreBox = score.getBoundingClientRect();
                        const fontSize = parseFloat(getComputedStyle(text).fontSize);
                        if (text.textContent.length > 7 && text.getBoundingClientRect().width < fontSize * 3)
                            failures.push(`${text.textContent}: name is squeezed into a narrow column.`);
                        if (nameBox.left < scoreBox.right - 1 && nameBox.right > scoreBox.left + 1
                            && nameBox.top < scoreBox.bottom - 1 && nameBox.bottom > scoreBox.top + 1)
                            failures.push(`${text.textContent}: name overlaps the score.`);
                        if (scoreBox.right > rowBox.right + 1 || score.scrollWidth > score.clientWidth + 1)
                            failures.push(`${text.textContent}: score leaves its row.`);
                    }
                    return failures;
                }
                """);
            Assert.True(failures.Length == 0, $"{view}, {width}px, {rootFontSize}px text: {string.Join("; ", failures)}");
            var output = Environment.GetEnvironmentVariable("LWC_FLAG_SCREENSHOTS");
            if (!string.IsNullOrEmpty(output) && width == 976 && view == "pheno")
            {
                await page.Locator(".leaderboard-controls").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"flags-filters-{width}-{rootFontSize}.png") });
            }
        }
    }
}
