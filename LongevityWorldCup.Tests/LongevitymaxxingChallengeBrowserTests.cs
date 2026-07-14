using Microsoft.Playwright;
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

        await page.SetViewportSizeAsync(1200, 900);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.lmx-board-row:not(.header) .lmx-cell').length === 22");

        Assert.Equal(22, await cells.CountAsync());
        Assert.True(await page.Locator("#lmxWeekPager").IsHiddenAsync());
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

    private static Task FulfillJsonAsync(IRoute route, string body)
        => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = body
        });

    private static object BuildParticipantState()
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
            }
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
