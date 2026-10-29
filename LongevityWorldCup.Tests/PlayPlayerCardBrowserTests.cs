using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayPlayerCardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ProAthlete = """
        const athlete = {
            Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/browser-test-athlete',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
        };
        sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        localStorage.setItem('selectedAthleteName', athlete.Name);
        localStorage.setItem('hasApplication', 'true');
        """;

    [Theory]
    [InlineData("/select-athlete", "#athleteSelectionPanel", 1440, 900, false)]
    [InlineData("/select-athlete", "#athleteSelectionPanel", 1024, 768, true)]
    [InlineData("/dashboard", "#athleteDashboardPanel", 1440, 900, false)]
    [InlineData("/dashboard", "#athleteDashboardPanel", 1280, 720, true)]
    public async Task Desktop_PortraitHeadsOneCardThatHoldsTheScreensControls(string path, string panel, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        if (path == "/dashboard") await context.AddInitScriptAsync(ProAthlete);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync(
            "panel => !document.body.classList.contains('play-route-hydrating') && document.querySelector(panel)?.hidden === false && document.querySelector(panel + ' img.illustration')?.complete",
            panel);
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            panel => {
                const card = document.querySelector(panel);
                const box = card.getBoundingClientRect();
                const style = getComputedStyle(card);
                const frame = card.querySelector('.athlete-picture-frame').getBoundingClientRect();
                const controls = card.querySelector(panel === '#athleteSelectionPanel' ? '#playAthleteInput' : '.play-dashboard-actions .flow-action').getBoundingClientRect();
                return [parseFloat(style.borderTopWidth), style.backgroundColor === 'rgba(0, 0, 0, 0)' ? 0 : 1,
                    box.left, box.right, box.top, box.bottom, frame.left, frame.right, frame.top, frame.width, frame.height,
                    controls.left, controls.right, controls.top, controls.bottom];
            }
            """, panel);
        var scenario = $"{path} {width}x{height}";
        Assert.True(layout[0] >= 1 && layout[1] == 1, $"{scenario}: the screen should be one bordered card.");
        Assert.True(Math.Abs(layout[6] - layout[2]) <= 2 && Math.Abs(layout[7] - layout[3]) <= 2 && Math.Abs(layout[8] - layout[4]) <= 2,
            $"{scenario}: the portrait should be the card's full-width header.");
        Assert.True(layout[9] >= layout[10] * 1.4, $"{scenario}: the header portrait should be wide, not a {layout[9]:F0}x{layout[10]:F0} square.");
        Assert.True(layout[11] > layout[2] && layout[12] < layout[3] && layout[13] > layout[8] + layout[10] - 1 && layout[14] < layout[5],
            $"{scenario}: the screen's controls should sit in the card's body under the portrait.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selection_VisualAndKeyboardOrderAgreeAcrossThePlayerCardBoundaries(bool fallback)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 768, Height = 1024 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(ProAthlete);
        if (fallback) await context.AddInitScriptAsync("Element.prototype.moveBefore = undefined");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && !document.getElementById('playConfirmAthleteBtn').disabled");
        await page.EvaluateAsync("() => document.fonts.ready");
        var input = page.Locator("#playAthleteInput");
        await input.FocusAsync();
        await input.EvaluateAsync("e => e.setSelectionRange(2, 7)");

        foreach (var (width, height) in new[] { (768, 1024), (760, 1024), (761, 1024), (769, 1024), (768, 599), (768, 600), (1024, 768) })
        {
            await page.SetViewportSizeAsync(width, height);
            await page.EvaluateAsync("async () => { window.LwcFlowActionDock.refreshNow(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
            await Assertions.Expect(input).ToBeFocusedAsync();
            Assert.Equal("Browser Test Athlete", await input.InputValueAsync());
            Assert.Equal(new[] { 2, 7 }, await input.EvaluateAsync<int[]>("e => [e.selectionStart, e.selectionEnd]"));
            Assert.True(await page.EvaluateAsync<bool>("""
                () => {
                    const buttons = [...document.querySelectorAll('.play-athlete-actions > .flow-action')];
                    const visualOrder = [...buttons].sort((a, b) => {
                        const x = a.getBoundingClientRect(), y = b.getBoundingClientRect();
                        return Math.abs(x.top - y.top) > 2 ? x.top - y.top : x.left - y.left;
                    });
                    return buttons.every((button, i) => button === visualOrder[i]);
                }
                """), $"{width}x{height}: keyboard order must follow visual order.");
        }

        await page.SetViewportSizeAsync(768, 1024);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await input.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#playSelectionBackBtn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#athleteDashboardPanel")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/dashboard", 480, 640)]
    [InlineData("/dashboard", 800, 200)]
    [InlineData("/dashboard", 768, 768)]
    [InlineData("/select-athlete", 480, 640)]
    [InlineData("/select-athlete", 800, 200)]
    [InlineData("/select-athlete", 768, 768)]
    public async Task PortraitHeader_FitsTheWholeSourceImage(string path, int imageWidth, int imageHeight)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(ProAthlete + """
            const selected = JSON.parse(sessionStorage.getItem('selectedAthlete'));
            selected.ProfilePic = '/player-card-shape.svg';
            sessionStorage.setItem('selectedAthlete', JSON.stringify(selected));
            """);
        await context.RouteAsync("**/player-card-shape.svg", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml",
            Body = $"<svg xmlns='http://www.w3.org/2000/svg' width='{imageWidth}' height='{imageHeight}'><rect width='100%' height='100%' fill='teal'/></svg>"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelector('.play-hub-panel:not([hidden]) img.illustration')?.naturalWidth > 0");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const image = document.querySelector('.play-hub-panel:not([hidden]) img.illustration');
                const rect = image.getBoundingClientRect(), style = getComputedStyle(image);
                const ratios = [rect.width / image.naturalWidth, rect.height / image.naturalHeight];
                const scale = style.objectFit === 'contain' ? Math.min(...ratios) : Math.max(...ratios);
                return image.naturalWidth * scale <= rect.width + 1
                    && image.naturalHeight * scale <= rect.height + 1;
            }
            """), $"{path}: the {imageWidth}x{imageHeight} portrait must fit without cropping its source framing.");
    }

    [Fact]
    public async Task InlineCommandGrid_ReflowsLongWordsWithIconsAtDoubleTextSize()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 1600 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(ProAthlete);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        var scale = await page.EvaluateAsync<double>("""
            async () => {
                await document.fonts.ready;
                const label = document.querySelector('#athleteDashboardActions .flow-action__label');
                const before = parseFloat(getComputedStyle(label).fontSize);
                label.textContent = 'Longevitymaxxing Championship';
                const sizes = [...document.body.querySelectorAll('*')].filter(e => e instanceof HTMLElement)
                    .map(e => [e, parseFloat(getComputedStyle(e).fontSize)]);
                for (const [e, size] of sizes) e.style.setProperty('font-size', `${size * 2}px`, 'important');
                window.LwcFlowActionDock.refreshNow();
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                return parseFloat(getComputedStyle(label).fontSize) / before;
            }
            """);
        Assert.InRange(scale, 1.99, 2.01);
        Assert.False(await page.Locator("#athleteDashboardActions").EvaluateAsync<bool>("e => e.classList.contains('flow-action-stack--docked')"));
        foreach (var button in await page.Locator("#athleteDashboardActions .flow-action").AllAsync())
        {
            Assert.True(await button.EvaluateAsync<bool>("""
                button => {
                    const box = button.getBoundingClientRect(), icon = button.querySelector('i').getBoundingClientRect();
                    const range = document.createRange(); range.selectNodeContents(button.querySelector('.flow-action__label'));
                    const glyphs = [...range.getClientRects()].filter(r => r.width && r.height);
                    const inside = r => r.left >= box.left - 1 && r.right <= box.right + 1 && r.top >= box.top - 1 && r.bottom <= box.bottom + 1;
                    return inside(icon) && icon.width > 0 && icon.height > 0 && glyphs.length > 0
                        && glyphs.every(r => inside(r) && !(r.left < icon.right - 1 && r.right > icon.left + 1 && r.top < icon.bottom - 1 && r.bottom > icon.top + 1));
                }
                """), "Each enlarged command must keep its actual text and icon inside the button without overlap.");
        }
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }
}
