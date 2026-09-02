using Microsoft.Playwright;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LongevitymaxxingChallengeBrowserTests
{
    [Fact]
    public async Task TimeZonePicker_NormalizesBrowserAliasesAndUsesOneFocusBoundary()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 450, Height = 800 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.RouteAsync(
            "**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var picker = page.Locator("[data-timezone-picker][data-select-id=\"lmxSignupTimeZone\"]");
        await Assertions.Expect(picker).ToHaveAttributeAsync(
            "data-wired",
            "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
        await page.Locator("#lmxSignupTimeZoneButton").ClickAsync();

        var search = page.Locator("#lmxSignupTimeZoneSearch");
        await search.WaitForAsync();
        await search.FocusAsync();
        var focusStyles = await search.EvaluateAsync<string[]>(
            """
            input => {
                const inputStyle = getComputedStyle(input);
                const wrapperStyle = getComputedStyle(input.closest('.lmx-timezone-search'));
                return [
                    inputStyle.borderTopWidth,
                    inputStyle.outlineStyle,
                    inputStyle.boxShadow,
                    inputStyle.backgroundColor,
                    inputStyle.borderRadius,
                    inputStyle.minHeight,
                    wrapperStyle.borderTopColor,
                    wrapperStyle.boxShadow,
                    wrapperStyle.backgroundColor
                ];
            }
            """);
        Assert.Equal("0px", focusStyles[0]);
        Assert.Equal("none", focusStyles[1]);
        Assert.Equal("none", focusStyles[2]);
        Assert.Equal("rgba(0, 0, 0, 0)", focusStyles[3]);
        Assert.Equal("0px", focusStyles[4]);
        Assert.Equal("42px", focusStyles[5]);
        Assert.NotEqual("rgba(0, 0, 0, 0)", focusStyles[6]);
        Assert.NotEqual("none", focusStyles[7]);
        Assert.NotEqual("rgba(0, 0, 0, 0)", focusStyles[8]);

        var geometry = await search.EvaluateAsync<double[]>(
            """
            input => {
                const inputRect = input.getBoundingClientRect();
                const wrapperRect = input.closest('.lmx-timezone-search').getBoundingClientRect();
                return [wrapperRect.left, inputRect.left, inputRect.right, wrapperRect.right];
            }
            """);
        Assert.True(geometry[1] >= geometry[0]);
        Assert.True(geometry[2] <= geometry[3]);

        var selectedOptionContentFits = await page.Locator(".lmx-timezone-option[aria-selected=\"true\"]")
            .EvaluateAsync<bool>(
                """
                option => {
                    const optionRect = option.getBoundingClientRect();
                    const metadataRect = option.querySelector('small').getBoundingClientRect();
                    return metadataRect.bottom <= optionRect.bottom;
                }
                """);
        Assert.True(selectedOptionContentFits);

        var preferredIds = new[]
        {
            "Africa/Asmara",
            "America/Argentina/Buenos_Aires",
            "America/Argentina/Catamarca",
            "America/Atikokan",
            "America/Argentina/Cordoba",
            "America/Nuuk",
            "America/Indiana/Indianapolis",
            "America/Argentina/Jujuy",
            "America/Kentucky/Louisville",
            "America/Argentina/Mendoza",
            "Asia/Kolkata",
            "Asia/Kathmandu",
            "Asia/Yangon",
            "Asia/Ho_Chi_Minh",
            "Atlantic/Faroe",
            "Europe/Kyiv",
            "Pacific/Kanton",
            "Pacific/Pohnpei",
            "Pacific/Chuuk"
        };
        var legacyIds = new[]
        {
            "Africa/Asmera",
            "America/Buenos_Aires",
            "America/Catamarca",
            "America/Coral_Harbour",
            "America/Cordoba",
            "America/Godthab",
            "America/Indianapolis",
            "America/Jujuy",
            "America/Louisville",
            "America/Mendoza",
            "Asia/Calcutta",
            "Asia/Katmandu",
            "Asia/Rangoon",
            "Asia/Saigon",
            "Atlantic/Faeroe",
            "Europe/Kiev",
            "Pacific/Enderbury",
            "Pacific/Ponape",
            "Pacific/Truk"
        };
        var zoneValues = await page.Locator("#lmxSignupTimeZone option")
            .EvaluateAllAsync<string[]>("options => options.map(option => option.value)");
        Assert.Equal(zoneValues.Length, zoneValues.Distinct(StringComparer.Ordinal).Count());
        Assert.All(preferredIds, preferredId => Assert.Contains(preferredId, zoneValues));
        Assert.All(legacyIds, legacyId => Assert.DoesNotContain(legacyId, zoneValues));

        var labelsWithoutCountries = await page.Locator(".lmx-timezone-option span")
            .EvaluateAllAsync<string[]>(
                "labels => labels.map(label => label.textContent.trim()).filter(label => label !== 'UTC' && !label.includes(', '))");
        Assert.Empty(labelsWithoutCountries);

        await search.FillAsync("Vietnam");
        var vietnamOption = page.Locator(".lmx-timezone-option");
        await Assertions.Expect(vietnamOption).ToHaveCountAsync(1);
        await Assertions.Expect(vietnamOption).ToHaveAttributeAsync("data-time-zone", "Asia/Ho_Chi_Minh");
        await Assertions.Expect(vietnamOption.Locator("span")).ToHaveTextAsync("Ho Chi Minh, Vietnam");
    }

    [Fact]
    public async Task ChallengeContent_UsesReadableSemanticColorsInLightAndDarkThemes()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = app.BaseAddress.ToString(),
                ColorScheme = scheme,
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 390, Height = 844 }
            });
            await BrowserTestApp.RouteExternalResourcesAsync(context);

            var page = await context.NewPageAsync();
            await page.RouteAsync(
                "**/api/data/athletes",
                route => FulfillJsonAsync(
                    route,
                    JsonSerializer.Serialize(new[]
                    {
                        new
                        {
                            DisplayName = "Browser Champion",
                            Name = "Browser Champion",
                            AthleteSlug = "browser_champion",
                            ProfilePicLeaderboardThumb = ""
                        }
                    })));
            await page.RouteAsync(
                "**/api/longevitymaxxing/state",
                route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
            await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator("#lmxBoardMeta").WaitForAsync();

            Assert.True(await page.Locator("#lmxHeroHighlights").IsHiddenAsync());
            Assert.Equal(0, await page.Locator("#lmxMetrics").CountAsync());
            Assert.Equal(0, await page.Locator(".lmx-ops-tile").CountAsync());

            var diagnostics = await BrowserContrast.MeasureVisibleTextAsync(
                page,
                "#lmxTitlePanel h1",
                "#lmxHeroCopy",
                ".lmx-question-preview-label span",
                "#lmxBoardMeta",
                ".lmx-board-row:not(.header) .lmx-name",
                ".lmx-board-row:not(.header) .lmx-number",
                ".lmx-field > .lmx-label",
                ".lmx-week-pager button:not(:disabled)");

            Assert.True(diagnostics.Length >= 10, $"Expected representative Challenge copy in {scheme} mode.");
            BrowserContrast.AssertMinimum($"{scheme} Challenge", diagnostics);

            await page.Locator("label:has(input[name='lmxSignupIdentity'][value='athlete'])").ClickAsync();
            var athleteSearch = page.Locator("#lmxSignupAthlete");
            await athleteSearch.FillAsync("Browser");
            var highlightedMatch = page.Locator(".lmx-athlete-option strong");
            await highlightedMatch.WaitForAsync();
            var highlightDiagnostics = await BrowserContrast.MeasureVisibleTextAsync(
                page,
                ".lmx-athlete-option strong");
            Assert.Single(highlightDiagnostics);
            BrowserContrast.AssertMinimum($"{scheme} athlete autocomplete highlight", highlightDiagnostics);

            await page.CloseAsync();
            await context.AddInitScriptAsync("window.localStorage.setItem('lmxAccessToken', 'browser-token');");
            var participantPage = await context.NewPageAsync();
            await participantPage.RouteAsync(
                "**/api/longevitymaxxing/state",
                route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
            await participantPage.RouteAsync(
                "**/api/longevitymaxxing/participant",
                route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(includeUpcomingCall: true))));
            await participantPage.GotoAsync(
                "/longevitymaxxing",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await participantPage.Locator(".lmx-dashboard-stat").First.WaitForAsync();

            var participantDiagnostics = await BrowserContrast.MeasureVisibleTextAsync(
                participantPage,
                "#lmxTitlePanel h1",
                ".lmx-status-pill:not(.muted)",
                "#lmxHeroCopy",
                ".lmx-ops-label",
                ".lmx-ops-tile strong",
                ".lmx-dashboard-head strong",
                ".lmx-mini-label",
                ".lmx-dashboard-stat span",
                ".lmx-dashboard-stat strong",
                ".lmx-dashboard-stat em",
                ".lmx-dashboard-category span",
                ".lmx-dashboard-category strong",
                "#lmxBoardMeta",
                ".lmx-board-row:not(.header) .lmx-name",
                ".lmx-board-row:not(.header) .lmx-number");

            Assert.True(
                participantDiagnostics.Length >= 22,
                $"Expected representative participant dashboard copy in {scheme} mode.");
            BrowserContrast.AssertMinimum($"{scheme} participant Challenge", participantDiagnostics);

            var onAccentForegrounds = await participantPage.EvaluateAsync<string[][]>(
                """
                () => {
                    if (!document.querySelector('.lmx-workflow-step i')) {
                        const workflowProbe = document.createElement('span');
                        workflowProbe.className = 'lmx-workflow-step';
                        workflowProbe.innerHTML = '<i aria-hidden="true"></i>';
                        document.querySelector('.lmx-page')?.append(workflowProbe);
                    }
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--lwc-on-accent)';
                    document.body.append(probe);
                    const expected = getComputedStyle(probe).color;
                    probe.remove();
                    const selectors = [
                        '.lmx-status-pill:not(.muted)',
                        '.lmx-mini-label',
                        '.lmx-ops-tile i',
                        '.lmx-workflow-step i'
                    ];
                    return selectors.map(selector => [
                        selector,
                        expected,
                        ...Array.from(document.querySelectorAll(selector), element => getComputedStyle(element).color)
                    ]);
                }
                """);
            Assert.All(onAccentForegrounds, colors =>
            {
                Assert.True(colors.Length > 2, $"Expected at least one {colors[0]} element.");
                Assert.All(colors.Skip(2), color => Assert.Equal(colors[1], color));
            });
        }
    }

    [Fact]
    public async Task QuoteDialogSourceLinksAndActions_MeetContrastForEveryThemeAndCategory()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = app.BaseAddress.ToString(),
                ColorScheme = scheme,
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 390, Height = 844 }
            });
            await BrowserTestApp.RouteExternalResourcesAsync(context);
            var page = await context.NewPageAsync();
            await page.RouteAsync(
                "**/api/longevitymaxxing/state",
                route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
            await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator(".lmx-page").WaitForAsync();

            foreach (var bucket in new[] { "sleep", "exercise", "nutrition", "vices", "mindset" })
            {
                await page.EvaluateAsync(
                    """
                    bucket => {
                        document.getElementById('contrastQuoteDialog')?.remove();
                        const dialog = document.createElement('div');
                        dialog.id = 'contrastQuoteDialog';
                        dialog.className = 'lmx-quote-dialog';
                        dialog.dataset.quoteBucket = bucket;
                        dialog.innerHTML = `
                            <div class="lmx-quote-dialog-card">
                                <div class="lmx-quote-dialog-main">
                                    <div class="lmx-quote-source">
                                        <span class="lmx-quote-athlete"><a href="#athlete">Quoted athlete</a></span>
                                    </div>
                                    <div class="lmx-quote-dialog-actions">
                                        <button class="lmx-button" type="button">OK</button>
                                    </div>
                                </div>
                            </div>`;
                        document.body.append(dialog);
                    }
                    """,
                    bucket);

                var link = page.Locator("#contrastQuoteDialog .lmx-quote-athlete a");
                var action = page.Locator("#contrastQuoteDialog .lmx-button");
                await link.WaitForAsync();

                var linkDefault = await BrowserContrast.MeasureVisibleTextAsync(
                    page,
                    "#contrastQuoteDialog .lmx-quote-athlete a");
                var actionDefault = await BrowserContrast.MeasureVisibleTextAsync(
                    page,
                    "#contrastQuoteDialog .lmx-button");
                Assert.Single(linkDefault);
                Assert.Single(actionDefault);
                BrowserContrast.AssertMinimum($"{scheme} {bucket} quote link", linkDefault);
                BrowserContrast.AssertMinimum($"{scheme} {bucket} quote action", actionDefault);

                await link.HoverAsync();
                var linkHover = await BrowserContrast.MeasureVisibleTextAsync(
                    page,
                    "#contrastQuoteDialog .lmx-quote-athlete a");
                BrowserContrast.AssertMinimum($"{scheme} {bucket} quote link hover", linkHover);

                await action.HoverAsync();
                var actionHover = await BrowserContrast.MeasureVisibleTextAsync(
                    page,
                    "#contrastQuoteDialog .lmx-button");
                BrowserContrast.AssertMinimum($"{scheme} {bucket} quote action hover", actionHover);
            }
        }
    }

    [Fact]
    public async Task Leaderboard_UsesTwoWeekPagerOnMobileAndKeepsFullDesktopTimeline()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var publicStateJson = JsonSerializer.Serialize(BuildPublicState());
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, publicStateJson));

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#lmxWeekPager:not([hidden])").WaitForAsync();

        var compatibilityChips = page.Locator("#lmxLifeStrip span");
        Assert.Equal(4, await compatibilityChips.CountAsync());
        foreach (var mobileViewport in new[] { (Width: 390, Height: 844), (Width: 360, Height: 800) })
        {
            await page.SetViewportSizeAsync(mobileViewport.Width, mobileViewport.Height);
            var mobileChipLayout = await compatibilityChips.EvaluateAllAsync<double[][]>(
                "chips => chips.map(chip => { const rect = chip.getBoundingClientRect(); return [rect.top, chip.scrollWidth, chip.clientWidth, chip.scrollHeight, chip.clientHeight]; })");
            var mobileRowCounts = mobileChipLayout
                .GroupBy(values => Math.Round(values[0]))
                .Select(row => row.Count())
                .OrderBy(count => count)
                .ToArray();
            Assert.Equal(new[] { 2, 2 }, mobileRowCounts);
            Assert.DoesNotContain(
                mobileChipLayout,
                values => values[1] > values[2] + 1 || values[3] > values[4] + 1);
            Assert.DoesNotContain(
                await compatibilityChips.EvaluateAllAsync<double[]>(
                    "chips => chips.map(chip => parseFloat(getComputedStyle(chip).fontSize))"),
                fontSize => fontSize < 12);
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                $"Compatibility labels introduce horizontal overflow at {mobileViewport.Width}x{mobileViewport.Height}.");
        }

        var cells = page.Locator(".lmx-board-row:not(.header) .lmx-cell");
        Assert.Equal(14, await cells.CountAsync());
        Assert.Equal("9", await cells.First.GetAttributeAsync("data-day"));
        Assert.Equal("22", await cells.Last.GetAttributeAsync("data-day"));
        Assert.Equal("Days 9\u201322", await page.Locator("#lmxWeekLabel").TextContentAsync());
        Assert.False(await page.Locator("#lmxWeekOlder").IsDisabledAsync());
        Assert.True(await page.Locator("#lmxWeekNewer").IsDisabledAsync());

        await page.Locator("#lmxWeekOlder").ClickAsync();

        Assert.Equal(8, await cells.CountAsync());
        Assert.Equal("1", await cells.First.GetAttributeAsync("data-day"));
        Assert.Equal("8", await cells.Last.GetAttributeAsync("data-day"));
        Assert.Equal("Days 1\u20138", await page.Locator("#lmxWeekLabel").TextContentAsync());
        Assert.True(await page.Locator("#lmxWeekOlder").IsDisabledAsync());
        Assert.False(await page.Locator("#lmxWeekNewer").IsDisabledAsync());

        await page.SetViewportSizeAsync(1024, 900);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.lmx-board-row:not(.header) .lmx-cell').length === 22");

        Assert.Equal(22, await cells.CountAsync());
        Assert.True(await page.Locator("#lmxWeekPager").IsHiddenAsync());
        foreach (var desktopWidth in new[] { 1024, 1081, 1200 })
        {
            await page.SetViewportSizeAsync(desktopWidth, 900);
            var desktopChipLayout = await compatibilityChips.EvaluateAllAsync<double[][]>(
                "chips => chips.map(chip => { const rect = chip.getBoundingClientRect(); return [rect.top, chip.scrollWidth, chip.clientWidth, chip.scrollHeight, chip.clientHeight]; })");
            var desktopChipTops = desktopChipLayout.Select(values => values[0]).ToArray();
            Assert.True(desktopChipTops.Max() - desktopChipTops.Min() <= 1,
                $"Compatibility labels should share one desktop row at {desktopWidth}px, but their top offsets were {string.Join(", ", desktopChipTops)}.");
            Assert.DoesNotContain(
                desktopChipLayout,
                values => values[1] > values[2] + 1 || values[3] > values[4] + 1);
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task HabitIcons_UseCategoryPaletteWhileLeaderboardDotsMatchTheirCells()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("window.localStorage.setItem('lmxAccessToken', 'browser-token');");

        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState())));

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".lmx-growth-control").First.WaitForAsync();
        await page.Locator(".lmx-habit-marks").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached
        });

        var expectedPalette = new[]
        {
            "rgb(37, 99, 235)",
            "rgb(220, 38, 38)",
            "rgb(22, 163, 74)",
            "rgb(124, 58, 237)"
        };
        var expectedCellColors = new[]
        {
            "rgb(6, 93, 104)",
            "rgb(180, 35, 59)",
            "rgb(154, 103, 0)",
            "rgb(31, 122, 56)"
        };
        Assert.Equal(expectedPalette, await ComputedColorsAsync(page.Locator(".lmx-habit-card i"), "backgroundColor"));
        Assert.Equal(expectedPalette, await ComputedColorsAsync(page.Locator(".lmx-question-preview-item i"), "backgroundColor"));
        Assert.Equal(expectedPalette, await ComputedColorsAsync(page.Locator(".lmx-question .lmx-question-icon"), "backgroundColor"));
        Assert.Equal(expectedPalette, await ComputedColorsAsync(page.Locator(".lmx-habit-key i"), "color"));

        var cellAndDotColors = await page.EvaluateAsync<string[][]>("""
            () => ["practice", "score-low", "score-mid", "score-high"].map(scoreClass => {
                const cell = document.createElement("div");
                cell.className = `lmx-cell lmx-cell-breakdown ${scoreClass}`;
                cell.innerHTML = ["sleep", "exercise", "nutrition", "vices"]
                    .map(key => `<span class="lmx-habit-mark full" data-key="${key}"></span>`)
                    .join("");
                (document.querySelector('.lmx-page') || document.body).append(cell);
                const colors = [
                    getComputedStyle(cell).color,
                    ...Array.from(cell.children, dot => getComputedStyle(dot).color)
                ];
                cell.remove();
                return colors;
            })
            """);
        Assert.Equal(expectedCellColors, cellAndDotColors.Select(colors => colors[0]));
        Assert.All(cellAndDotColors, colors => Assert.All(colors.Skip(1), dotColor => Assert.Equal(colors[0], dotColor)));
    }

    [Fact]
    public async Task CommunityCallIcon_RendersWithoutExternalIconFont()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("window.localStorage.setItem('lmxAccessToken', 'browser-token');");

        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(includeUpcomingCall: true))));

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var icon = page.Locator(".lmx-call-title-icon");
        await icon.WaitForAsync();

        Assert.Equal("svg", await icon.EvaluateAsync<string>("element => element.tagName.toLowerCase()"));
        Assert.Equal("0 0 640 512", await icon.GetAttributeAsync("viewBox"));
        var size = await icon.EvaluateAsync<double[]>("element => { const rect = element.getBoundingClientRect(); return [rect.width, rect.height]; }");
        Assert.True(size[0] >= 16 && size[1] >= 12, $"Community call icon rendered at {size[0]}x{size[1]} pixels.");
    }

    [Fact]
    public async Task CheckInForm_ShowsLatestDiscussionSupportsRepliesAndOpensPhotosInAccessibleViewer()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 760, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("window.localStorage.setItem('lmxAccessToken', 'browser-token');");

        var publicStateJson = JsonSerializer.Serialize(BuildPublicState(
            includeMentionParticipants: true,
            includeDiscussionNotesWithMentionParticipants: true));
        var participantStateJson = JsonSerializer.Serialize(BuildParticipantState(
            includeMentionParticipants: true,
            includeDiscussionNotesWithMentionParticipants: true));
        var successfulReplyStateJson = JsonSerializer.Serialize(BuildParticipantState(
            includeMentionParticipants: true,
            includeDiscussionNotesWithMentionParticipants: true,
            discussionReplySnapshot: DiscussionReplySnapshot.ContinuousAfterReply));
        var disjointReplyStateJson = JsonSerializer.Serialize(BuildParticipantState(
            includeMentionParticipants: true,
            includeDiscussionNotesWithMentionParticipants: true,
            discussionReplySnapshot: DiscussionReplySnapshot.DisjointAfterConcurrentReplies));
        var freshAfterDelayedPageStateJson = JsonSerializer.Serialize(BuildParticipantState(
            includeMentionParticipants: true,
            includeDiscussionNotesWithMentionParticipants: true,
            discussionReplySnapshot: DiscussionReplySnapshot.FreshAfterDelayedPage));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, publicStateJson));
        await page.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, participantStateJson));
        string? replyPagePayload = null;
        var replyPageAttempts = 0;
        var delayedReplyPageRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelayedReplyPage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedReplyPageFulfilled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", async route =>
        {
            replyPagePayload = route.Request.PostData;
            replyPageAttempts++;
            if (replyPageAttempts > 1)
            {
                delayedReplyPageRequested.TrySetResult(true);
                await releaseDelayedReplyPage.Task;
                await FulfillJsonAsync(route, JsonSerializer.Serialize(new
                {
                    replies = new[]
                    {
                        Reply("stale-r4", "p3", "Bea", "Delayed old reply four.", "2026-06-30T08:00:00Z"),
                        Reply("stale-r5", "p4", "Cam", "Delayed old reply five.", "2026-06-30T09:00:00Z"),
                        Reply("stale-r6", "p5", "Dee", "Delayed old reply six.", "2026-06-30T10:00:00Z")
                    },
                    totalCount = 9,
                    remainingEarlierReplyCount = 3,
                    hasEarlier = true,
                    nextBeforeCreatedAtUtc = "2026-06-30T08:00:00Z",
                    nextBeforeReplyId = "stale-r4"
                }));
                delayedReplyPageFulfilled.TrySetResult(true);
                return;
            }
            await FulfillJsonAsync(route, JsonSerializer.Serialize(new
            {
                replies = new[] { Reply("r1", "p3", "Bea", "This helped me rethink breakfast.", "2026-06-29T08:00:00Z") },
                totalCount = 4,
                remainingEarlierReplyCount = 0,
                hasEarlier = false,
                nextBeforeCreatedAtUtc = (string?)null,
                nextBeforeReplyId = (string?)null
            }));
        });
        var replyPayloads = new List<string>();
        var replyAttempts = 0;
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            replyPayloads.Add(route.Request.PostData ?? "");
            replyAttempts++;
            if (replyAttempts == 1)
            {
                await route.AbortAsync("connectionfailed");
                return;
            }
            var response = replyAttempts switch
            {
                2 => successfulReplyStateJson,
                3 => disjointReplyStateJson,
                _ => freshAfterDelayedPageStateJson
            };
            await FulfillJsonAsync(route, response);
        });

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".lmx-recent-remarks").WaitForAsync();

        var remarks = page.Locator(".lmx-recent-remark");
        Assert.Equal(3, await remarks.CountAsync());

        var checkInText = await page.Locator("#lmxCheckinList").InnerTextAsync();
        Assert.Contains("Active discussion", checkInText);
        Assert.Contains("Fox\nFri, Jun 12 · Day 5", checkInText);
        Assert.Contains("An older post with enough current discussion to rank first.", checkInText);
        Assert.Contains("Ari\nMon, Jun 29 · Day 22", checkInText);
        Assert.Contains("First recent public remark.", checkInText);
        Assert.Contains("Bea\nSun, Jun 28 · Day 21", checkInText);
        Assert.DoesNotContain("Cam\nSat, Jun 27 · Day 20", checkInText);
        Assert.DoesNotContain("Fourth older public remark.", checkInText);
        Assert.DoesNotContain("Private participant-only remark.", checkInText);

        var replyButtons = remarks.Locator(".lmx-discussion-reply");
        Assert.Equal(3, await replyButtons.CountAsync());
        var replyBox = await replyButtons.First.BoundingBoxAsync();
        Assert.NotNull(replyBox);
        Assert.True(replyBox.Width >= 44 && replyBox.Height >= 44, $"Expected at least a 44px discussion reply target; got {replyBox.Width}x{replyBox.Height}.");
        var discussionInput = page.Locator(".lmx-checkin-card > .lmx-field textarea[data-mention-input]").First;
        Assert.Equal("", await discussionInput.InputValueAsync());
        await discussionInput.FillAsync("Unsaved check-in discussion draft.");
        var unsavedExerciseNo = page.Locator(".lmx-checkin-card .lmx-question[data-key='exercise'] input[value='0']");
        await page.Locator(".lmx-checkin-card .lmx-question[data-key='sleep'] label[data-answer='yes']").ClickAsync();
        await page.Locator(".lmx-checkin-card .lmx-question[data-key='exercise'] label[data-answer='no']").ClickAsync();
        await page.Locator(".lmx-checkin-card .lmx-question[data-key='nutrition'] label[data-answer='yes']").ClickAsync();
        await page.Locator(".lmx-checkin-card .lmx-question[data-key='vices'] label[data-answer='yes']").ClickAsync();
        await replyButtons.First.ClickAsync();
        var composer = remarks.First.Locator(".lmx-discussion-reply-composer");
        await composer.WaitForAsync();
        Assert.Contains("Reply to Fox", await composer.InnerTextAsync());
        Assert.True(await page.Locator(".lmx-checkin-card").EvaluateAsync<bool>("form => form.checkValidity()"));
        var replyText = composer.Locator("textarea");
        Assert.True(await replyText.EvaluateAsync<bool>("element => element === document.activeElement"));
        await replyText.FillAsync("Thanks @Ari");
        var ariMention = composer.Locator(".lmx-mention-option", new LocatorLocatorOptions { HasText = "Ari Able" });
        await ariMention.ClickAsync();
        Assert.Equal("Thanks @Ari Able ", await replyText.InputValueAsync());

        var foxReplies = remarks.First.Locator(".lmx-discussion-reply-item");
        Assert.Equal(3, await foxReplies.CountAsync());
        var loadEarlierReplies = remarks.First.Locator("[data-discussion-replies-page]");
        Assert.Equal("View 1 earlier reply", (await loadEarlierReplies.InnerTextAsync()).Trim());
        await loadEarlierReplies.ClickAsync();
        await Assertions.Expect(remarks.First.Locator(".lmx-discussion-reply-item")).ToHaveCountAsync(4);
        await Assertions.Expect(remarks.First.Locator("[data-discussion-replies-page]")).ToHaveCountAsync(0);
        Assert.Equal("Thanks @Ari Able ", await replyText.InputValueAsync());
        Assert.Equal("Unsaved check-in discussion draft.", await discussionInput.InputValueAsync());
        Assert.True(await unsavedExerciseNo.IsCheckedAsync());
        Assert.NotNull(replyPagePayload);
        using (var pageJson = JsonDocument.Parse(replyPagePayload!))
        {
            var root = pageJson.RootElement;
            Assert.Equal("browser-token", root.GetProperty("accessToken").GetString());
            Assert.Equal("p7", root.GetProperty("postParticipantId").GetString());
            Assert.Equal(5, root.GetProperty("challengeDay").GetInt32());
            Assert.Equal("2026-06-29T12:00:00Z", root.GetProperty("beforeCreatedAtUtc").GetString());
            Assert.Equal("r2", root.GetProperty("beforeReplyId").GetString());
        }

        await replyText.FillAsync("An actual child reply for @Ari Able.");
        await composer.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(composer.Locator("[data-reply-submit]")).ToBeEnabledAsync();
        Assert.Single(replyPayloads);
        await composer.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-discussion-reply-composer")).ToHaveCountAsync(0);
        var foxDiscussionSurfaces = new[]
        {
            page.Locator(".lmx-recent-remark[data-discussion-post-participant-id='p7'][data-discussion-post-challenge-day='5']"),
            page.Locator("#lmxNotes .lmx-note[data-discussion-post-participant-id='p7'][data-discussion-post-challenge-day='5']")
        };
        foreach (var surface in foxDiscussionSurfaces)
        {
            var visibleReplyIds = await surface.Locator(".lmx-discussion-reply-item")
                .EvaluateAllAsync<string[]>("replies => replies.map(reply => reply.dataset.discussionReplyId)");
            Assert.Equal(["r1", "r2", "r3", "r4", "r6"], visibleReplyIds);
            await Assertions.Expect(surface.Locator("[data-discussion-reply-id='r1']")).ToContainTextAsync("This helped me rethink breakfast.");
            await Assertions.Expect(surface.Locator("[data-discussion-reply-id='r2']")).ToContainTextAsync("Trying the same approach tomorrow.");
            await Assertions.Expect(surface.Locator("[data-discussion-reply-id='r6']")).ToContainTextAsync("An actual child reply for @Ari Able.");
            await Assertions.Expect(surface.Locator("[data-discussion-replies-page]")).ToHaveCountAsync(0);
        }
        Assert.Equal(2, replyPayloads.Count);
        string? firstReplyId = null;
        foreach (var replyPayload in replyPayloads)
        {
            using var replyJson = JsonDocument.Parse(replyPayload);
            var root = replyJson.RootElement;
            Assert.Equal("browser-token", root.GetProperty("accessToken").GetString());
            Assert.Equal("p7", root.GetProperty("postParticipantId").GetString());
            Assert.Equal(5, root.GetProperty("challengeDay").GetInt32());
            Assert.Equal("An actual child reply for @Ari Able.", root.GetProperty("body").GetString());
            var replyId = root.GetProperty("replyId").GetString();
            Assert.True(Guid.TryParse(replyId, out _));
            firstReplyId ??= replyId;
            Assert.Equal(firstReplyId, replyId);
        }
        Assert.Equal("Unsaved check-in discussion draft.", await discussionInput.InputValueAsync());
        Assert.True(await unsavedExerciseNo.IsCheckedAsync());
        Assert.False(await page.Locator(".lmx-checkin-card button[type='submit']").IsDisabledAsync());

        await foxDiscussionSurfaces[0].Locator("[data-discussion-reply]").ClickAsync();
        var secondComposer = foxDiscussionSurfaces[0].Locator(".lmx-discussion-reply-composer");
        await secondComposer.Locator("textarea").FillAsync("A second local reply after concurrent activity.");
        await secondComposer.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-discussion-reply-composer")).ToHaveCountAsync(0);
        Assert.Equal(3, replyPayloads.Count);
        foreach (var surface in foxDiscussionSurfaces)
        {
            var visibleReplyIds = await surface.Locator(".lmx-discussion-reply-item")
                .EvaluateAllAsync<string[]>("replies => replies.map(reply => reply.dataset.discussionReplyId)");
            Assert.Equal(["r7", "r8", "r9"], visibleReplyIds);
            var restartedPager = surface.Locator("[data-discussion-replies-page]");
            await Assertions.Expect(restartedPager).ToHaveTextAsync("View 6 earlier replies");
            Assert.Equal("2026-06-30T11:00:00Z", await restartedPager.GetAttributeAsync("data-before-created-at-utc"));
            Assert.Equal("r7", await restartedPager.GetAttributeAsync("data-before-reply-id"));
        }
        Assert.Equal("Unsaved check-in discussion draft.", await discussionInput.InputValueAsync());
        Assert.True(await unsavedExerciseNo.IsCheckedAsync());

        var delayedPager = foxDiscussionSurfaces[0].Locator("[data-discussion-replies-page]");
        await delayedPager.ClickAsync();
        await delayedReplyPageRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await delayedPager.IsDisabledAsync());
        await foxDiscussionSurfaces[0].Locator("[data-discussion-reply]").ClickAsync();
        var thirdComposer = foxDiscussionSurfaces[0].Locator(".lmx-discussion-reply-composer");
        await thirdComposer.Locator("textarea").FillAsync("A reply while older discussion is loading.");
        await thirdComposer.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-discussion-reply-composer")).ToHaveCountAsync(0);
        Assert.Equal(4, replyPayloads.Count);
        foreach (var surface in foxDiscussionSurfaces)
        {
            var visibleReplyIds = await surface.Locator(".lmx-discussion-reply-item")
                .EvaluateAllAsync<string[]>("replies => replies.map(reply => reply.dataset.discussionReplyId)");
            Assert.Equal(["r10", "r11", "r12"], visibleReplyIds);
            var freshPager = surface.Locator("[data-discussion-replies-page]");
            await Assertions.Expect(freshPager).ToHaveTextAsync("View 9 earlier replies");
            Assert.Equal("2026-06-30T14:00:00Z", await freshPager.GetAttributeAsync("data-before-created-at-utc"));
            Assert.Equal("r10", await freshPager.GetAttributeAsync("data-before-reply-id"));
        }

        releaseDelayedReplyPage.TrySetResult(true);
        await delayedReplyPageFulfilled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        foreach (var surface in foxDiscussionSurfaces)
        {
            var visibleReplyIds = await surface.Locator(".lmx-discussion-reply-item")
                .EvaluateAllAsync<string[]>("replies => replies.map(reply => reply.dataset.discussionReplyId)");
            Assert.Equal(["r10", "r11", "r12"], visibleReplyIds);
            await Assertions.Expect(surface.Locator("[data-discussion-reply-id^='stale-']")).ToHaveCountAsync(0);
            var freshPager = surface.Locator("[data-discussion-replies-page]");
            await Assertions.Expect(freshPager).ToHaveTextAsync("View 9 earlier replies");
            Assert.Equal("2026-06-30T14:00:00Z", await freshPager.GetAttributeAsync("data-before-created-at-utc"));
            Assert.Equal("r10", await freshPager.GetAttributeAsync("data-before-reply-id"));
        }
        Assert.Equal("Unsaved check-in discussion draft.", await discussionInput.InputValueAsync());
        Assert.True(await unsavedExerciseNo.IsCheckedAsync());

        var photos = page.Locator(".lmx-recent-remark .lmx-note-photo");
        Assert.Equal(2, await photos.CountAsync());
        Assert.Equal("button", await photos.Nth(0).EvaluateAsync<string>("element => element.tagName.toLowerCase()"));
        Assert.Equal("button", await photos.Nth(0).GetAttributeAsync("type"));
        Assert.Equal("/generated/longevitymaxxing/check-in-photos/ari.webp?v=ari", await photos.Nth(0).GetAttributeAsync("data-photo-src"));
        Assert.Equal("/generated/longevitymaxxing/check-in-photos/bea.webp?v=bea", await photos.Nth(1).GetAttributeAsync("data-photo-src"));
        Assert.Equal("1600", await photos.Nth(0).Locator("img").GetAttributeAsync("width"));
        Assert.Equal("800", await photos.Nth(0).Locator("img").GetAttributeAsync("height"));

        await photos.Nth(0).ClickAsync();
        var viewer = page.Locator("#lmxNotePhotoViewer");
        await page.Locator("#lmxNotePhotoViewer.show").WaitForAsync();
        Assert.Equal("dialog", await viewer.GetAttributeAsync("role"));
        Assert.Equal("true", await viewer.GetAttributeAsync("aria-modal"));
        Assert.Equal("false", await viewer.GetAttributeAsync("aria-hidden"));
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('lmx-photo-viewer-open')"));
        Assert.True(await page.Locator("main").EvaluateAsync<bool>("main => main.inert"));
        Assert.Equal("Photo 1 of 2", await viewer.Locator(".lmx-photo-viewer-position").InnerTextAsync());
        Assert.Equal(
            "/generated/longevitymaxxing/check-in-photos/ari.webp?v=ari",
            await viewer.Locator(".lmx-photo-viewer-stage img").GetAttributeAsync("src"));

        var closeButton = viewer.Locator(".lmx-photo-viewer-close");
        var previousButton = viewer.Locator(".lmx-photo-viewer-nav.previous");
        var nextButton = viewer.Locator(".lmx-photo-viewer-nav.next");
        Assert.True(await closeButton.EvaluateAsync<bool>("button => button === document.activeElement"));
        Assert.False(await previousButton.IsEnabledAsync());
        Assert.True(await nextButton.IsEnabledAsync());
        foreach (var control in new[] { closeButton, previousButton, nextButton })
        {
            var box = await control.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Width >= 44 && box.Height >= 44, $"Expected at least a 44px photo viewer touch target during motion; got {box.Width}x{box.Height}.");
        }

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Photo 2 of 2", await viewer.Locator(".lmx-photo-viewer-position").InnerTextAsync());
        Assert.Equal(
            "/generated/longevitymaxxing/check-in-photos/bea.webp?v=bea",
            await viewer.Locator(".lmx-photo-viewer-stage img").GetAttributeAsync("src"));
        Assert.True(await previousButton.IsEnabledAsync());
        Assert.False(await nextButton.IsEnabledAsync());
        Assert.Equal(
            "/generated/longevitymaxxing/check-in-photos/bea.webp?v=bea",
            await page.EvaluateAsync<string>("() => history.state?.lmxNotePhotoViewer?.source"));

        await previousButton.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await closeButton.EvaluateAsync<bool>("button => button === document.activeElement"));

        await page.Keyboard.PressAsync("Escape");
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.False(await page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('lmx-photo-viewer-open')"));
        Assert.False(await page.Locator("main").EvaluateAsync<bool>("main => main.inert"));
        Assert.True(await photos.Nth(1).EvaluateAsync<bool>("button => button === document.activeElement"));

        await page.GoForwardAsync();
        await page.Locator("#lmxNotePhotoViewer.show").WaitForAsync();
        Assert.Equal("Photo 2 of 2", await viewer.Locator(".lmx-photo-viewer-position").InnerTextAsync());
        await page.GoBackAsync();
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        await photos.Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("#lmxNotePhotoViewer.show").WaitForAsync();
        await viewer.EvaluateAsync("dialog => dialog.click()");
        await viewer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.True(await photos.Nth(0).EvaluateAsync<bool>("button => button === document.activeElement"));

        Assert.True(await page.Locator(".lmx-checkin-card").EvaluateAsync<bool>(
            """
            form => {
                const save = form.querySelector('button[type="submit"]');
                const remarks = form.querySelector('.lmx-recent-remarks');
                return !!save && !!remarks && !!(save.compareDocumentPosition(remarks) & Node.DOCUMENT_POSITION_FOLLOWING);
            }
            """));
        Assert.Contains(errors, error => error.Contains("ERR_CONNECTION_FAILED", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => !error.Contains("ERR_CONNECTION_FAILED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscussionPager_PreservesServerHotOrderAndPagesPosts()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1140, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var notes = page.Locator("#lmxNotes");
        var pager = page.Locator("#lmxNotesPager");
        var newer = page.Locator("#lmxNotesNewer");
        var older = page.Locator("#lmxNotesOlder");
        var label = page.Locator("#lmxNotesDayLabel");
        await notes.WaitForAsync();

        Assert.Equal("1–5 of 6", await label.InnerTextAsync());
        Assert.Equal("Discussion posts 1 through 5 of 6", await label.GetAttributeAsync("aria-label"));
        Assert.True(await newer.IsDisabledAsync());
        Assert.False(await older.IsDisabledAsync());
        var firstPageText = await notes.InnerTextAsync();
        Assert.Contains("Fox\nFri, Jun 12 · Day 5", firstPageText);
        Assert.Contains("An older post with enough current discussion to rank first.", firstPageText);
        Assert.Contains("Ari\nMon, Jun 29 · Day 22", firstPageText);
        Assert.Contains("Cam\nSat, Jun 27 · Day 20", firstPageText);
        Assert.Contains("Dee\nFri, Jun 26 · Day 19", firstPageText);
        Assert.DoesNotContain("Another note from the same day.", firstPageText);
        var postHeader = notes.Locator(".lmx-discussion-post-header").First;
        Assert.Equal("div", await postHeader.EvaluateAsync<string>("element => element.tagName.toLowerCase()"));
        Assert.NotEqual("fixed", await postHeader.EvaluateAsync<string>("element => getComputedStyle(element).position"));
        var postHeaderBox = await postHeader.BoundingBoxAsync();
        Assert.NotNull(postHeaderBox);
        Assert.True(postHeaderBox.Height < 80, $"Discussion post header unexpectedly expanded to {postHeaderBox.Height}px.");
        Assert.Equal("visible", await notes.EvaluateAsync<string>("element => getComputedStyle(element).overflowY"));
        Assert.Equal("none", await notes.EvaluateAsync<string>("element => getComputedStyle(element).maxHeight"));

        await older.ClickAsync();
        Assert.Equal("6 of 6", await label.InnerTextAsync());
        Assert.Equal("Discussion post 6 of 6", await label.GetAttributeAsync("aria-label"));
        var oldestPageText = await notes.InnerTextAsync();
        Assert.Contains("Eli\nFri, Jun 26 · Day 19", oldestPageText);
        Assert.Contains("Another note from the same day.", oldestPageText);
        Assert.DoesNotContain("Fox", oldestPageText);
        Assert.True(await older.IsDisabledAsync());
        Assert.False(await newer.IsDisabledAsync());

        await newer.ClickAsync();
        Assert.Equal("1–5 of 6", await label.InnerTextAsync());
        Assert.Contains("Fox", await notes.InnerTextAsync());
        Assert.Contains("Ari", await notes.InnerTextAsync());

        foreach (var control in new[] { newer, older })
        {
            var box = await control.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Width >= 44 && box.Height >= 44, $"Expected at least a 44px notes pager touch target; got {box.Width}x{box.Height}.");
        }

        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await pager.EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().right <= element.closest('.lmx-card').getBoundingClientRect().right"));
        Assert.Equal("column", await page.Locator(".lmx-notes-header").EvaluateAsync<string>("element => getComputedStyle(element).flexDirection"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task CheckInNoteMentionPickerSupportsKeyboardAndPointerWithoutCoveringActions()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("window.localStorage.setItem('lmxAccessToken', 'browser-token');");

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState(includeMentionParticipants: true))));
        await page.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(includeMentionParticipants: true))));

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var textarea = page.Locator("textarea[data-mention-input]");
        await textarea.WaitForAsync();

        var displayedNote = page.Locator(".lmx-recent-remark p");
        var profileMention = displayedNote.Locator("a.lmx-note-mention");
        var participantMention = displayedNote.Locator("span.lmx-note-mention");
        Assert.Equal(1, await profileMention.CountAsync());
        Assert.Equal("@Ari Able", await profileMention.InnerTextAsync());
        Assert.Equal("/athlete/ari-able", await profileMention.GetAttributeAsync("href"));
        Assert.Equal("Ari Able, view athlete profile", await profileMention.GetAttributeAsync("aria-label"));
        Assert.Equal(1, await participantMention.CountAsync());
        Assert.Equal("@Bea Builder", await participantMention.InnerTextAsync());
        Assert.Equal(2, await displayedNote.Locator(".lmx-note-mention").CountAsync());
        Assert.Contains("@Nobody", await displayedNote.InnerTextAsync());
        Assert.DoesNotContain("test@Ari Able", await profileMention.InnerTextAsync());
        Assert.True(await profileMention.EvaluateAsync<bool>(
            "element => getComputedStyle(element).textDecorationLine.includes('underline')"));
        await profileMention.FocusAsync();
        Assert.Equal("solid", await profileMention.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));

        await textarea.FillAsync("Great work @Be");

        var list = page.Locator(".lmx-mention-options:not([hidden])");
        await list.WaitForAsync();
        Assert.Equal("combobox", await textarea.GetAttributeAsync("role"));
        Assert.Equal("true", await textarea.GetAttributeAsync("aria-expanded"));
        Assert.Equal("listbox", await list.GetAttributeAsync("role"));
        var option = list.Locator(".lmx-mention-option");
        Assert.Equal(1, await option.CountAsync());
        Assert.Contains("Bea Builder", await option.InnerTextAsync());
        Assert.Equal("true", await option.GetAttributeAsync("aria-selected"));

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const textarea = document.querySelector('textarea[data-mention-input]');
                const list = document.querySelector('.lmx-mention-options:not([hidden])');
                const photoField = document.querySelector('.lmx-note-photo-field');
                const option = document.querySelector('.lmx-mention-option');
                if (!textarea || !list || !photoField || !option) return [];
                const textareaRect = textarea.getBoundingClientRect();
                const listRect = list.getBoundingClientRect();
                const photoRect = photoField.getBoundingClientRect();
                const optionRect = option.getBoundingClientRect();
                return [textareaRect.bottom, listRect.top, listRect.bottom, photoRect.top, optionRect.height];
            }
            """);
        Assert.Equal(5, layout.Length);
        Assert.True(layout[1] >= layout[0] - 1, "Mention suggestions should flow below the note editor.");
        Assert.True(layout[3] >= layout[2] - 1, "Mention suggestions should not cover the photo or save actions.");
        Assert.True(layout[4] >= 44, $"Expected a 44px mention touch target; got {layout[4]}.");

        await textarea.PressAsync("Enter");
        Assert.Equal("Great work @Bea Builder ", await textarea.InputValueAsync());
        Assert.Equal("false", await textarea.GetAttributeAsync("aria-expanded"));
        Assert.True(await list.IsHiddenAsync());

        await textarea.FillAsync("Thanks @Ar");
        await list.WaitForAsync();
        var ari = list.Locator(".lmx-mention-option");
        Assert.Equal(1, await ari.CountAsync());
        await ari.DispatchEventAsync("mousedown");
        Assert.Equal("Thanks @Ari Able ", await textarea.InputValueAsync());
        Assert.True(await textarea.EvaluateAsync<bool>("element => element === document.activeElement"));

        await textarea.FillAsync("Thanks @Ari Able for this.");
        await textarea.ClickAsync();
        Assert.True(await list.IsHiddenAsync());

        await textarea.FillAsync("@");
        await list.WaitForAsync();
        Assert.Equal(2, await list.Locator(".lmx-mention-option").CountAsync());
        Assert.DoesNotContain("Browser Tester", await list.InnerTextAsync());
        await textarea.PressAsync("Escape");
        Assert.Equal("false", await textarea.GetAttributeAsync("aria-expanded"));
        Assert.True(await list.IsHiddenAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DirectCheckInLink_OpensFocusedDialogWithExplicitTaperedTallAnswersAndDisabledSaveUntilComplete()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 760, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.RouteAsync(
            "**/api/longevitymaxxing/participant",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(eligibleDayDate: yesterday))));

        await page.GotoAsync("/longevitymaxxing?token=browser-token", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var dialog = page.Locator("#lmxParticipantPanel.lmx-checkin-dialog-panel");
        await dialog.WaitForAsync();

        Assert.Equal("dialog", await dialog.GetAttributeAsync("role"));
        Assert.Equal("true", await dialog.GetAttributeAsync("aria-modal"));
        Assert.Equal("Daily check-in", await dialog.GetAttributeAsync("aria-label"));
        Assert.True(await page.Locator("html").EvaluateAsync<bool>("html => html.classList.contains('lmx-checkin-dialog-open')"));
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('lmx-checkin-dialog-open')"));
        Assert.True(await page.Locator("main").EvaluateAsync<bool>("main => main.inert"));
        Assert.True(await page.Locator("#lmxTitlePanel").IsVisibleAsync());
        Assert.True(await page.Locator("#lmxBoardSection").IsVisibleAsync());
        Assert.True(await dialog.Locator("#lmxParticipantTabs").IsHiddenAsync());
        Assert.Equal("Yesterday", await dialog.Locator("[data-check-in-day-label]").InnerTextAsync());
        Assert.DoesNotContain("Day 22", await dialog.Locator(".lmx-checkin-card > h3").InnerTextAsync());
        Assert.Equal(0, await dialog.Locator("input[type='range']").CountAsync());

        var publicDiscussion = dialog.Locator(".lmx-recent-remarks");
        await publicDiscussion.WaitForAsync();
        Assert.True(await publicDiscussion.IsVisibleAsync());
        Assert.Equal("Active public discussion", await publicDiscussion.GetAttributeAsync("aria-label"));
        Assert.Equal(3, await publicDiscussion.Locator(".lmx-recent-remark").CountAsync());
        var publicDiscussionText = await publicDiscussion.InnerTextAsync();
        Assert.Contains("Fox\nFri, Jun 12 · Day 5", publicDiscussionText);
        Assert.Contains("An older post with enough current discussion to rank first.", publicDiscussionText);
        Assert.Contains("Ari\nMon, Jun 29 · Day 22", publicDiscussionText);
        Assert.Contains("First recent public remark.", publicDiscussionText);
        Assert.Contains("Bea\nSun, Jun 28 · Day 21", publicDiscussionText);
        Assert.DoesNotContain("Cam\nSat, Jun 27 · Day 20", publicDiscussionText);
        Assert.DoesNotContain("Private participant-only remark.", publicDiscussionText);

        var answerInputs = dialog.Locator(".lmx-answer-input");
        Assert.Equal(12, await answerInputs.CountAsync());
        Assert.Equal(0, await dialog.Locator(".lmx-answer-input:checked").CountAsync());
        var expectedAccessibleLabels = Enumerable.Range(0, 4)
            .SelectMany(_ => new[] { "No", "Somewhat", "Yes" })
            .ToArray();
        Assert.Equal(
            expectedAccessibleLabels,
            await answerInputs.EvaluateAllAsync<string[]>(
                "elements => elements.map(element => element.getAttribute('aria-label'))"));
        var answerFaces = dialog.Locator(".lmx-answer-face");
        Assert.Equal(12, await answerFaces.CountAsync());
        Assert.All(
            await answerFaces.EvaluateAllAsync<string[]>("elements => elements.map(element => element.textContent.trim())"),
            Assert.Empty);
        var answerIcons = dialog.Locator(".lmx-answer-face-icon");
        Assert.Equal(12, await answerIcons.CountAsync());
        var answerIconSizes = await answerIcons.EvaluateAllAsync<double[][]>(
            "elements => elements.map(element => { const box = element.getBoundingClientRect(); return [box.width, box.height]; })");
        Assert.All(answerIconSizes, size =>
        {
            Assert.InRange(size[0], 36, 42);
            Assert.InRange(size[1], 36, 42);
        });
        var answerMouths = await dialog.Locator(".lmx-answer-face-mouth").EvaluateAllAsync<string[]>(
            "elements => elements.map(element => element.getAttribute('d'))");
        Assert.Equal(3, answerMouths.Distinct().Count());
        var answerHeights = await answerFaces.EvaluateAllAsync<double[]>(
            "elements => elements.map(element => element.getBoundingClientRect().height)");
        Assert.All(answerHeights, height => Assert.True(height >= 100, $"Expected a tall answer button; got {height}px."));
        var sleepAnswerBoxes = await dialog
            .Locator(".lmx-question[data-key='sleep'] .lmx-answer-face")
            .EvaluateAllAsync<double[][]>(
                "elements => elements.map(element => { const box = element.getBoundingClientRect(); return [box.width, box.height]; })");
        Assert.Equal(3, sleepAnswerBoxes.Length);
        Assert.InRange(sleepAnswerBoxes[0][0] / sleepAnswerBoxes[2][0], 0.62d, 0.66d);
        Assert.InRange(sleepAnswerBoxes[1][0] / sleepAnswerBoxes[2][0], 0.80d, 0.84d);
        Assert.True(
            sleepAnswerBoxes.Max(box => box[1]) - sleepAnswerBoxes.Min(box => box[1]) <= 0.5d,
            $"Expected equal-height answer buttons; got {string.Join(", ", sleepAnswerBoxes.Select(box => box[1]))}px.");

        var closeButton = dialog.Locator("#lmxCheckinDialogClose");
        var closeBox = await closeButton.BoundingBoxAsync();
        Assert.NotNull(closeBox);
        Assert.True(closeBox.Width >= 44 && closeBox.Height >= 44, $"Expected a 44px close target; got {closeBox.Width}x{closeBox.Height}.");
        Assert.True(await closeButton.EvaluateAsync<bool>("button => button === document.activeElement"));

        var save = dialog.Locator(".lmx-checkin-card > button[type='submit']");
        Assert.False(await save.IsEnabledAsync());
        var selections = new[]
        {
            (Key: "sleep", Value: "0", Border: "rgb(220, 38, 38)", Text: "rgb(153, 27, 27)"),
            (Key: "exercise", Value: "1", Border: "rgb(37, 99, 235)", Text: "rgb(30, 64, 175)"),
            (Key: "nutrition", Value: "2", Border: "rgb(22, 163, 74)", Text: "rgb(22, 101, 52)")
        };
        foreach (var selection in selections)
        {
            var option = dialog.Locator($".lmx-question[data-key='{selection.Key}'] .lmx-answer-option:has(.lmx-answer-input[value='{selection.Value}'])");
            await option.ClickAsync();
            Assert.True(await option.Locator(".lmx-answer-input").IsCheckedAsync());
            await Assertions.Expect(option.Locator(".lmx-answer-face")).ToHaveCSSAsync("border-color", selection.Border);
            await Assertions.Expect(option.Locator(".lmx-answer-face")).ToHaveCSSAsync("color", selection.Text);
        }
        Assert.False(await save.IsEnabledAsync());
        var vicesYes = dialog.Locator(".lmx-question[data-key='vices'] .lmx-answer-option:has(.lmx-answer-input[value='2'])");
        await vicesYes.ClickAsync();
        Assert.True(await vicesYes.Locator(".lmx-answer-input").IsCheckedAsync());
        await Assertions.Expect(vicesYes.Locator(".lmx-answer-face")).ToHaveCSSAsync("border-color", "rgb(22, 163, 74)");
        await Assertions.Expect(vicesYes.Locator(".lmx-answer-face")).ToHaveCSSAsync("color", "rgb(22, 101, 52)");
        Assert.True(await save.IsEnabledAsync());

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
        var darkSelections = new[]
        {
            (Option: dialog.Locator(".lmx-question[data-key='sleep'] .lmx-answer-option[data-answer='no'] .lmx-answer-face"), Border: "rgb(255, 139, 160)", Text: "rgb(255, 139, 160)"),
            (Option: dialog.Locator(".lmx-question[data-key='exercise'] .lmx-answer-option[data-answer='somewhat'] .lmx-answer-face"), Border: "rgb(96, 165, 250)", Text: "rgb(191, 219, 254)"),
            (Option: dialog.Locator(".lmx-question[data-key='nutrition'] .lmx-answer-option[data-answer='yes'] .lmx-answer-face"), Border: "rgb(104, 196, 125)", Text: "rgb(104, 196, 125)"),
            (Option: vicesYes.Locator(".lmx-answer-face"), Border: "rgb(104, 196, 125)", Text: "rgb(104, 196, 125)")
        };
        foreach (var selection in darkSelections)
        {
            await Assertions.Expect(selection.Option).ToHaveCSSAsync("border-color", selection.Border);
            await Assertions.Expect(selection.Option).ToHaveCSSAsync("color", selection.Text);
        }
        await closeButton.ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.False(await page.Locator("main").EvaluateAsync<bool>("main => main.inert"));
        Assert.False(await page.Locator("html").EvaluateAsync<bool>("html => html.classList.contains('lmx-checkin-dialog-open')"));
        Assert.False(await page.Locator("body").EvaluateAsync<bool>("body => body.classList.contains('lmx-checkin-dialog-open')"));
        Assert.True(await page.Locator("#lmxParticipantPanel").EvaluateAsync<bool>("panel => !!panel.closest('.lmx-action-card')"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task CheckInGarden_UsesEstablishedGrowthDamageWithSeedlingStartAndBoundedProceduralPlants()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("window.localStorage.setItem('lmxAccessToken', 'browser-token');");

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        object participantResponse = BuildParticipantState();
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, JsonSerializer.Serialize(participantResponse)));

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".lmx-growth-control").First.WaitForAsync();

        Assert.Equal(4, await page.Locator(".lmx-plant").CountAsync());
        Assert.Equal("760", await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant").GetAttributeAsync("data-yes-count"));
        Assert.Equal("903", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-no-count"));
        foreach (var plant in await page.Locator(".lmx-plant").AllAsync())
        {
            Assert.Equal(64, await plant.Locator(".lmx-plant-leaf").CountAsync());
            Assert.Equal(64, await plant.Locator(".lmx-plant-branch").CountAsync());
            Assert.Equal(12, await plant.Locator(".lmx-plant-bud").CountAsync());
        }
        Assert.Equal(0, await page.Locator(".lmx-plant figcaption").CountAsync());
        Assert.Equal(0, await page.Locator(".lmx-lever-input").CountAsync());
        Assert.Equal(0, await page.Locator(".lmx-answer-input:checked").CountAsync());
        var mobileAnswerWidths = await page
            .Locator(".lmx-question[data-key='sleep'] .lmx-answer-face")
            .EvaluateAllAsync<double[]>(
                "elements => elements.map(element => element.getBoundingClientRect().width)");
        Assert.All(
            mobileAnswerWidths,
            width => Assert.True(width >= 44, $"Expected at least a 44px mobile answer target; got {width}px."));
        Assert.Equal("55", await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal("24", await page.Locator(".lmx-question[data-key='exercise'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal("0", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal("64", await page.Locator(".lmx-question[data-key='vices'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal(55, await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant-leaf.active").CountAsync());
        Assert.Equal(24, await page.Locator(".lmx-question[data-key='exercise'] .lmx-plant-leaf.active").CountAsync());
        Assert.Equal(0, await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant-leaf.active").CountAsync());
        Assert.Equal(64, await page.Locator(".lmx-question[data-key='vices'] .lmx-plant-leaf.active").CountAsync());

        var sleepNo = page.Locator(".lmx-question[data-key='sleep'] .lmx-answer-option:has(.lmx-answer-input[value='0'])");
        var sleepYes = page.Locator(".lmx-question[data-key='sleep'] .lmx-answer-option:has(.lmx-answer-input[value='2'])");
        var nutritionNo = page.Locator(".lmx-question[data-key='nutrition'] .lmx-answer-option:has(.lmx-answer-input[value='0'])");
        var vicesYes = page.Locator(".lmx-question[data-key='vices'] .lmx-answer-option:has(.lmx-answer-input[value='2'])");
        await sleepNo.ClickAsync();
        var damagedSleepPlant = page.Locator(".lmx-question[data-key='sleep'] .lmx-plant");
        var establishedDamageVitality = double.Parse(
            (await damagedSleepPlant.GetAttributeAsync("data-vitality"))!,
            CultureInfo.InvariantCulture);
        Assert.Equal(0.559d, establishedDamageVitality, 4);
        Assert.Equal("34", await damagedSleepPlant.GetAttributeAsync("data-leaf-count"));
        await sleepYes.ClickAsync();
        await nutritionNo.ClickAsync();
        await vicesYes.ClickAsync();

        Assert.Equal("2", await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant").GetAttributeAsync("data-preview"));
        Assert.Equal("0", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-preview"));
        Assert.Equal("2", await page.Locator(".lmx-question[data-key='vices'] .lmx-plant").GetAttributeAsync("data-preview"));
        Assert.Contains("1000 saved check-ins", await page.Locator(".lmx-question[data-key='vices'] .lmx-plant").GetAttributeAsync("aria-label"));
        Assert.Equal("903", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-no-count"));

        participantResponse = BuildParticipantState(emptyGarden: true);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".lmx-growth-control").First.WaitForAsync();

        var newSleepNo = page.Locator(".lmx-question[data-key='sleep'] .lmx-answer-option:has(.lmx-answer-input[value='0'])");
        var newSleepYes = page.Locator(".lmx-question[data-key='sleep'] .lmx-answer-option:has(.lmx-answer-input[value='2'])");
        var newSleepPlant = page.Locator(".lmx-question[data-key='sleep'] .lmx-plant");
        Assert.Equal("0.0000", await newSleepPlant.GetAttributeAsync("data-vitality"));
        Assert.Contains("--lmx-plant-scale: 0.1800", await newSleepPlant.GetAttributeAsync("style"));
        await newSleepNo.ClickAsync();
        var firstNoVitality = double.Parse(
            (await newSleepPlant.GetAttributeAsync("data-vitality"))!,
            CultureInfo.InvariantCulture);
        Assert.Equal(0d, firstNoVitality, 4);
        Assert.Equal("0", await newSleepPlant.GetAttributeAsync("data-leaf-count"));
        await newSleepYes.ClickAsync();
        var firstYesVitality = double.Parse(
            (await newSleepPlant.GetAttributeAsync("data-vitality"))!,
            CultureInfo.InvariantCulture);
        Assert.Equal(0.025d, firstYesVitality, 4);
        Assert.Equal("0", await newSleepPlant.GetAttributeAsync("data-leaf-count"));
        Assert.Contains("--lmx-plant-scale: 0.2005", await newSleepPlant.GetAttributeAsync("style"));
        Assert.Empty(errors);
    }

    private static Task FulfillJsonAsync(IRoute route, string body)
        => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = body
        });

    private static Task<string[]> ComputedColorsAsync(ILocator locator, string property)
        => locator.EvaluateAllAsync<string[]>(
            "(elements, property) => elements.map(element => getComputedStyle(element)[property])",
            property);

    private static object BuildParticipantState(
        bool emptyGarden = false,
        bool includeUpcomingCall = false,
        bool includeMentionParticipants = false,
        bool includeDiscussionNotesWithMentionParticipants = false,
        string? eligibleDayDate = null,
        DiscussionReplySnapshot discussionReplySnapshot = DiscussionReplySnapshot.Initial)
        => new
        {
            @public = BuildPublicState(
                includeMentionParticipants,
                includeDiscussionNotesWithMentionParticipants,
                discussionReplySnapshot),
            participant = new
            {
                id = "p1",
                email = "browser@example.test",
                displayName = "Browser Tester",
                timeZoneId = "UTC",
                athleteSlug = (string?)null,
                athleteUrl = (string?)null,
                profileImageUrl = (string?)null,
                challengeEmailsStopped = false,
                challengeInactive = false,
                daysIn = 22
            },
            eligibleDays = includeUpcomingCall
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        challengeDay = 22,
                        date = eligibleDayDate ?? "2026-06-29",
                        countsForScore = true,
                        existing = (object?)null
                    }
                },
            notes = (includeMentionParticipants && !includeDiscussionNotesWithMentionParticipants
                    ? BuildMentionDiscussionNotes()
                    : BuildDiscussionNotes(discussionReplySnapshot))
                .Concat(new[] { Note("p-private", "Private", 1, "2026-06-08", "Private participant-only remark.") })
                .ToArray(),
            calls = includeUpcomingCall
                ? new object[]
                {
                    new
                    {
                        key = "community-call",
                        label = "Community call",
                        selectedSlot = new
                        {
                            id = "2099-01-01T08:30:00Z",
                            startsAtUtc = "2099-01-01T08:30:00Z"
                        },
                        videoCallUrl = (string?)null
                    }
                }
                : Array.Empty<object>(),
            garden = BuildGardenState(emptyGarden)
        };

    private static object BuildGardenState(bool emptyGarden)
        => emptyGarden
            ? new
            {
                checkedInDays = 0,
                sleep = new { yesCount = 0, noCount = 0, vitality = 0d },
                exercise = new { yesCount = 0, noCount = 0, vitality = 0d },
                nutrition = new { yesCount = 0, noCount = 0, vitality = 0d },
                vices = new { yesCount = 0, noCount = 0, vitality = 0d }
            }
            : new
            {
                checkedInDays = 1000,
                sleep = new { yesCount = 760, noCount = 86, vitality = 0.86d },
                exercise = new { yesCount = 300, noCount = 314, vitality = 0.4d },
                nutrition = new { yesCount = 30, noCount = 903, vitality = 0.025d },
                vices = new { yesCount = 1000, noCount = 0, vitality = 0.999d }
            };

    private static object BuildPublicState(
        bool includeMentionParticipants = false,
        bool includeDiscussionNotesWithMentionParticipants = false,
        DiscussionReplySnapshot discussionReplySnapshot = DiscussionReplySnapshot.Initial)
        => new
        {
            challengeName = "Longevitymaxxing Challenge",
            phase = "active",
            signupOpen = true,
            startDate = "2026-06-08",
            signupClosesAtUtc = "2026-06-08T00:00:00Z",
            callSelectionClosesAtUtc = "2026-06-06T18:00:00Z",
            endDate = "2026-06-21",
            durationDays = 14,
            dailyMaxScore = 11,
            days = Enumerable.Range(1, 22)
                .Select(day => new
                {
                    challengeDay = day,
                    date = DateOnly.Parse("2026-06-08").AddDays(day - 1).ToString("yyyy-MM-dd")
                })
                .ToArray(),
            leaderboard = new object[]
            {
                new
                {
                    participantId = "p1",
                    displayName = "Browser Tester",
                    athleteUrl = (string?)null,
                    profileImageUrl = (string?)null,
                    checkedInDays = 21,
                    totalPoints = 168,
                    currentStreak = 21,
                    cells = Enumerable.Range(1, 22)
                        .Select(day => new
                        {
                            challengeDay = day,
                            checkedIn = day == 22,
                            score = day == 22 ? 8 : (int?)null,
                            countsForScore = day != 1,
                            sleep = day == 22 ? 2 : (int?)null,
                            exercise = day == 22 ? 1 : (int?)null,
                            nutrition = day == 22 ? 2 : (int?)null,
                            vices = day == 22 ? 1 : (int?)null
                        })
                        .ToArray(),
                    badges = Array.Empty<string>(),
                    latestCheckInAtUtc = "2026-06-28T07:00:00Z",
                    challengeEmailsStopped = false,
                    challengeInactive = false
                }
            }.Concat(includeMentionParticipants
                ? new object[]
                {
                    MentionLeaderboardRow("p2", "Ari Able", "/athlete/ari-able"),
                    MentionLeaderboardRow("p3", "Bea Builder")
                }
                : []).ToArray(),
            podium = Array.Empty<object>(),
            notes = includeMentionParticipants && !includeDiscussionNotesWithMentionParticipants
                ? BuildMentionDiscussionNotes()
                : BuildDiscussionNotes(discussionReplySnapshot),
            calls = Array.Empty<object>(),
            slackInviteUrl = "",
            slackRoomUrl = (string?)null
        };

    private static object[] BuildDiscussionNotes(DiscussionReplySnapshot snapshot = DiscussionReplySnapshot.Initial)
    {
        var (foxReplies, foxReplyCount, foxLastActivityAtUtc) = snapshot switch
        {
            DiscussionReplySnapshot.ContinuousAfterReply => (
                new object[]
                {
                    Reply("r3", "p5", "Dee", "The small version worked for me.", "2026-06-30T07:00:00Z"),
                    Reply("r4", "p2", "Ari", "Keep us posted on the next step.", "2026-06-30T09:00:00Z"),
                    Reply("r6", "p1", "Browser Tester", "An actual child reply for @Ari Able.", "2026-06-30T10:00:00Z")
                },
                5,
                "2026-06-30T10:00:00Z"),
            DiscussionReplySnapshot.DisjointAfterConcurrentReplies => (
                new object[]
                {
                    Reply("r7", "p7", "Fox", "Concurrent reply seven.", "2026-06-30T11:00:00Z"),
                    Reply("r8", "p3", "Bea", "Concurrent reply eight.", "2026-06-30T12:00:00Z"),
                    Reply("r9", "p4", "Cam", "Concurrent reply nine.", "2026-06-30T13:00:00Z")
                },
                9,
                "2026-06-30T13:00:00Z"),
            DiscussionReplySnapshot.FreshAfterDelayedPage => (
                new object[]
                {
                    Reply("r10", "p3", "Bea", "Fresh reply ten.", "2026-06-30T14:00:00Z"),
                    Reply("r11", "p4", "Cam", "Fresh reply eleven.", "2026-06-30T15:00:00Z"),
                    Reply("r12", "p5", "Dee", "Fresh reply twelve.", "2026-06-30T16:00:00Z")
                },
                12,
                "2026-06-30T16:00:00Z"),
            _ => (
                new object[]
                {
                    Reply("r2", "p4", "Cam", "Trying the same approach tomorrow.", "2026-06-29T12:00:00Z"),
                    Reply("r3", "p5", "Dee", "The small version worked for me.", "2026-06-30T07:00:00Z"),
                    Reply("r4", "p2", "Ari", "Keep us posted on the next step.", "2026-06-30T09:00:00Z")
                },
                4,
                "2026-06-30T09:00:00Z")
        };

        return
        [
            Note(
                "p7",
                "Fox",
                5,
                "2026-06-12",
                "An older post with enough current discussion to rank first.",
                updatedAtUtc: "2026-06-12T07:00:00Z",
                lastActivityAtUtc: foxLastActivityAtUtc,
                replies: foxReplies,
                replyCount: foxReplyCount),
            Note("p2", "Ari", 22, "2026-06-29", "First recent public remark.",
                [CheckInImage("/generated/longevitymaxxing/check-in-photos/ari.webp?v=ari")],
                lastActivityAtUtc: "2026-06-29T10:00:00Z",
                replies: [Reply("r5", "p4", "Cam", "Nice work.", "2026-06-29T10:00:00Z")]),
            Note("p3", "Bea", 21, "2026-06-28", null,
                [CheckInImage("/generated/longevitymaxxing/check-in-photos/bea.webp?v=bea")]),
            Note("p4", "Cam", 20, "2026-06-27", "Third recent public remark."),
            Note("p5", "Dee", 19, "2026-06-26", "Fourth older public remark."),
            Note("p6", "Eli", 19, "2026-06-26", "Another note from the same day.")
        ];
    }

    private enum DiscussionReplySnapshot
    {
        Initial,
        ContinuousAfterReply,
        DisjointAfterConcurrentReplies,
        FreshAfterDelayedPage
    }

    private static object[] BuildMentionDiscussionNotes()
        =>
        [
            Note(
                "p4",
                "Cam",
                22,
                "2026-06-29",
                "Contact test@Ari Able, then thank @Ari Able and @Bea Builder — not @Nobody.")
        ];

    private static object MentionLeaderboardRow(string participantId, string displayName, string? athleteUrl = null)
        => new
        {
            participantId,
            displayName,
            athleteUrl,
            profileImageUrl = (string?)null,
            checkedInDays = 1,
            totalPoints = 8,
            currentStreak = 1,
            cells = Enumerable.Range(1, 22)
                .Select(day => new
                {
                    challengeDay = day,
                    checkedIn = false,
                    score = (int?)null,
                    countsForScore = day != 1,
                    sleep = (int?)null,
                    exercise = (int?)null,
                    nutrition = (int?)null,
                    vices = (int?)null
                })
                .ToArray(),
            badges = Array.Empty<string>(),
            latestCheckInAtUtc = "2026-06-28T07:00:00Z",
            challengeEmailsStopped = false,
            challengeInactive = false
        };

    private static object Note(
        string participantId,
        string displayName,
        int challengeDay,
        string date,
        string? note,
        object[]? images = null,
        string? updatedAtUtc = null,
        string? lastActivityAtUtc = null,
        object[]? replies = null,
        int? replyCount = null)
    {
        var effectiveUpdatedAtUtc = updatedAtUtc ?? $"{date}T07:00:00Z";
        var effectiveReplies = replies ?? Array.Empty<object>();
        return new
        {
            participantId,
            displayName,
            challengeDay,
            date,
            note,
            updatedAtUtc = effectiveUpdatedAtUtc,
            lastActivityAtUtc = lastActivityAtUtc ?? effectiveUpdatedAtUtc,
            replyCount = replyCount ?? effectiveReplies.Length,
            images = images ?? Array.Empty<object>(),
            replies = effectiveReplies
        };
    }

    private static object Reply(
        string id,
        string participantId,
        string displayName,
        string body,
        string createdAtUtc)
        => new { id, participantId, displayName, body, createdAtUtc };

    private static object CheckInImage(string url)
        => new
        {
            url,
            width = 1600,
            height = 800
        };
}
