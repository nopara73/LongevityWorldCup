using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayAthleteSearchLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, 16, 3)]
    [InlineData(1280, 720, 16, 3)]
    [InlineData(390, 844, 16, 3)]
    [InlineData(320, 640, 16, 3)]
    [InlineData(820, 1180, 16, 3)]
    [InlineData(844, 390, 16, 1)]
    [InlineData(320, 480, 16, 1)]
    [InlineData(1440, 900, 32, 2)]
    [InlineData(390, 844, 32, 2)]
    [InlineData(320, 640, 32, 1)]
    public async Task AthleteSearch_ShowsEveryMatchWithItsPortraitAboveTheActions(int width, int height, int fontSize, int minimumVisible)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('play-route-hydrating')");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.WaitForFunctionAsync("size => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - size) < 0.1", fontSize);
        await WaitForFiniteAnimationsAsync(page);
        await input.FillAsync("a");
        await Assertions.Expect(page.Locator(".autocomplete-items [role=option]").First).ToBeVisibleAsync();
        await page.EvaluateAsync("async () => { window.LwcFlowActionDock?.refreshNow(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        await page.WaitForFunctionAsync("""
            () => {
                const list = document.querySelector('.autocomplete-items')?.getBoundingClientRect();
                const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                return list && list.height > 20 && list.top >= 0 && list.bottom <= Math.min(innerHeight, actions.top) - 1;
            }
            """);
        var problems = await page.EvaluateAsync<string[]>(
            """
            minimumVisible => {
                const issues = [];
                const list = document.querySelector('.autocomplete-items');
                const options = Array.from(list.querySelectorAll('[role=option]'));
                const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                const listBox = list.getBoundingClientRect();
                if (options.length < 3) issues.push(`only ${options.length} matches`);
                for (const option of options) {
                    const box = option.getBoundingClientRect();
                    const name = option.textContent.trim();
                    const thumb = option.querySelector('img');
                    if (!thumb) issues.push(`${name}: no portrait`);
                    else if (thumb.getAttribute('alt') !== '') issues.push(`${name}: portrait should be decorative`);
                    if (box.bottom > listBox.bottom + 1) continue; // scrolled inside the list
                    if (box.bottom > innerHeight + 1) issues.push(`${name}: below the screen`);
                    if (box.bottom > actions.top + 1 && box.top < actions.bottom - 1) issues.push(`${name}: hidden behind the actions`);
                }
                const visible = options.filter(option => {
                    const box = option.getBoundingClientRect();
                    return box.top >= listBox.top && box.bottom <= Math.min(listBox.bottom, innerHeight, actions.top) + 1;
                });
                if (visible.length < Math.min(minimumVisible, options.length)) issues.push(`only ${visible.length} matches visible`);
                return issues;
            }
            """, minimumVisible);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));

        var options = page.GetByRole(AriaRole.Option);
        var count = await options.CountAsync();
        var pageScroll = await page.EvaluateAsync<double>("scrollY");
        for (var index = 0; index < count; index++)
        {
            await input.PressAsync("ArrowDown");
            var option = options.Nth(index);
            await Assertions.Expect(option).ToHaveAttributeAsync("aria-selected", "true");
            var navigationScroll = await page.EvaluateAsync<double>("scrollY");
            Assert.True(Math.Abs(navigationScroll - pageScroll) <= 1,
                $"Match {index + 1} moved the page from {pageScroll} to {navigationScroll}.");
            // Popup sizing and dock callbacks can settle after keydown. Keep the
            // geometry and hit test together while the page itself stays still.
            await page.WaitForFunctionAsync("""
                option => {
                    const box = option.getBoundingClientRect();
                    const list = option.parentElement.getBoundingClientRect();
                    const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                    return box.top >= list.top - 1 && box.bottom <= Math.min(list.bottom, actions.top, innerHeight) + 1
                        && option.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2));
                }
                """, await option.ElementHandleAsync());
        }
        var selectedName = await options.Last.InnerTextAsync();
        await input.PressAsync("Enter");
        await Assertions.Expect(input).ToHaveValueAsync(selectedName);
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task AthleteSearch_ReservesAWholeWrappedOptionWhenNeitherSideInitiallyFits(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 640 },
            ReducedMotion = ReducedMotion.Reduce, IsMobile = true, HasTouch = true
        });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new[] { "Aaron", "AgatheL", "Alan V", "Alberto Pérez Sciú", "Alena" }
                .Select(name => new { Name = name, ProfilePic = "/assets/favicon-512x512.png" }))
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '32px'");
        await page.EvaluateAsync("() => document.fonts.ready");
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync("a");
        await page.GetByRole(AriaRole.Option).First.WaitForAsync();
        // This scroll position left only 136px above the field for a 146px
        // wrapped option. Both popup placement and keyboard scrolling must fit it.
        await page.EvaluateAsync("() => { scrollTo(0, 49); window.dispatchEvent(new Event('scroll')); }");
        await page.WaitForFunctionAsync("""
            minimum => {
                const list = document.querySelector('[role=listbox]').getBoundingClientRect();
                return [...document.querySelectorAll('[role=option]')].filter(option => {
                    const box = option.getBoundingClientRect();
                    return box.top >= list.top && box.bottom <= list.bottom + 1;
                }).length >= minimum;
            }
            """, width >= 360 ? 2 : 1);
        for (var index = 0; index < 5; index++)
        {
            await input.PressAsync("ArrowDown");
            await page.WaitForFunctionAsync("""
                () => {
                    const option = document.querySelector('[role=option][aria-selected=true]');
                    if (!option) return false;
                    const box = option.getBoundingClientRect();
                    const list = option.parentElement.getBoundingClientRect();
                    const field = document.getElementById('playAthleteInput').getBoundingClientRect();
                    const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                    return box.top >= list.top - 1 && box.bottom <= Math.min(list.bottom, actions.top, innerHeight) + 1
                        && field.top >= 0 && field.bottom < actions.top
                        && option.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2));
                }
                """);
        }
        await input.PressAsync("Enter");
        await Assertions.Expect(input).ToHaveValueAsync("Alena");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task AthleteSearch_RefitsAfterTextAndViewportChangesWithoutLosingTheActiveOption()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await input.FillAsync("a");
        await page.GetByRole(AriaRole.Option).First.WaitForAsync();
        await input.PressAsync("ArrowDown");
        var active = await input.GetAttributeAsync("aria-activedescendant");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '32px'");
        await page.SetViewportSizeAsync(320, 640);
        await page.WaitForFunctionAsync("""
            () => {
                const list = document.querySelector('.autocomplete-items')?.getBoundingClientRect();
                const input = document.querySelector('#playAthleteInput').getBoundingClientRect();
                const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                return list && list.height > 60 && list.top >= 0 && list.bottom < actions.top && input.bottom < actions.top;
            }
            """);
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-activedescendant", active!);
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AthleteSearch_PortraitsFallBackAndNamesRemainSafeAndAccessible()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        const string unsafeName = "Portrait <img src=x onerror=alert(1)> Élodie";
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new object[]
            {
                new { Name = "Preferred", DisplayName = unsafeName, ProfilePicLeaderboardThumb = "/assets/favicon-128x128.png", ProfilePicThumb = "/assets/favicon-512x512.png", ProfilePic = "/assets/favicon.ico" },
                new { Name = "Portrait Thumb", ProfilePicThumb = "/assets/favicon-128x128.png", ProfilePic = "/assets/favicon-512x512.png" },
                new { Name = "Portrait Full", ProfilePic = "/assets/favicon-512x512.png" },
                new { Name = "Portrait Missing" },
                new { Name = "Portrait Broken", ProfilePicLeaderboardThumb = "/broken-portrait.png" },
                new { Name = "Portrait Tiny", ProfilePicLeaderboardThumb = "data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=" }
            })
        }));
        await context.RouteAsync("**/broken-portrait.png", route => route.FulfillAsync(new() { Status = 404, Body = "" }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await input.FillAsync("portrait");
        var list = page.GetByRole(AriaRole.Listbox, new() { Name = "Athlete suggestions" });
        await Assertions.Expect(list.GetByRole(AriaRole.Option)).ToHaveCountAsync(5);
        await Assertions.Expect(list.GetByRole(AriaRole.Option).First).ToHaveAccessibleNameAsync(unsafeName);
        await Assertions.Expect(list.Locator("img")).ToHaveCountAsync(5);
        await Assertions.Expect(list.GetByRole(AriaRole.Option).First.Locator("strong")).ToHaveTextAsync("Portrait");
        var sources = await list.Locator("img").EvaluateAllAsync<string[]>("images => images.map(image => image.getAttribute('src'))");
        Assert.Contains("favicon-128x128.png", sources[0]);
        Assert.Contains("favicon-128x128.png", sources[1]);
        Assert.Contains("favicon-512x512.png", sources[2]);
        foreach (var name in new[] { "Portrait Missing", "Portrait Broken", "Portrait Tiny" })
        {
            await input.FillAsync(name);
            await page.WaitForFunctionAsync("""
                () => {
                    const image = document.querySelector('[role=option] img');
                    return image?.complete && image.naturalWidth > 16 && image.src.includes('play-athlete-placeholder.jpg');
                }
                """);
            await Assertions.Expect(page.GetByRole(AriaRole.Option)).ToHaveAccessibleNameAsync(name);
            await Assertions.Expect(page.GetByRole(AriaRole.Option).Locator("img")).ToHaveAttributeAsync("alt", "");
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task AthleteSearch_RetryDismissalTouchAndNavigationPreserveSelection()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            IsMobile = true, HasTouch = true, ReducedMotion = ReducedMotion.Reduce
        });
        var attempts = 0;
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            Status = ++attempts == 1 ? 503 : 200,
            ContentType = "application/json",
            Body = attempts == 1 ? "{}" : """[{"Name":"Touch Athlete","DisplayName":"Touch Athlete","ProfilePic":"/assets/favicon-512x512.png","Biomarkers":[]}]"""
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).TapAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToHaveCountAsync(0);
        await input.FillAsync("Touch");
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-expanded", "true");
        await input.FillAsync("");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await input.FillAsync("no match");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await input.FillAsync("Touch");
        await input.PressAsync("Escape");
        await Assertions.Expect(input).ToHaveValueAsync("Touch");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await input.FocusAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Touch Athlete", Exact = true }).TapAsync();
        await Assertions.Expect(input).ToHaveValueAsync("Touch Athlete");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        await page.Locator("#playConfirmAthleteBtn").TapAsync();
        await page.WaitForURLAsync("**/dashboard");
        Assert.Contains("Touch Athlete", await page.EvaluateAsync<string>("sessionStorage.getItem('selectedAthlete')"));
        await page.GoBackAsync();
        await Assertions.Expect(input).ToHaveValueAsync("Touch Athlete");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
    }
}
