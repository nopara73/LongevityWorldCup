using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayAthleteSelectionCardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, false, 16, false)]
    [InlineData(390, 844, true, 16, false)]
    [InlineData(320, 640, false, 16, false)]
    [InlineData(1440, 900, false, 32, false)]
    [InlineData(390, 844, true, 32, false)]
    [InlineData(320, 640, false, 32, false)]
    [InlineData(740, 390, false, 32, false)]
    [InlineData(390, 844, true, 16, true)]
    [InlineData(740, 390, true, 16, true)]
    public async Task Selection_SearchIsAMagnifierLedFieldAsTallAsTheActions(int width, int height, bool dark, int fontSize, bool doubleInputText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
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
        if (doubleInputText)
            await input.EvaluateAsync("e => e.style.fontSize = parseFloat(getComputedStyle(e).fontSize) * 2 + 'px'");

        var field = await page.EvaluateAsync<string[]>(
            """
            () => {
                const input = document.getElementById('playAthleteInput');
                const box = input.getBoundingClientRect();
                const icon = document.querySelector('.play-athlete-search-icon');
                const iconBox = icon.getBoundingClientRect();
                const iconCentre = iconBox.top + iconBox.height / 2;
                return [String(box.height), getComputedStyle(icon, '::before').content,
                    String(Math.abs(iconCentre - (box.top + box.height / 2))),
                    String(box.left + parseFloat(getComputedStyle(input).paddingLeft) - iconBox.right)];
            }
            """);
        var scenario = $"{width}x{height}, root {fontSize}px, double input text {doubleInputText}";
        Assert.True(double.Parse(field[0], System.Globalization.CultureInfo.InvariantCulture) >= (height <= 480 ? 44 : 56),
            $"{scenario}: the athlete search should be as tall as the actions, not a {field[0]}px line.");
        Assert.True(field[1] == "\"\"", $"{scenario}: the athlete search should be led by the magnifier, not {field[1]}.");
        Assert.True(double.Parse(field[2], System.Globalization.CultureInfo.InvariantCulture) <= 2, $"{scenario}: the magnifier should sit centred in the field.");
        Assert.True(double.Parse(field[3], System.Globalization.CultureInfo.InvariantCulture) >= 4, $"{scenario}: typed names should start clear of the magnifier.");
        await Assertions.Expect(input).ToHaveAccessibleNameAsync("Athlete name");
        await Assertions.Expect(page.Locator("label[for=playAthleteInput]")).ToHaveClassAsync("visually-hidden");
        await Assertions.Expect(page.Locator(".play-athlete-search-icon")).ToHaveAttributeAsync("aria-hidden", "true");
        Assert.DoesNotContain("\uf002", await page.Locator(".play-autocomplete-container").AriaSnapshotAsync());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public async Task Selection_SearchIconRemainsCenteredWhenTheDirectoryErrorWraps(int fontSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 320, Height = 640 },
            ReducedMotion = ReducedMotion.Reduce, IsMobile = true, HasTouch = true
        });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { Status = 503, Body = "{}" }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
        await page.EvaluateAsync("() => document.fonts.ready");
        var positions = await page.EvaluateAsync<double[]>("""
            () => {
                const input = document.getElementById('playAthleteInput').getBoundingClientRect();
                const icon = document.querySelector('.play-athlete-search-icon').getBoundingClientRect();
                const error = document.getElementById('playAthleteError').getBoundingClientRect();
                return [Math.abs(icon.top + icon.height / 2 - input.top - input.height / 2), error.top - input.bottom];
            }
            """);
        Assert.InRange(positions[0], 0, 2);
        Assert.True(positions[1] >= 0, "The directory error must stay below the field.");
    }

    [Theory]
    [InlineData(1440, 900, false, 16)]
    [InlineData(390, 844, true, 16)]
    [InlineData(320, 640, false, 16)]
    [InlineData(1440, 900, false, 32)]
    [InlineData(390, 844, true, 32)]
    [InlineData(320, 640, false, 32)]
    public async Task Selection_KeepsOnePlayerCardWithTheNameAsItsCaption(int width, int height, bool dark, int fontSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await Assertions.Expect(input).ToHaveAccessibleNameAsync("Athlete name");
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('play-route-hydrating')");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
        await page.WaitForFunctionAsync("size => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - size) < 0.1", fontSize);
        // Rem-based padding starts its own reduced-motion transition after the
        // root font changes; measure the card only after that layout settles.
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForFiniteAnimationsAsync(page);

        const string measure = """
            () => {
                const card = document.querySelector('.play-athlete-card').getBoundingClientRect();
                const frame = document.getElementById('athleteSelectionPicture');
                const frameBox = frame.getBoundingClientRect();
                const title = document.getElementById('athleteSelectionTitle');
                const titleBox = title.getBoundingClientRect();
                const field = document.getElementById('playAthleteInput').getBoundingClientRect();
                return [card.width, card.height, frameBox.width, frameBox.height,
                    titleBox.top - frameBox.top, frameBox.bottom - titleBox.bottom,
                    parseFloat(getComputedStyle(frame).borderTopLeftRadius), Number(getComputedStyle(title).opacity),
                    field.width, card.bottom - titleBox.bottom, titleBox.top - frameBox.bottom];
            }
            """;
        var idle = await page.EvaluateAsync<double[]>(measure);
        Assert.InRange(Math.Abs(idle[0] - idle[2]), 0, 1);
        // Tablets and desktops head the player card with a wide portrait; phones keep the square.
        var wideCard = width >= 761 && height >= 600;
        if (wideCard) Assert.InRange(idle[2] / idle[3], 1.55, 1.65);
        else Assert.InRange(Math.Abs(idle[2] - idle[3]), 0, 1);
        var captionBelow = wideCard && fontSize == 32;
        Assert.True(idle[4] > 0 && Math.Abs(idle[9]) <= 1, $"{width}px: the title should caption the player card.");
        Assert.InRange(Math.Abs(captionBelow ? idle[10] : idle[5]), 0, 1);
        if (!wideCard) Assert.True(Math.Abs(idle[0] - idle[8]) <= 1, $"{width}px: the card ({idle[0]}px) should share the field's column ({idle[8]}px).");

        await input.FillAsync("a");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".autocomplete-items [role=option]").First).ToBeVisibleAsync();
        var searching = await page.EvaluateAsync<double[]>(measure);
        Assert.True(searching[0] < idle[0] * 0.6, $"{width}px: the card should step aside while matches are open.");
        Assert.InRange(Math.Abs(searching[2] - searching[3]), 0, 1);
        Assert.True(searching[6] < searching[2] / 4, $"{width}px: the card keeps its rounded-square shape instead of becoming a circle.");
        Assert.Equal(0, searching[7]);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Athlete selection", Exact = true })).ToHaveCountAsync(1);

        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        var name = await input.InputValueAsync();
        await Assertions.Expect(page.Locator("#athleteSelectionTitle")).ToHaveTextAsync(name);
        var chosen = await page.EvaluateAsync<double[]>(measure);
        Assert.InRange(Math.Abs(chosen[0] - idle[0]), 0, 1);
        Assert.True(Math.Abs(chosen[9]) <= 1 && chosen[7] == 1, $"{width}px: the chosen athlete's name should caption their portrait.");
        Assert.InRange(Math.Abs(captionBelow ? chosen[10] : chosen[5]), 0, 1);
    }

    [Theory]
    [InlineData(640, 360)]
    [InlineData(740, 390)]
    [InlineData(760, 480)]
    [InlineData(844, 390)]
    public async Task Selection_ShortLandscapeKeepsTheFieldAboveTheDockAndItsSearchHeadingAccessible(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        var input = page.GetByRole(AriaRole.Combobox, new() { Name = "Athlete name", Exact = true });
        await Assertions.Expect(input).ToBeEditableAsync();
        await page.WaitForFunctionAsync("""
            () => {
                const card = document.querySelector('.play-athlete-card').getBoundingClientRect();
                const input = document.getElementById('playAthleteInput').getBoundingClientRect();
                const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                return card.width <= 240 && card.bottom < input.top && input.top >= 0
                    && input.bottom < Math.min(actions.top, innerHeight);
            }
            """);
        await input.FillAsync("a");
        await page.GetByRole(AriaRole.Option).First.WaitForAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Athlete selection", Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(input).ToHaveAccessibleNameAsync("Athlete name");
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(320, 640)]
    [InlineData(390, 844)]
    public async Task Selection_LongCaptionAndActualDoubleSizedTextRemainReadable(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        const string name = "Alexandria Catherine Montgomery-Worthington";
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new[] { new { Name = name, ProfilePic = "/assets/favicon-512x512.png" } })
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await input.FillAsync("Alexandria");
        await page.GetByRole(AriaRole.Option).First.TapAsync();
        await Assertions.Expect(page.Locator("#athleteSelectionTitle")).ToHaveTextAsync(name);
        var normalSize = await page.Locator("#athleteSelectionTitle").EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        await page.Locator("#athleteSelectionTitle, #playAthleteInput, .play-athlete-actions .flow-action__label").EvaluateAllAsync<object>("""
            elements => {
                const sizes = elements.map(element => parseFloat(getComputedStyle(element).fontSize));
                elements.forEach((element, index) => element.style.fontSize = sizes[index] * 2 + 'px');
            }
            """);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await page.WaitForFunctionAsync("""
            expected => Math.abs(parseFloat(getComputedStyle(document.getElementById('athleteSelectionTitle')).fontSize) - expected) < 0.1
            """, normalSize * 2);
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const title = document.getElementById('athleteSelectionTitle');
                const box = title.getBoundingClientRect();
                const card = title.parentElement.getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(title);
                const issues = [];
                for (const fragment of range.getClientRects()) {
                    if (fragment.left < card.left - 1 || fragment.right > card.right + 1
                        || fragment.top < card.top - 1 || fragment.bottom > card.bottom + 1) issues.push('caption clipped');
                }
                if (title.scrollWidth > title.clientWidth + 1 || title.scrollHeight > title.clientHeight + 1) issues.push('caption overflow');
                if (getComputedStyle(title).color !== 'rgb(255, 255, 255)' || getComputedStyle(title).opacity !== '1') issues.push('caption contrast');
                if (box.bottom > card.bottom + 1) issues.push('caption outside card');
                for (const label of document.querySelectorAll('.play-athlete-actions .flow-action__label')) {
                    range.selectNodeContents(label);
                    const fragments = [...range.getClientRects()];
                    if (fragments.some(fragment => Math.abs(fragment.top - fragments[0].top) > 1)) issues.push(label.textContent + ': action label split');
                    const button = label.closest('button').getBoundingClientRect();
                    if (button.left < -1 || button.right > innerWidth + 1 || button.top < -1 || button.bottom > innerHeight + 1) issues.push('action outside viewport');
                }
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflow');
                return issues;
            }
            """);
        Assert.Empty(issues);
        await input.FocusAsync();
        await Assertions.Expect(input).ToHaveAccessibleNameAsync("Athlete name");
        await input.FillAsync("");
        await Assertions.Expect(page.Locator("#athleteSelectionTitle")).ToHaveTextAsync("Athlete selection");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData(ReducedMotion.Reduce)]
    [InlineData(ReducedMotion.NoPreference)]
    public async Task Selection_SearchReversalAndResizeRestoreTheSameCard(ReducedMotion motion)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = motion
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        if (motion == ReducedMotion.Reduce)
        {
            foreach (var selector in new[] { ".play-athlete-card", "#athleteSelectionTitle", ".play-autocomplete-wrapper" })
                await Assertions.Expect(page.Locator(selector)).ToHaveCSSAsync("transition-property", "none");
        }
        for (var index = 0; index < 3; index++)
        {
            await input.FillAsync("a");
            await page.GetByRole(AriaRole.Option).First.WaitForAsync();
            await input.PressAsync("Escape");
            await input.FillAsync("");
        }
        await page.SetViewportSizeAsync(390, 844);
        await input.FillAsync("a");
        await page.GetByRole(AriaRole.Option).First.WaitForAsync();
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        await page.WaitForFunctionAsync("""
            () => {
                const card = document.querySelector('.play-athlete-card').getBoundingClientRect();
                const input = document.getElementById('playAthleteInput').getBoundingClientRect();
                const title = document.getElementById('athleteSelectionTitle');
                return Math.abs(card.width - input.width) < 1 && getComputedStyle(title).opacity === '1'
                    && title.textContent === document.getElementById('playAthleteInput').value;
            }
            """);
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selection_FocusedConfirmSurvivesDockingAndUndocking(bool useFallback)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        if (useFallback)
            await context.AddInitScriptAsync("Element.prototype.moveBefore = undefined;");
        await context.AddInitScriptAsync("""
            window.selectionFocusTrace = [];
            for (const kind of ['focusin', 'focusout']) document.addEventListener(kind, event => {
                window.selectionFocusTrace.push({ kind, target: event.target?.id,
                    active: document.activeElement?.id, time: performance.now() });
            }, true);
            """);
        var page = await context.NewPageAsync();
        // Collections share Chromium and can open other tabs. Emulate document
        // focus without assigning focus to any control, as the clipboard tests do.
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setFocusEmulationEnabled", new() { ["enabled"] = true });
        await NavigateAndSettleAsync(page, "/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await input.FillAsync("a");
        await page.GetByRole(AriaRole.Option).First.WaitForAsync();
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        var confirm = page.Locator("#playConfirmAthleteBtn");
        await Assertions.Expect(confirm).ToBeFocusedAsync();
        foreach (var height in new[] { 480, 900, 480, 900 })
        {
            await page.SetViewportSizeAsync(1280, height);
            await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
            await page.WaitForFunctionAsync("""
                docked => document.querySelector('.play-athlete-actions').classList.contains('flow-action-stack--docked') === docked
                """, height == 480);
            await cdp.SendAsync("Emulation.setFocusEmulationEnabled", new() { ["enabled"] = true });
            try
            {
                await Assertions.Expect(confirm).ToBeFocusedAsync();
            }
            catch (PlaywrightException error)
            {
                var diagnostic = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({ url: location.href, viewport: [innerWidth, innerHeight],
                        documentFocused: document.hasFocus(), active: document.activeElement?.outerHTML,
                        trace: window.selectionFocusTrace,
                        controls: [...document.querySelectorAll('.play-athlete-actions, .play-athlete-actions > button')].map(element => {
                            const style = getComputedStyle(element);
                            return { id: element.id, classes: element.className, disabled: element.disabled,
                                display: style.display, visibility: style.visibility, opacity: style.opacity,
                                rect: element.getBoundingClientRect().toJSON() };
                        }) })
                    """);
                throw new Xunit.Sdk.XunitException($"Selection focus lost after resize to {height}px (fallback={useFallback}). {diagnostic}\n{error.Message}");
            }
        }
    }
}
