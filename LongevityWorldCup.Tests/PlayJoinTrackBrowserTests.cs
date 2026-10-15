using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayJoinTrackBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ReturningAmateur = """
        const athlete = {
            Name: 'Browser Test Athlete',
            DisplayName: 'Browser Test Athlete',
            PersonalLink: 'https://example.test/browser-test-athlete',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
            Biomarkers: []
        };
        window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        window.localStorage.setItem('selectedAthleteName', athlete.Name);
        window.localStorage.setItem('hasApplication', 'true');
        window.localStorage.setItem('gmaHasPerfectGuess', '1');
        """;

    [Theory]
    [InlineData(1440, 900, false, false)]
    [InlineData(1440, 900, true, false)]
    [InlineData(1280, 720, false, true)]
    [InlineData(768, 1024, true, true)]
    public async Task DesktopTrackCards_LineUpTheirBiomarkersAndActions(int width, int height, bool openAmateurList, bool withDiscount)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (withDiscount) await context.AddInitScriptAsync(ReturningAmateur);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinStartAmateurBtn')?.disabled && !document.getElementById('joinGoProButton')?.disabled");
        if (withDiscount)
            await Assertions.Expect(page.Locator("#joinProDiscountNote")).ToBeVisibleAsync();
        if (openAmateurList)
        {
            await page.Locator(".play-join-card:not(.play-join-card--pro) summary").ClickAsync();
            await Assertions.Expect(page.Locator(".play-join-card:not(.play-join-card--pro) details")).ToHaveAttributeAsync("open", "");
        }

        var rows = await page.EvaluateAsync<double[]>(
            """
            () => {
                const [amateur, pro] = document.querySelectorAll('.play-join-card');
                const top = (card, selector) => card.querySelector(selector).getBoundingClientRect().top;
                return [
                    amateur.getBoundingClientRect().top, pro.getBoundingClientRect().top,
                    top(amateur, '.play-join-card-actions .flow-action'), top(pro, '.play-join-card-actions .flow-action'),
                    amateur.getBoundingClientRect().bottom, pro.getBoundingClientRect().bottom,
                    top(amateur, 'summary'), top(pro, 'summary')
                ];
            }
            """);
        var scenario = $"{width}x{height}, list {(openAmateurList ? "open" : "closed")}, discount {withDiscount}";
        var sideBySide = Math.Abs(rows[0] - rows[1]) <= 1;
        if (width >= 960) Assert.True(sideBySide, $"{scenario}: the track cards should sit side by side.");
        if (sideBySide)
        {
            Assert.True(Math.Abs(rows[2] - rows[3]) <= 1, $"{scenario}: the track actions do not line up ({rows[2]} vs {rows[3]}).");
            Assert.True(Math.Abs(rows[4] - rows[5]) <= 1, $"{scenario}: the track cards end at different heights ({rows[4]} vs {rows[5]}).");
            if (!openAmateurList)
                Assert.True(Math.Abs(rows[6] - rows[7]) <= 1, $"{scenario}: the biomarker lists do not line up ({rows[6]} vs {rows[7]}).");
        }
        await ExpectNoHorizontalOverflowAsync(page);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task EnlargedText_KeepsTrackNamesAndDetailsWhole(int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForSelectorAsync(".play-join-title");
        await page.EvaluateAsync("() => { document.documentElement.style.fontSize = '200%'; }");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        // Phones keep room for the prize ribbon beside the track name, so only their values are checked.
        var selector = width >= 600 ? ".play-join-card .track-name, .play-join-meta dd" : ".play-join-meta dd";
        var splitWords = await page.EvaluateAsync<string[]>(
            """
            selector => {
                const split = [];
                for (const node of document.querySelectorAll(selector)) {
                    for (const word of node.textContent.trim().split(/\s+/)) {
                        const walker = document.createTreeWalker(node, NodeFilter.SHOW_TEXT);
                        let text;
                        while ((text = walker.nextNode())) {
                            const index = text.textContent.indexOf(word);
                            if (index < 0) continue;
                            const range = document.createRange();
                            range.setStart(text, index);
                            range.setEnd(text, index + word.length);
                            const lines = new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size;
                            if (lines > 1) split.push(word);
                            break;
                        }
                    }
                }
                return split;
            }
            """, selector);
        Assert.True(splitWords.Length == 0, $"{width}px at 200%: words split across lines: {string.Join(", ", splitWords)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task BiomarkerLists_OpenAndCloseFromTheKeyboard()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForSelectorAsync(".play-join-card--pro summary");

        var summary = page.Locator(".play-join-card--pro summary");
        var details = page.Locator(".play-join-card--pro details");
        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(details).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(details.Locator("li")).ToHaveCountAsync(22);
        await Assertions.Expect(details.Locator("li").Last).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(details).Not.ToHaveAttributeAsync("open", "");
        var outline = await summary.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle");
        Assert.NotEqual("none", outline);
    }
}
