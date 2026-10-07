using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardRowReflowBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 16, false)]
    [InlineData(390, 16, true)]
    [InlineData(320, 32, true)]
    [InlineData(390, 32, false)]
    [InlineData(480, 32, true)]
    public async Task LongNamesAndEnlargedText_KeepEveryRankingReadable(int width, int rootFontSize, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var names = new[] { "HealthOptimisers", "EatBiohackLove", "Vishwamithra Shashishekara", "QingqingZhuo" };
            var index = 0;
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                var displayName = names[index % names.Length];
                athlete["Name"] = $"{athlete["Name"]!.GetValue<string>()} {displayName}";
                athlete["DisplayName"] = displayName;
                if (index++ < 5)
                {
                    athlete["CrowdAge"] = 30;
                    athlete["CrowdCount"] = 150;
                }
            }
            await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
        });

        var page = await context.NewPageAsync();
        foreach (var path in new[]
        {
            "/", "/leaderboard", "/league/pheno", "/league/bortz",
            "/league/improvement", "/league/bortz-improvement", "/league/crowd"
        })
        {
            await NavigateAndSettleAsync(page, path);
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", rootFontSize);
            await page.WaitForFunctionAsync(
                "size => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - size) < .1",
                rootFontSize);
            await page.EvaluateAsync("() => document.fonts.ready");

            var visibleNames = await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").AllTextContentsAsync();
            var searchTerm = visibleNames.Contains("HealthOptimisers") ? "optim"
                : visibleNames.Contains("EatBiohackLove") ? "biohack"
                : visibleNames.Contains("QingqingZhuo") ? "qing" : "shash";
            foreach (var query in new[] { "", searchTerm, "" })
            {
                await page.Locator("#athleteSearch").FillAsync(query);
                var highlights = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name .highlight");
                if (query.Length > 0)
                    await Assertions.Expect(highlights.First).ToBeVisibleAsync();
                else
                    await Assertions.Expect(highlights).ToHaveCountAsync(0);

                var failures = await page.EvaluateAsync<string[]>(
                    """
                    async () => {
                        const rows = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')]
                            .filter(row => row.getBoundingClientRect().height > 0);
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const failures = [];
                        if (rows.length === 0) failures.push('No athlete rows were rendered.');
                        for (const row of rows) {
                            const name = row.querySelector('.athlete-name');
                            const athlete = row.querySelector('.athlete-td');
                            const score = row.querySelector('.age-reduction-td');
                            const nameBox = name.getBoundingClientRect();
                            const athleteBox = athlete.getBoundingClientRect();
                            const scoreBox = score.getBoundingClientRect();
                            const rowBox = row.getBoundingClientRect();
                            if (nameBox.left < athleteBox.left - 1 || nameBox.right > athleteBox.right + 1)
                                failures.push(`${name.textContent}: name leaves the athlete cell.`);
                            if (name.scrollWidth > name.clientWidth + 1 || score.scrollWidth > score.clientWidth + 1)
                                failures.push(`${name.textContent}: text leaves its cell.`);
                            if (nameBox.left < scoreBox.right - 1 && nameBox.right > scoreBox.left + 1
                                && nameBox.top < scoreBox.bottom - 1 && nameBox.bottom > scoreBox.top + 1)
                                failures.push(`${name.textContent}: name overlaps the score.`);
                            if (scoreBox.left < rowBox.left - 1 || scoreBox.right > rowBox.right + 1)
                                failures.push(`${name.textContent}: score leaves the row.`);
                            if (nameBox.height < 44)
                                failures.push(`${name.textContent}: name loses its touch target.`);
                            const textNodes = document.createTreeWalker(name, NodeFilter.SHOW_TEXT);
                            let previous = null;
                            while (textNodes.nextNode()) {
                                const text = textNodes.currentNode;
                                for (let i = 0; i < text.length; i++) {
                                    if (/\s/.test(text.textContent[i])) continue;
                                    const range = document.createRange();
                                    range.setStart(text, i);
                                    range.setEnd(text, i + 1);
                                    const box = range.getBoundingClientRect();
                                    if (box.left < athleteBox.left - 1 || box.right > athleteBox.right + 1)
                                        failures.push(`${name.textContent}: a character leaves the athlete cell.`);
                                    if (previous && (box.top < previous.top - 1
                                        || (Math.abs(box.top - previous.top) < 1 && box.left < previous.left - 1)))
                                        failures.push(`${name.textContent}: characters lose their reading order.`);
                                    previous = box;
                                }
                            }
                        }
                        return failures;
                    }
                    """);
                Assert.True(failures.Length == 0,
                    $"{path}, search '{query}', {width}px, root {rootFontSize}px: {string.Join(" ", failures)}");
            }
            var firstName = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").First;
            await firstName.FocusAsync();
            await Assertions.Expect(firstName).ToBeFocusedAsync();
        }
    }
}
