using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeaderboardNameWrapBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 1440)]
    [InlineData("/leaderboard", 1440)]
    public async Task ServerRenderedRows_KeepNamesReadableBeforeHydration(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, JavaScriptEnabled = false
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '32px'");
        var broken = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('.server-rendered-leaderboard-row .athlete-name')]
                .filter(name => name.getBoundingClientRect().height > parseFloat(getComputedStyle(name).fontSize) * 5)
                .map(name => name.textContent)
            """);
        Assert.Empty(broken);
        Assert.True(await page.Locator(".server-rendered-leaderboard-row").CountAsync() > 0);
    }

    [Theory]
    [InlineData(320, 640, 100)]
    [InlineData(390, 844, 200)]
    [InlineData(320, 640, 200)]
    [InlineData(390, 844, 100)]
    [InlineData(1440, 900, 100)]
    public async Task PhoneRows_KeepEveryNameWordWhole(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var broken = await page.EvaluateAsync<string[]>(
            """
            () => {
                const broken = [];
                const names = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name] .athlete-name')]
                    .filter(name => name.getClientRects().length);
                for (const name of names) {
                    const walker = document.createTreeWalker(name, NodeFilter.SHOW_TEXT);
                    const glyphs = [];
                    for (let node; (node = walker.nextNode());) {
                        for (let index = 0; index < node.length; index++) {
                            const range = document.createRange();
                            range.setStart(node, index);
                            range.setEnd(node, index + 1);
                            const box = [...range.getClientRects()].filter(rect => rect.width > 0).at(-1);
                            glyphs.push({ char: node.data[index], top: box ? Math.round(box.top) : null });
                        }
                    }
                    // Match across highlight spans/text nodes; otherwise a word split at a
                    // highlight boundary can pass despite being broken between letters.
                    const text = glyphs.map(glyph => glyph.char).join('');
                    const athlete = name.closest('.athlete-td');
                    const identity = name.closest('.athlete-identity');
                    // A word wider than the complete fallback line must still wrap.
                    // On wider tables the portrait keeps its column, so only the
                    // identity is available; phone rows can use the entire cell.
                    const available = innerWidth <= 480 && getComputedStyle(athlete).display !== 'contents'
                        ? athlete.getBoundingClientRect().width
                        : identity.getBoundingClientRect().width;
                    const style = getComputedStyle(name);
                    const canvas = document.createElement('canvas');
                    const context = canvas.getContext('2d');
                    context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                    for (const part of text.matchAll(/[^\s \-\u2010]+/gu)) {
                        const lines = new Set(glyphs.slice(part.index, part.index + part[0].length)
                            .filter(glyph => glyph.top !== null).map(glyph => glyph.top));
                        const wordWidth = context.measureText(part[0]).width;
                        const padding = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight);
                        if (lines.size > 1 && wordWidth <= available - padding + 1) broken.push(part[0]);
                    }
                }
                return names.length ? broken : ['no names rendered'];
            }
            """);
        Assert.True(broken.Length == 0, $"{width}x{height} at {textPercent}%: names break inside {string.Join(", ", broken.Distinct().Take(12))}.");
    }

    [Theory]
    [InlineData(320, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    public async Task NamesWithMarkupAndUnicode_KeepTheirTargetsAndLastResortWrapping(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce, HasTouch = true, IsMobile = true
        });
        const string lastResort = "AbcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var names = new[] { "HealthOptimisers", "EatBiohackLove", "Vishwamithra Shashishekara", "Élodie-Anne Đorđević", "山田太郎", lastResort };
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            // A compact real-data fixture covers each spelling without duplicating
            // the synthetic extreme hundreds of times during every resize.
            athletes = new JsonArray(athletes.Take(18).Select(athlete => athlete!.DeepClone()).ToArray());
            var index = 0;
            foreach (var athlete in athletes.OfType<JsonObject>())
                athlete["DisplayName"] = names[index++ % names.Length];
            await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        foreach (var path in new[] { "/", "/leaderboard" })
        {
            await NavigateAndSettleAsync(page, path);
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            var renderedNames = await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").AllTextContentsAsync();
            Assert.NotEmpty(renderedNames);
            Assert.All(renderedNames, name => Assert.Contains(name, names));
            if (path == "/leaderboard") Assert.Contains(lastResort, renderedNames);
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
            await page.EvaluateAsync("async () => { await document.fonts.ready; for (let i = 0; i < 4; i++) await new Promise(requestAnimationFrame); }");
            foreach (var query in new[] { "", "optim", "" })
            {
                await page.Locator("#athleteSearch").FillAsync(query);
                if (query.Length > 0)
                    await Assertions.Expect(page.Locator(".leaderboard .athlete-name .highlight").First).ToBeVisibleAsync();
                await AssertNameGeometryAsync(page);
            }
            // CSS fallback must disappear when space returns, with the same rows and data.
            await page.SetViewportSizeAsync(1440, 900);
            await page.WaitForFunctionAsync("() => getComputedStyle(document.querySelector('.leaderboard tbody tr[data-athlete-name]')).display === 'table-row'");
            await AssertNameGeometryAsync(page);
            await page.SetViewportSizeAsync(width, 844);
            await AssertNameGeometryAsync(page);
        }
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task ActualDoubledText_EnlargesNamesAndPreservesSeparateRowParts(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce, HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var ratio = await page.EvaluateAsync<double>(
            """
            async () => {
                const name = document.querySelector('.leaderboard .athlete-name');
                const before = parseFloat(getComputedStyle(name).fontSize);
                // Match text enlargement even for fixed/clamped fonts. Preserve the root
                // and CSS viewport; a device-scale/screenshot multiplier cannot prove reflow.
                const sizes = [document.body, ...document.body.querySelectorAll('*')]
                    .filter(element => element instanceof HTMLElement)
                    .map(element => [element, parseFloat(getComputedStyle(element).fontSize)]);
                const labelSize = parseFloat(getComputedStyle(document.querySelector('.age-reduction-td'), '::before').fontSize);
                for (const [element, size] of sizes) element.style.setProperty('font-size', `${size * 2}px`, 'important');
                const style = document.createElement('style');
                style.textContent = `.leaderboard .age-reduction-td::before { font-size: ${labelSize * 2}px !important; }`;
                document.head.append(style);
                for (let i = 0; i < 4; i++) await new Promise(requestAnimationFrame);
                return parseFloat(getComputedStyle(name).fontSize) / before;
            }
            """);
        Assert.InRange(ratio, 1.999, 2.001);
        await AssertNameGeometryAsync(page);
        var name = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").First;
        await name.FocusAsync();
        await Assertions.Expect(name).ToBeFocusedAsync();
    }

    private static async Task AssertNameGeometryAsync(IPage page)
    {
        var problems = await page.EvaluateAsync<string[]>(
            """
            async () => {
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                const issues = [];
                const rows = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')]
                    .filter(row => row.getBoundingClientRect().height > 0);
                if (!rows.length) issues.push('No visible athlete rows.');
                for (const row of rows) {
                    const name = row.querySelector('.athlete-name'), identity = row.querySelector('.athlete-identity');
                    const box = name.getBoundingClientRect(), identityBox = identity.getBoundingClientRect();
                    const rowBox = row.getBoundingClientRect();
                    if (box.height < 44 || name.scrollWidth > name.clientWidth + 1)
                        issues.push(`${name.textContent}: clipped or undersized name target.`);
                    const parts = [...row.querySelectorAll('.rank-td, .portrait-wrapper, .athlete-identity, .age-reduction-td')]
                        .map(element => element.getBoundingClientRect()).filter(box => box.width && box.height);
                    for (let i = 0; i < parts.length; i++) for (let j = i + 1; j < parts.length; j++) {
                        const a = parts[i], b = parts[j];
                        if (a.left < b.right - 1 && a.right > b.left + 1 && a.top < b.bottom - 1 && a.bottom > b.top + 1)
                            issues.push(`${name.textContent}: row parts overlap.`);
                    }
                    const rank = row.querySelector('.rank');
                    const rankRange = document.createRange(); rankRange.selectNodeContents(rank);
                    const rankBox = rankRange.getBoundingClientRect();
                    if (rankBox.left < rowBox.left - 1 || rankBox.right > rowBox.right + 1)
                        issues.push(`${name.textContent}: rank text leaves the row.`);
                    for (const indicator of row.querySelectorAll('.rank-arrow, .new-bubble')) {
                        const indicatorBox = indicator.getBoundingClientRect();
                        if (rankBox.left < indicatorBox.right - 1 && rankBox.right > indicatorBox.left + 1
                            && rankBox.top < indicatorBox.bottom - 1 && rankBox.bottom > indicatorBox.top + 1)
                            issues.push(`${name.textContent}: rank text overlaps the portrait indicator.`);
                    }
                    const walker = document.createTreeWalker(name, NodeFilter.SHOW_TEXT);
                    for (let node; (node = walker.nextNode());) for (let i = 0; i < node.length; i++) {
                        if (/\s/.test(node.data[i])) continue;
                        const range = document.createRange(); range.setStart(node, i); range.setEnd(node, i + 1);
                        for (const glyph of range.getClientRects()) if (glyph.width > 0
                            && (glyph.left < identityBox.left - 1 || glyph.right > identityBox.right + 1
                                || glyph.top < box.top - 1 || glyph.bottom > box.bottom + 1
                                || glyph.left < -1 || glyph.right > innerWidth + 1))
                            issues.push(`${name.textContent}: a visible glyph leaves its target or viewport.`);
                    }
                }
                return [...new Set(issues)];
            }
            """);
        Assert.True(problems.Length == 0, string.Join(" ", problems.Take(12)));
    }
}
