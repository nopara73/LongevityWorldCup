using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationNextStepsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about", false)]
    [InlineData("/about", true)]
    [InlineData("/history", false)]
    [InlineData("/history", true)]
    [InlineData("/ruleset", false)]
    [InlineData("/ruleset", true)]
    public async Task NextSteps_KeepSingleWholeTileLinksAndReadableBalancedRows(string path, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var links = page.Locator("#where-to-go-next + ul > li > a");
        var destinations = Destinations(path);
        Assert.Equal(destinations.Select(d => d.Href),
            await links.EvaluateAllAsync<string[]>("links => links.map(a => a.getAttribute('href'))"));
        for (var i = 0; i < destinations.Length; i++)
        {
            await Assertions.Expect(links.Nth(i)).ToHaveAccessibleNameAsync(destinations[i].Name);
            var external = destinations[i].Href.StartsWith("https://", StringComparison.Ordinal);
            Assert.Equal(external ? "_blank" : null, await links.Nth(i).GetAttributeAsync("target"));
            if (external) Assert.Contains("noopener", await links.Nth(i).GetAttributeAsync("rel"));
        }

        foreach (var percent in new[] { 100, 200, 100 })
        {
            await page.EvaluateAsync("p => document.documentElement.style.fontSize = `${p}%`", percent);
            foreach (var width in new[] { 320, 390, 600, 601, 768, 844, 1024, 1440, 390 })
            {
                await page.SetViewportSizeAndWaitForLayoutAsync(width, width == 844 ? 390 : 900);
                await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
                Assert.Equal(16 * percent / 100d,
                    await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)"));
                var problems = await page.EvaluateAsync<string[]>(TileProblems);
                Assert.True(problems.Length == 0, $"{path}, {width}px, {percent}% text, dark={dark}: {string.Join("; ", problems)}");
            }
        }

        await links.First.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        foreach (var destination in destinations)
        {
            await page.Keyboard.PressAsync("Tab");
            var link = links.Filter(new() { HasText = destination.Name });
            await Assertions.Expect(link).ToBeFocusedAsync();
            Assert.True(await link.EvaluateAsync<bool>(
                "a => a.matches(':focus-visible') && parseFloat(getComputedStyle(a, '::after').outlineWidth) >= 3 && getComputedStyle(a, '::after').outlineStyle === 'solid'"));
        }
    }

    [Theory]
    [InlineData("/about", 1440)]
    [InlineData("/history", 1440)]
    [InlineData("/ruleset", 1440)]
    [InlineData("/about", 390)]
    [InlineData("/history", 390)]
    [InlineData("/ruleset", 390)]
    public async Task NextSteps_CardCornersNavigateToTheOriginalDestinations(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = width == 390
        });
        foreach (var destination in Destinations(path).Where(d => d.Href.StartsWith("https://", StringComparison.Ordinal)))
            await context.RouteAsync(destination.Href, route => route.FulfillAsync(new() { ContentType = "text/html", Body = "<p>Destination fixture</p>" }));
        var page = await context.NewPageAsync();
        foreach (var destination in Destinations(path))
        {
            await NavigateAndSettleAsync(page, path);
            var tile = page.Locator("#where-to-go-next + ul > li").Filter(new() { HasText = destination.Name });
            await tile.ScrollIntoViewIfNeededAsync();
            var box = (await tile.BoundingBoxAsync())!;
            Func<Task> activate = width == 390
                ? () => page.Touchscreen.TapAsync(box.X + box.Width - 5, box.Y + box.Height - 5)
                : () => page.Mouse.ClickAsync(box.X + box.Width - 5, box.Y + box.Height - 5);
            if (destination.Href.StartsWith("https://", StringComparison.Ordinal))
            {
                var popup = await page.RunAndWaitForPopupAsync(activate);
                await popup.WaitForURLAsync(destination.Href);
                await popup.CloseAsync();
            }
            else
            {
                await activate();
                await page.WaitForURLAsync(new Uri(App.BaseAddress, destination.Href).ToString());
            }
        }
    }

    private static (string Href, string Name)[] Destinations(string path) => path switch
    {
        "/about" => [
            ("/leaderboard", "Leaderboard"), ("/ruleset", "Ruleset"), ("/history", "History"),
            ("https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL", "Immortal Combat podcast"),
            ("https://github.com/nopara73/LongevityWorldCup", "GitHub")],
        "/history" => [("/about", "About Longevity World Cup"), ("/ruleset", "Ruleset"), ("/leaderboard", "Leaderboard")],
        "/ruleset" => [("/about", "About Longevity World Cup"), ("/history", "History of Longevity as a Sport"), ("/leaderboard", "Leaderboard")],
        _ => throw new ArgumentOutOfRangeException(nameof(path))
    };

    private const string TileProblems = """
        () => {
            const issues = [];
            const list = document.querySelector('#where-to-go-next + ul');
            const listBox = list.getBoundingClientRect(), tiles = [...list.children];
            if (getComputedStyle(list).display !== 'flex') issues.push('destinations are not flexible tiles');
            const rows = new Map();
            for (const tile of tiles) {
                const box = tile.getBoundingClientRect();
                const row = rows.get(Math.round(box.top)) ?? [];
                row.push(box); rows.set(Math.round(box.top), row);
            }
            for (const tile of tiles) {
                tile.scrollIntoView({ block: 'center', behavior: 'instant' });
                const box = tile.getBoundingClientRect(), style = getComputedStyle(tile);
                const a = tile.querySelector(':scope > a'), linkStyle = getComputedStyle(a);
                const label = a.textContent.trim();
                if (tile.querySelectorAll('a').length !== 1) issues.push(`${label}: ambiguous destination`);
                if (parseFloat(style.borderTopWidth) < 1 || parseFloat(linkStyle.borderTopWidth) !== 0
                    || parseFloat(linkStyle.paddingLeft) !== 0 || linkStyle.backgroundColor !== 'rgba(0, 0, 0, 0)')
                    issues.push(`${label}: expected one tile shell, without a second padded link card`);
                if (box.height < 44) issues.push(`${label}: touch target under 44px`);
                if (!getComputedStyle(tile, '::before').content.endsWith('/ ""'))
                    issues.push(`${label}: arrow must be decorative`);
                const walker = document.createTreeWalker(tile, NodeFilter.SHOW_TEXT);
                for (let node; (node = walker.nextNode());) {
                    if (!node.textContent.trim()) continue;
                    const range = document.createRange(); range.selectNodeContents(node);
                    for (const rect of range.getClientRects()) {
                        if (rect.left < box.left + parseFloat(style.paddingLeft) - 1
                            || rect.right > box.right - parseFloat(style.paddingRight) + 1
                            || rect.top < box.top - 1 || rect.bottom > box.bottom + 1)
                            issues.push(`${label}: text escapes its content bounds`);
                    }
                }
                const hit = tile.getBoundingClientRect();
                for (const [x, y] of [[4,4], [hit.width-4,4], [4,hit.height-4], [hit.width-4,hit.height-4], [hit.width/2,hit.height/2]]) {
                    const target = document.elementFromPoint(hit.left+x, hit.top+y);
                    if (target?.closest('a') !== a) issues.push(`${label}: tile point ${x},${y} is not its link`);
                }
                if (style.transitionDuration !== '0s') issues.push(`${label}: reduced motion still animates`);
            }
            for (const row of rows.values()) {
                if (Math.abs(row[0].left - listBox.left) > 1 || Math.abs(row.at(-1).right - listBox.right) > 1)
                    issues.push('row does not span the full list');
                if (row.some(box => Math.abs(box.width - row[0].width) > 1 || Math.abs(box.height - row[0].height) > 1))
                    issues.push('row tiles are unbalanced');
            }
            if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page scrolls sideways');
            return [...new Set(issues)];
        }
        """;
}
