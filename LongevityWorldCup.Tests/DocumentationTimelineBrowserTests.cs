using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class DocumentationTimelineBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 100, false)]
    [InlineData(1440, 100, true)]
    [InlineData(820, 100, false)]
    [InlineData(390, 100, true)]
    [InlineData(320, 100, false)]
    [InlineData(390, 200, false)]
    public async Task HistoryTimeline_SetsEachDateApartWithoutChangingHeadingsOrLinks(int width, int textPercent, bool dark)
    {
        var markdown = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "LongevityWorldCup.Documentation", "LongevitySportHistory.md"));
        var datedHeadings = Regex.Matches(markdown, "^### (\\d{4}.*)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value.Trim()).ToArray();
        Assert.NotEmpty(datedHeadings);

        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            // Geometry is checked in its final reading state; motion has separate coverage.
            ReducedMotion = ReducedMotion.Reduce
        });
        // The page's screenshots are hosted on GitHub; keep the test hermetic.
        await context.RouteAsync(url => !url.StartsWith(App.BaseAddress.ToString(), StringComparison.Ordinal), route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/history", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var headings = await page.Locator(".documentation-timeline-entry > h3").EvaluateAllAsync<string[]>("headings => headings.map(h => h.textContent)");
        Assert.Equal(datedHeadings, headings);
        Assert.Equal(1, await page.Locator(".documentation-timeline").CountAsync());

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const rail = document.querySelector('.documentation-timeline');
                const railLine = getComputedStyle(rail, '::before');
                const railLeft = rail.getBoundingClientRect().left + parseFloat(railLine.left) + parseFloat(railLine.width) / 2;
                for (const entry of document.querySelectorAll('.documentation-timeline-entry')) {
                    const heading = entry.querySelector('h3');
                    const date = heading.querySelector('.documentation-timeline-date');
                    const title = heading.querySelector('.documentation-timeline-title');
                    const name = heading.textContent;
                    const dateBox = date.getBoundingClientRect(), titleBox = title.getBoundingClientRect();
                    if (!heading.id) issues.push(`${name}: lost its id`);
                    if (!dateBox.width || !titleBox.width) issues.push(`${name}: date or title hidden`);
                    if (dateBox.bottom > titleBox.top + 1) issues.push(`${name}: date is not above the title`);
                    // The marker sits on the rail, level with the date.
                    const marker = getComputedStyle(entry, '::before');
                    const entryBox = entry.getBoundingClientRect();
                    const markerLeft = entryBox.left + parseFloat(marker.left);
                    const markerCenterX = markerLeft + parseFloat(marker.width) / 2;
                    const markerCenterY = entryBox.top + parseFloat(marker.top) + parseFloat(marker.height) / 2;
                    if (Math.abs(markerCenterX - railLeft) > 1.5) issues.push(`${name}: marker is off the rail`);
                    if (markerCenterY < dateBox.top - 2 || markerCenterY > dateBox.bottom + 2) issues.push(`${name}: marker is not level with the date`);
                    const hollow = marker.backgroundColor !== marker.borderTopColor;
                    if (hollow !== /announced/i.test(date.textContent)) issues.push(`${name}: announced marker does not match`);
                    const link = document.querySelector(`.documentation-nav a[href="#${CSS.escape(heading.id)}"]`);
                    if (!link) issues.push(`${name}: contents link missing`);
                    else if (link.textContent !== name) issues.push(`${name}: contents link text changed to ${link.textContent}`);
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}% / {(dark ? "dark" : "light")}: {string.Join(", ", problems)}");
    }

    [Theory]
    [InlineData("/about")]
    [InlineData("/ruleset")]
    public async Task OtherDocumentationPages_HaveNoTimeline(string path)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = App.BaseAddress.ToString() });
        await context.RouteAsync(url => !url.StartsWith(App.BaseAddress.ToString(), StringComparison.Ordinal), route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await page.Locator(".documentation-timeline, .documentation-nav-timeline").CountAsync());
        Assert.True(await page.Locator(".documentation-document h2").CountAsync() > 0);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
