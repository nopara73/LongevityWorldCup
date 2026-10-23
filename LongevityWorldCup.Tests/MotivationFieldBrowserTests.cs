using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class MotivationFieldBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Saved = "Training for a longer, healthier life.";
    private const string Long = "I want to see what is possible in feeling the most energetic and excited about life. Following the status quo and the herd is not an option. I measure, I adjust, I compete, and I share what I learn so others can take back their biological age too.";

    [Theory]
    [InlineData(true, 390, 844)]
    [InlineData(false, 390, 844)]
    [InlineData(true, 1440, 900)]
    [InlineData(false, 1440, 900)]
    public async Task FocusedMotivation_DoesNotUndoManualScrolling(bool application, int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var (page, field) = await OpenAsync(context, application);
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await SettleAsync(page);
        // The progress bar deliberately performs a one-time scroll after 500ms.
        // Exercise the user's gesture after that startup behavior has finished.
        await page.WaitForTimeoutAsync(600);
        await field.ClickAsync();
        await SettleAsync(page);
        await page.EvaluateAsync("() => scrollTo({top:0, behavior:'instant'})");
        await SettleAsync(page);
        await SettleAsync(page);
        await Assertions.Expect(field).ToBeFocusedAsync();
        Assert.InRange(await page.EvaluateAsync<double>("() => scrollY"), 0, 1);
    }

    [Theory]
    [InlineData(false, 390, 844)]
    [InlineData(true, 390, 844)]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 1440, 900)]
    public async Task ManualScroll_BeforeQueuedFocusClearanceFinishes_TakesPrecedence(bool application, int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var (page, field) = await OpenAsync(context, application);
        await field.ClickAsync();
        await SettleAsync(page);
        await field.EvaluateAsync("""
            field => {
                window.LwcFlowActionDock.ensureClear(field, {behavior: 'auto'});
                // Run the gesture before the deferred reconciliation, without
                // depending on a cross-process request winning the frame race.
                scrollTo({top: 0, behavior: 'instant'});
            }
            """);
        await SettleAsync(page);
        await SettleAsync(page);
        await Assertions.Expect(field).ToBeFocusedAsync();
        Assert.InRange(await page.EvaluateAsync<double>("() => scrollY"), 0, 1);
    }

    [Theory]
    [InlineData(false, 390, 844)]
    [InlineData(true, 390, 844)]
    [InlineData(false, 1440, 900)]
    [InlineData(true, 1440, 900)]
    public async Task ManualScroll_WithinAnAlreadyCompletedClearancePath_TakesPrecedence(bool application, int width, int height)
    {
        await using var context = await CreateContextAsync(width, height);
        var (page, field) = await OpenAsync(context, application);
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await SettleAsync(page);
        await field.ClickAsync();
        await SettleAsync(page);
        var positions = await field.EvaluateAsync<double[]>("""
            field => {
                scrollTo({top: 0, behavior: 'instant'});
                window.LwcFlowActionDock.ensureClear(field, {behavior: 'auto'});
                const cleared = scrollY;
                const manual = Math.floor(cleared / 2);
                scrollTo({top: manual, behavior: 'instant'});
                return [cleared, manual];
            }
            """);
        Assert.True(positions[0] > 2, "The immediate clearance must actually scroll before the manual move.");
        Assert.InRange(positions[1], 1, positions[0] - 1);
        await SettleAsync(page);
        await SettleAsync(page);
        await Assertions.Expect(field).ToBeFocusedAsync();
        Assert.InRange(await page.EvaluateAsync<double>("() => scrollY"), positions[1] - 1, positions[1] + 1);
    }

    [Fact]
    public async Task DeferredClearance_AllowsItsOwnSmoothScrollToFinish()
    {
        await using var context = await CreateContextAsync(390, 844);
        var (page, field) = await OpenAsync(context, false);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await SettleAsync(page);
        await field.ClickAsync();
        await SettleAsync(page);
        await SettleAsync(page);
        await field.EvaluateAsync("""
            field => {
                scrollTo({top: 0, behavior: 'instant'});
                window.LwcFlowActionDock.ensureClear(field, {behavior: 'smooth'});
            }
            """);
        await page.WaitForFunctionAsync("""
            () => {
                const field = document.getElementById('whyDisplayInput').getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return scrollY > 0 && field.bottom <= (dock?.top ?? innerHeight);
            }
            """);
        await ExpectFieldClearAsync(page, field);
        await Assertions.Expect(field).ToHaveValueAsync(Saved);
    }

    [Theory]
    [InlineData(false, 1440, 900, false)]
    [InlineData(true, 1440, 900, true)]
    [InlineData(false, 768, 1024, true)]
    [InlineData(true, 768, 1024, false)]
    [InlineData(false, 390, 844, false)]
    [InlineData(true, 390, 844, true)]
    [InlineData(false, 320, 640, true)]
    [InlineData(true, 320, 640, false)]
    public async Task TextChanges_GrowAndShrinkWithoutLosingContent(bool application, int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var (page, field) = await OpenAsync(context, application);
        await field.FillAsync("");
        await SettleAsync(page);
        var emptyHeight = (await field.BoundingBoxAsync())!.Height;
        // Force more than the minimum three lines at both card widths; a
        // paragraph that fits in three lines must not require artificial growth.
        var multiline = string.Join("\n", Enumerable.Repeat("A reason to keep training.", 6));
        foreach (var text in new[] { "A short motivation.", Saved, Long, multiline })
        {
            await field.FillAsync(text);
            await SettleAsync(page);
            await Assertions.Expect(field).ToHaveValueAsync(text);
            Assert.True(await field.EvaluateAsync<bool>("field => field.scrollHeight <= field.clientHeight + 1"));
            if (text == multiline) Assert.True((await field.BoundingBoxAsync())!.Height > emptyHeight + 1);
        }
        await field.FillAsync("Short again.");
        await SettleAsync(page);
        Assert.InRange((await field.BoundingBoxAsync())!.Height, emptyHeight - 1, emptyHeight + 1);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false, 1440, 900, 100)]
    [InlineData(true, 1440, 900, 100)]
    [InlineData(false, 844, 390, 100)]
    [InlineData(true, 844, 390, 100)]
    [InlineData(true, 844, 390, 200)]
    [InlineData(true, 390, 844, 200)]
    [InlineData(true, 1440, 900, 200)]
    public async Task PastedMultilineText_AndKeyboardNavigation_StayClearOfDockedActions(bool application, int width, int height, int textPercent)
    {
        await using var context = await CreateContextAsync(width, height);
        var (page, field) = await OpenAsync(context, application);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await SettleAsync(page);
        var text = string.Join('\n', Enumerable.Range(1, 20).Select(index => $"Line {index}"));
        await field.FillAsync("");
        await field.ClickAsync();
        // One native insertion reproduces a paste, including the first docking
        // transition. Typing one line at a time conceals that race.
        await page.Keyboard.InsertTextAsync(text);
        await SettleAsync(page);
        await ExpectFieldClearAsync(page, field);
        Assert.True(await field.EvaluateAsync<bool>("field => field.scrollHeight > field.clientHeight + 1 && field.scrollTop > 0"));
        foreach (var key in new[] { "Control+Home", "ArrowDown", "Control+End" })
        {
            await field.PressAsync(key);
            await SettleAsync(page);
            await ExpectFieldClearAsync(page, field, caretOnly: true);
        }
        await Assertions.Expect(field).ToHaveValueAsync(text);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardOcclusion_LeavesBothEndsAndTheCounterReachable(bool application)
    {
        await using var context = await CreateContextAsync(390, 844);
        await context.AddInitScriptAsync("""
            const viewport = new EventTarget();
            Object.assign(viewport, {height: innerHeight, offsetTop: 0});
            Object.defineProperty(window, 'visualViewport', {configurable: true, value: viewport});
            window.reviewKeyboardHeight = height => {
                viewport.height = height;
                viewport.dispatchEvent(new Event('resize'));
            };
            """);
        var (page, field) = await OpenAsync(context, application);
        // The mobile viewport meta tag is parsed after init scripts. Establish
        // the unobscured baseline after that layout, before focusing the field.
        await page.EvaluateAsync("() => { document.activeElement?.blur(); window.reviewKeyboardHeight(innerHeight); }");
        await SettleAsync(page);
        await field.FillAsync(string.Join('\n', Enumerable.Range(1, 20).Select(index => $"Line {index}")));
        await page.EvaluateAsync("() => window.reviewKeyboardHeight(280)");
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('flow-input-keyboard-open')");
        await SettleAsync(page);
        await SettleAsync(page);
        await ExpectFieldClearAsync(page, field);
        await field.PressAsync("Control+Home");
        await page.Keyboard.InsertTextAsync("X");
        await SettleAsync(page);
        await ExpectFieldClearAsync(page, field, caretOnly: true);
    }

    [Fact]
    public async Task EditDraft_RestoreResetUndoAndReload_ResizeToTheRestoredValue()
    {
        await using var context = await CreateContextAsync(390, 844);
        var (page, field) = await OpenAsync(context, false);
        var savedHeight = (await field.BoundingBoxAsync())!.Height;
        await field.FillAsync(Long);
        await page.ReloadAsync();
        await ReadyAsync(page);
        await Assertions.Expect(field).ToHaveValueAsync(Long);
        await page.Locator("#restoreWhyDisplayBtn").ClickAsync();
        await Assertions.Expect(field).ToHaveValueAsync(Saved);
        await SettleAsync(page);
        Assert.InRange((await field.BoundingBoxAsync())!.Height, savedHeight - 1, savedHeight + 1);
        await field.FillAsync(Long);
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await Assertions.Expect(field).ToHaveValueAsync(Saved);
        await SettleAsync(page);
        Assert.InRange((await field.BoundingBoxAsync())!.Height, savedHeight - 1, savedHeight + 1);
        await page.Locator("#undoProfileDraftButton").PressAsync("Enter");
        await Assertions.Expect(field).ToHaveValueAsync(Long);
        await SettleAsync(page);
        Assert.True((await field.BoundingBoxAsync())!.Height > savedHeight + 1);
    }

    [Fact]
    public async Task Application_LimitsValidationAndSavedDraft_SurviveResizing()
    {
        await using var context = await CreateContextAsync(390, 844);
        var (page, field) = await OpenAsync(context, true, fake: false);
        await Assertions.Expect(field).ToHaveAttributeAsync("minlength", "30");
        await Assertions.Expect(field).ToHaveAttributeAsync("maxlength", "250");
        await field.FillAsync("Too short.");
        await field.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#whyError")).ToContainTextAsync("between 30 and 250");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        var maximum = Long.PadRight(250, '!');
        await field.FillAsync(maximum);
        await field.PressAsync("End");
        await page.Keyboard.InsertTextAsync("extra");
        await Assertions.Expect(field).ToHaveValueAsync(maximum);
        await Assertions.Expect(page.Locator("#whyCharCounter")).ToHaveTextAsync("250/250");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
        await page.Locator("#nextButton").ClickAsync();
        await page.Locator("#backButton").ClickAsync();
        await Assertions.Expect(field).ToHaveValueAsync(maximum);
        await page.ReloadAsync();
        await ReadyAsync(page);
        await Assertions.Expect(field).ToHaveValueAsync(maximum);
        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '2'");
        await SettleAsync(page);
        Assert.True(await field.EvaluateAsync<bool>("field => field.scrollHeight <= field.clientHeight + 1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedSizing_RetainsTheScrollableRowsFallback(bool application)
    {
        await using var context = await CreateContextAsync(390, 844);
        await context.RouteAsync("**/css/aesthetic-system.css*", async route =>
        {
            var response = await route.FetchAsync();
            // Exercise the browser's real conditional-cascade fallback without
            // depending on an obsolete browser binary in CI.
            var css = (await response.TextAsync()).Replace(
                "@supports (field-sizing: content)", "@supports (field-sizing: unsupported-review-value)");
            await route.FulfillAsync(new() { Response = response, Body = css });
        });
        var (page, field) = await OpenAsync(context, application);
        await field.FillAsync("");
        await SettleAsync(page);
        var emptyHeight = (await field.BoundingBoxAsync())!.Height;
        await field.FillAsync(Long);
        await SettleAsync(page);
        Assert.InRange((await field.BoundingBoxAsync())!.Height, emptyHeight - 1, emptyHeight + 1);
        Assert.True(await field.EvaluateAsync<bool>("field => field.scrollHeight > field.clientHeight + 1"));
        await field.PressAsync("Control+End");
        Assert.True(await field.EvaluateAsync<bool>("field => field.selectionEnd === field.value.length && field.scrollTop > 0"));
        await Assertions.Expect(field).ToHaveAttributeAsync("rows", application ? "5" : "3");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task ExpectFieldClearAsync(IPage page, ILocator field, bool caretOnly = false)
    {
        await Assertions.Expect(field).ToBeFocusedAsync();
        var bounds = await field.EvaluateAsync<double[]>("""
            (field, caretOnly) => {
                const rect = field.getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                let top = rect.top, bottom = rect.bottom;
                if (!caretOnly && field.id === 'why') {
                    const counter = document.getElementById('whyCharCounter').getBoundingClientRect();
                    bottom = Math.max(bottom, counter.bottom);
                }
                if (caretOnly) {
                    // A textarea's value is not a DOM text node. Mirror its
                    // computed text layout to measure the active caret line.
                    const style = getComputedStyle(field), mirror = document.createElement('div');
                    for (const property of style) mirror.style.setProperty(property, style.getPropertyValue(property));
                    Object.assign(mirror.style, {position:'fixed', left:'-10000px', top:'0', margin:'0',
                        width:`${rect.width}px`, height:'auto', minHeight:'0', maxHeight:'none',
                        overflow:'visible', whiteSpace:'pre-wrap', overflowWrap:'break-word'});
                    mirror.textContent = field.value.slice(0, field.selectionEnd);
                    const marker = document.createElement('span'); marker.textContent = '\u200b'; mirror.append(marker);
                    document.body.append(mirror);
                    const line = marker.getBoundingClientRect(), origin = mirror.getBoundingClientRect();
                    top = rect.top + line.top - origin.top - field.scrollTop;
                    bottom = rect.top + line.bottom - origin.top - field.scrollTop;
                    mirror.remove();
                }
                const viewport = window.visualViewport;
                const stickyBottoms = ['site-sticky-header', 'site-sticky-progress'].map(id => document.getElementById(id))
                    .filter(bar => bar?.classList.contains('visible') && bar.getBoundingClientRect().width > 0)
                    .map(bar => bar.getBoundingClientRect().bottom);
                return [top, bottom, Math.min((viewport?.offsetTop ?? 0) + (viewport?.height ?? innerHeight), dock?.top ?? innerHeight),
                    Math.max(viewport?.offsetTop ?? 0, ...stickyBottoms)];
            }
            """, caretOnly);
        Assert.True(bounds[0] >= bounds[3] - 1 && bounds[1] <= bounds[2] + 1,
            $"Focused motivation spans {bounds[0]}–{bounds[1]}px; usable viewport spans {bounds[3]}–{bounds[2]}px.");
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = width < 600,
            HasTouch = width < 960 || height < 480
        });
        await context.AddInitScriptAsync("""
            if (!sessionStorage.getItem('selectedAthlete')) {
                const athlete = {Name:'Alex Morgan', DisplayName:'Alex Morgan',
                    ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg', Division:"Men's",
                    Flag:'United Kingdom', PersonalLink:'https://example.com', MediaContact:'press@example.com',
                    Why:'Training for a longer, healthier life.', Biomarkers:[]};
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name);
                localStorage.setItem('hasApplication', 'true');
            }
            """);
        return context;
    }

    private static async Task<(IPage Page, ILocator Field)> OpenAsync(IBrowserContext context, bool application, bool fake = true)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(application ? (fake ? "/apply?fake=1" : "/apply") : "/edit-profile");
        await ReadyAsync(page);
        if (application)
        {
            if (!fake)
            {
                await page.Locator("#name").FillAsync("Fixture Athlete");
                await page.Locator("#division").SelectOptionAsync("Open");
                await page.Locator("#flag").FillAsync("United Kingdom");
            }
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            await page.Locator("#nextButton").ClickAsync();
            await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '2'");
        }
        else await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync(Saved);
        await SettleAsync(page);
        return (page, page.Locator(application ? "#why" : "#whyDisplayInput"));
    }

    private static Task ReadyAsync(IPage page) => page.WaitForFunctionAsync("""
        () => window.modulesReady && !document.documentElement.hasAttribute('data-initial-view')
            && (document.getElementById('name')?.dataset.stage1ValidityListener === 'true'
                || document.getElementById('mediaContactInput')?.value === 'press@example.com')
        """);

    private static Task SettleAsync(IPage page) => page.EvaluateAsync("""
        async () => {
            await document.fonts.ready;
            window.LwcFlowActionDock.refreshNow();
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        }
        """);
}
