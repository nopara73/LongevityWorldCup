using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteStatsResponsiveBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false, 1440)]
    [InlineData(false, 390)]
    [InlineData(true, 1440)]
    [InlineData(true, 390)]
    public async Task AthleteWhy_AlreadyQuotedSourceKeepsOneOpenerBeforeAndAfterHydration(bool javascript, int width)
    {
        using var client = App.CreateClient();
        var athletes = (await client.GetFromJsonAsync<JsonArray>("/api/data/athletes"))!;
        var original = athletes.Single(a => a?["Name"]?.GetValue<string>() == "QingqingZhuo")!["Why"]!.GetValue<string>();
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            JavaScriptEnabled = javascript
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/qingqingzhuo");
        if (javascript)
            await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').hasAttribute('data-server-rendered-profile') && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await Assertions.Expect(page.Locator("#athleteBio")).ToHaveTextAsync(original);
        var mark = await page.Locator("#athleteBio").EvaluateAsync<string>("bio => getComputedStyle(bio, '::before').content");
        Assert.True(mark is "none" or "normal", $"The original quoted why already has its opener; decoration was {mark}.");
        if (javascript)
        {
            await page.EvaluateAsync("() => window.openAthleteModalBySlug('michael-lustgarten')");
            await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content').dataset.athleteSlug === 'michael-lustgarten' && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
            Assert.Equal("\"“\" / \"\"", await page.Locator("#athleteBio").EvaluateAsync<string>("bio => getComputedStyle(bio, '::before').content"));
        }
    }

    [Theory]
    [InlineData("/athlete/michael-lustgarten", 390, 100)]
    [InlineData("/athlete/michael-lustgarten", 375, 200)]
    [InlineData("/athlete/michael-lustgarten", 390, 200)]
    [InlineData("/athlete/michael-lustgarten", 480, 200)]
    [InlineData("/athlete/michael-lustgarten", 769, 200)]
    [InlineData("/athlete/michael-lustgarten", 933, 200)]
    [InlineData("/about", 390, 200)]
    public async Task ProfileStats_KeepLabelsValuesAndUnitsInsideTheirRows(string path, int width, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (path == "/about")
        {
            await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();
        }
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten' && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var rows = page.Locator(".athlete-info-table tr:not(.athlete-info-divider-row)");
        Assert.True(await rows.CountAsync() > 0);
        var problems = await rows.EvaluateAllAsync<string[]>(
            """
            rows => rows.flatMap(row => {
                const bounds = row.getBoundingClientRect();
                return [...row.querySelectorAll('th, td')].flatMap(cell => {
                    const range = document.createRange();
                    range.selectNodeContents(cell);
                    return [...range.getClientRects()]
                        .filter(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1)
                        .map(() => `${row.querySelector('th')?.textContent.trim()}: clipped text`);
                });
            })
            """);
        Assert.True(problems.Length == 0, $"{path}, {width}px / {textPercent}%: {string.Join(", ", problems)}");
        Assert.Equal(await rows.CountAsync(), await rows.Locator("th[scope='row']").CountAsync());
    }

    [Theory]
    [InlineData("/athlete/michael-lustgarten", 320, false, 100)]
    [InlineData("/athlete/michael-lustgarten", 375, true, 100)]
    [InlineData("/athlete/michael-lustgarten", 320, false, 200)]
    [InlineData("/athlete/michael-lustgarten", 375, true, 200)]
    [InlineData("/athlete/michael-lustgarten", 390, false, 200)]
    [InlineData("/athlete/michael-lustgarten", 480, true, 200)]
    [InlineData("/about", 320, true, 200)]
    [InlineData("/about", 375, false, 200)]
    public async Task ProfileScoresAndRanks_ReflowWithEnlargedText(string path, int width, bool dark, int textPercent)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (path == "/about")
            await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();

        await page.WaitForFunctionAsync("""
            () => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten'
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
                && document.querySelector('#ageRadarCenter')?.dataset.diffSign === 'negative'
                && typeof document.querySelector('#detailsModal .modal-content')._scrollProgressListener === 'function'
                && [...document.querySelectorAll('link[rel="stylesheet"]')].every(link => !!link.sheet)
            """);
        // External chart scripts are stubbed in this fixture; expose the real summary
        // so the same values and styles are exercised as in a rendered radar chart.
        await page.EvaluateAsync("""
            () => {
                document.getElementById('ageRadarFallback').style.display = 'none';
                document.getElementById('ageRadarCenter').style.display = 'flex';
            }
            """);
        // A three-digit placement must remain readable as the competition grows.
        await page.Locator("#athleteRankings strong").First.EvaluateAsync("element => element.textContent = '#199'");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.WaitForFunctionAsync("percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100", textPercent);
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                for (const selector of ['#athleteName', '#athleteRankings a', '.age-radar-summary', '.proofs-heading']) {
                    for (const element of document.querySelectorAll(selector)) {
                        if (element.getBoundingClientRect().height === 0) continue;
                        const bounds = element.getBoundingClientRect();
                        const range = document.createRange();
                        range.selectNodeContents(element);
                        if ([...range.getClientRects()].some(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                            failures.push(`${selector}: text leaves its card`);
                        if (element.scrollWidth > element.clientWidth + 1)
                            failures.push(`${selector}: horizontal overflow`);
                    }
                }
                const score = document.querySelector('#ageRadarCenterValue').firstChild;
                const number = document.createRange();
                number.setStart(score, 0);
                number.setEnd(score, score.textContent.indexOf(' '));
                const numberLines = [...number.getClientRects()];
                if (numberLines.some(rect => Math.abs(rect.top - numberLines[0].top) > 1))
                    failures.push('age score: numeric value wraps');
                const modal = document.querySelector('#detailsModal .modal-content');
                if (modal.scrollWidth > modal.clientWidth + 1)
                    failures.push('dialog: horizontal overflow after scrolling');
                return failures;
            }
            """);
        Assert.True(problems.Length == 0, $"{path}, {width}px, dark={dark}, {textPercent}% text: {string.Join(", ", problems)}");
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task AthleteWhy_ReadsAsAnOpenPullQuoteRatherThanAnotherBox(int width, int height)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("""
            () => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten'
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
            """);
        // Exercise a realistic why regardless of the fixture's copy.
        await page.EvaluateAsync("() => { document.getElementById('athleteBio').textContent = 'To extend my health span as long as possible, and to help others do the same.'; }");

        var quote = await page.EvaluateAsync<string[]>(
            """
            () => {
                const bio = document.getElementById('athleteBio');
                const style = getComputedStyle(bio);
                const mark = getComputedStyle(bio, '::before');
                const box = bio.getBoundingClientRect();
                const name = document.getElementById('athleteName').getBoundingClientRect();
                return [style.borderTopWidth, style.backgroundColor, style.boxShadow, mark.content, style.textAlign,
                    String(Math.round(box.left + box.width / 2 - (name.left + name.width / 2))), String(box.right <= innerWidth && box.left >= 0)];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(quote[0] == "0px" && quote[1] == "rgba(0, 0, 0, 0)" && quote[2] == "none",
            $"{scenario}: the why should not be a bordered box (border {quote[0]}, background {quote[1]}, shadow {quote[2]}).");
        Assert.True(quote[3] == "\"“\" / \"\"", $"{scenario}: the why should open with a decorative quotation mark, not {quote[3]}.");
        Assert.Equal("center", quote[4]);
        Assert.True(Math.Abs(int.Parse(quote[5])) <= 2 && quote[6] == "true", $"{scenario}: the quote should sit centred under the name (offset {quote[5]}, inside {quote[6]}).");

        await page.EvaluateAsync("() => { document.getElementById('athleteBio').textContent = ''; }");
        Assert.False(await page.Locator("#athleteBio").IsVisibleAsync(), $"{scenario}: an athlete without a why should not show a lone quotation mark.");
    }
}
