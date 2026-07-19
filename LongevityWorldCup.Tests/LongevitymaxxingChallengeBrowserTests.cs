using Microsoft.Playwright;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LongevitymaxxingChallengeBrowserTests
{
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
    public async Task CheckInForm_ShowsLatestPublicRemarksUnderSave()
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

        var publicStateJson = JsonSerializer.Serialize(BuildPublicState());
        var participantStateJson = JsonSerializer.Serialize(BuildParticipantState());
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

        await page.GotoAsync("/longevitymaxxing", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".lmx-recent-remarks").WaitForAsync();

        var remarks = page.Locator(".lmx-recent-remark");
        Assert.Equal(3, await remarks.CountAsync());

        var checkInText = await page.Locator("#lmxCheckinList").InnerTextAsync();
        Assert.Contains("Recent remarks", checkInText);
        Assert.Contains("Ari · Day 22", checkInText);
        Assert.Contains("First recent public remark.", checkInText);
        Assert.Contains("Bea · Day 21", checkInText);
        Assert.Contains("Second recent public remark.", checkInText);
        Assert.Contains("Cam · Day 20", checkInText);
        Assert.Contains("Third recent public remark.", checkInText);
        Assert.DoesNotContain("Fourth older public remark.", checkInText);
        Assert.DoesNotContain("Private participant-only remark.", checkInText);

        Assert.True(await page.Locator(".lmx-checkin-card").EvaluateAsync<bool>(
            """
            form => {
                const save = form.querySelector('button[type="submit"]');
                const remarks = form.querySelector('.lmx-recent-remarks');
                return !!save && !!remarks && !!(save.compareDocumentPosition(remarks) & Node.DOCUMENT_POSITION_FOLLOWING);
            }
            """));
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
        Assert.Equal(0, await page.Locator(".lmx-lever-kicker").CountAsync());
        Assert.Equal("55", await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal("24", await page.Locator(".lmx-question[data-key='exercise'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal("0", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal("64", await page.Locator(".lmx-question[data-key='vices'] .lmx-plant").GetAttributeAsync("data-leaf-count"));
        Assert.Equal(55, await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant-leaf.active").CountAsync());
        Assert.Equal(24, await page.Locator(".lmx-question[data-key='exercise'] .lmx-plant-leaf.active").CountAsync());
        Assert.Equal(0, await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant-leaf.active").CountAsync());
        Assert.Equal(64, await page.Locator(".lmx-question[data-key='vices'] .lmx-plant-leaf.active").CountAsync());

        var sleep = page.Locator(".lmx-question[data-key='sleep'] .lmx-lever-input");
        var nutrition = page.Locator(".lmx-question[data-key='nutrition'] .lmx-lever-input");
        var vices = page.Locator(".lmx-question[data-key='vices'] .lmx-lever-input");
        await sleep.FocusAsync();
        await sleep.PressAsync("ArrowLeft");
        var damagedSleepPlant = page.Locator(".lmx-question[data-key='sleep'] .lmx-plant");
        var establishedDamageVitality = double.Parse(
            (await damagedSleepPlant.GetAttributeAsync("data-vitality"))!,
            CultureInfo.InvariantCulture);
        Assert.Equal(0.559d, establishedDamageVitality, 4);
        Assert.Equal("34", await damagedSleepPlant.GetAttributeAsync("data-leaf-count"));
        await sleep.PressAsync("ArrowRight");
        await sleep.PressAsync("ArrowRight");
        await nutrition.FocusAsync();
        await nutrition.PressAsync("ArrowLeft");
        await vices.FocusAsync();
        await vices.PressAsync("ArrowRight");

        Assert.Equal("2", await page.Locator(".lmx-question[data-key='sleep'] .lmx-plant").GetAttributeAsync("data-preview"));
        Assert.Equal("0", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-preview"));
        Assert.Equal("2", await page.Locator(".lmx-question[data-key='vices'] .lmx-plant").GetAttributeAsync("data-preview"));
        Assert.Contains("1000 saved check-ins", await page.Locator(".lmx-question[data-key='vices'] .lmx-plant").GetAttributeAsync("aria-label"));
        Assert.Equal("903", await page.Locator(".lmx-question[data-key='nutrition'] .lmx-plant").GetAttributeAsync("data-no-count"));

        participantResponse = BuildParticipantState(emptyGarden: true);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".lmx-growth-control").First.WaitForAsync();

        var newSleep = page.Locator(".lmx-question[data-key='sleep'] .lmx-lever-input");
        var newSleepPlant = page.Locator(".lmx-question[data-key='sleep'] .lmx-plant");
        Assert.Equal("0.0000", await newSleepPlant.GetAttributeAsync("data-vitality"));
        Assert.Contains("--lmx-plant-scale: 0.1800", await newSleepPlant.GetAttributeAsync("style"));
        await newSleep.FocusAsync();
        await newSleep.PressAsync("ArrowLeft");
        var firstNoVitality = double.Parse(
            (await newSleepPlant.GetAttributeAsync("data-vitality"))!,
            CultureInfo.InvariantCulture);
        Assert.Equal(0d, firstNoVitality, 4);
        Assert.Equal("0", await newSleepPlant.GetAttributeAsync("data-leaf-count"));
        await newSleep.PressAsync("ArrowRight");
        await newSleep.PressAsync("ArrowRight");
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

    private static object BuildParticipantState(bool emptyGarden = false)
        => new
        {
            @public = BuildPublicState(),
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
                commitmentAmountUsd = 25m,
                daysIn = 22
            },
            eligibleDays = new[]
            {
                new
                {
                    challengeDay = 22,
                    date = "2026-06-29",
                    countsForScore = true,
                    existing = (object?)null
                }
            },
            notes = new[]
            {
                Note("p5", "Private", 1, "2026-06-08", "Private participant-only remark.")
            },
            calls = Array.Empty<object>(),
            commitment = new
            {
                status = "current",
                blocksParticipant = false,
                canEditAmount = true,
                canPay = false,
                amountUsd = 25m,
                owedAmountUsd = (decimal?)null,
                triggerChallengeDay = (int?)null,
                triggerScore = (int?)null,
                thresholdAverage = (decimal?)null,
                invoiceId = (string?)null,
                checkoutLink = (string?)null,
                invoiceStatus = (string?)null,
                message = (string?)null
            },
            trendGuidance = new
            {
                enforced = true,
                priorScoredDays = 14,
                averagePoints = 8m,
                neededPoints = 7,
                text = ""
            },
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

    private static object BuildPublicState()
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
            leaderboard = new[]
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
                    challengeInactive = false,
                    commitmentStatus = (string?)null
                }
            },
            podium = Array.Empty<object>(),
            notes = new[]
            {
                Note("p2", "Ari", 22, "2026-06-29", "First recent public remark."),
                Note("p3", "Bea", 21, "2026-06-28", "Second recent public remark."),
                Note("p4", "Cam", 20, "2026-06-27", "Third recent public remark."),
                Note("p5", "Dee", 19, "2026-06-26", "Fourth older public remark.")
            },
            calls = Array.Empty<object>(),
            slackInviteUrl = "",
            slackRoomUrl = (string?)null
        };

    private static object Note(string participantId, string displayName, int challengeDay, string date, string note)
        => new
        {
            participantId,
            displayName,
            challengeDay,
            date,
            note,
            updatedAtUtc = $"{date}T07:00:00Z",
            images = Array.Empty<object>()
        };
}
