using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// The two internal tools (site statistics and the Social Post Manager) share one
// dashboard language: header strip cards, uppercase section titles, accent
// underline tabs, unclipped lists and full-width preview cards.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class InternalToolsDashboardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Statistics = "/internal/site-statistics.html";
    private const string Designer = "/internal/custom-event-designer.html";

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1440, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task Statistics_OverviewIsBuiltFromOneCardAndTabLanguage(int width, ColorScheme scheme)
    {
        await using var context = await NewContextAsync(width, scheme);
        var page = await context.NewPageAsync();
        await RouteDashboardAsync(page, DashboardPayload(diagnostics: false));
        await page.GotoAsync(Statistics);
        await page.Locator("#trafficDailyChart").WaitForAsync();

        var layout = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const css = (selector, pseudo) => getComputedStyle(document.querySelector(selector), pseudo || null);
                const cards = ['.stats-header', '.traffic-audience', '.traffic-outcomes', '.traffic-ranked-panel', '.traffic-disclosure']
                    .map(selector => css(selector));
                return {
                    cardRadii: cards.map(style => style.borderTopLeftRadius),
                    cardBorders: cards.map(style => style.borderTopWidth),
                    headings: [...document.querySelectorAll('.traffic-list-heading')].map(heading => getComputedStyle(heading).borderBottomWidth),
                    titleCase: [...document.querySelectorAll('.traffic-overview h2')].map(title => getComputedStyle(title).textTransform),
                    activeTabBackground: css('.stats-tabs > .stats-tab.active').backgroundColor,
                    activeTabUnderline: css('.stats-tabs > .stats-tab.active', '::after').backgroundColor,
                    busy: document.querySelector('.stats-page').getAttribute('aria-busy')
                };
            }
            """);

        Assert.All(layout.GetProperty("cardRadii").EnumerateArray(), radius => Assert.Equal("12px", radius.GetString()));
        Assert.All(layout.GetProperty("cardBorders").EnumerateArray(), border => Assert.Equal("1px", border.GetString()));
        Assert.NotEmpty(layout.GetProperty("headings").EnumerateArray());
        Assert.All(layout.GetProperty("headings").EnumerateArray(), border => Assert.Equal("1px", border.GetString()));
        Assert.All(layout.GetProperty("titleCase").EnumerateArray(), value => Assert.Equal("uppercase", value.GetString()));
        Assert.Equal("rgba(0, 0, 0, 0)", layout.GetProperty("activeTabBackground").GetString());
        Assert.Equal("rgb(107, 206, 192)", layout.GetProperty("activeTabUnderline").GetString());
        Assert.Equal("false", layout.GetProperty("busy").GetString());
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(1024)]
    [InlineData(1180)]
    [InlineData(1181)]
    [InlineData(195)]
    [InlineData(160)]
    [InlineData(390)]
    public async Task Statistics_DiagnosticsListsAreReadableAndNeverClipped(int width)
    {
        await using var context = await NewContextAsync(width, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        await RouteDashboardAsync(page, DashboardPayload(diagnostics: true));
        await page.GotoAsync(Statistics + "?tab=Onboarding%20Diagnostics");
        await page.Locator("#decisionBrief .decision-card").First.WaitForAsync();
        await page.Locator("#eventSamples .sample-row").First.WaitForAsync();

        var result = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const clipped = ['#decisionBrief', '#recommendedInvestigations', '#segmentComparisons', '#trendWatch']
                    .map(selector => document.querySelector(selector))
                    .filter(host => host.scrollHeight > host.clientHeight + 1)
                    .map(host => host.id);
                const rows = ['.sample-row', '.funnel-row', '.friction-row', '.metric-tile']
                    .map(selector => document.querySelector(selector))
                    .filter(Boolean)
                    .map(row => ({ name: row.className, align: getComputedStyle(row).textAlign, background: getComputedStyle(row).backgroundColor }));
                const fonts = [...document.querySelectorAll('.metric-label, .decision-gridline strong, .funnel-rate, .sample-meta, .friction-name span, .compact-table td, .compact-table th')]
                    .map(node => parseFloat(getComputedStyle(node).fontSize));
                const headers = [...document.querySelectorAll('.compact-table th')]
                    .map(th => ({ text: th.textContent, wraps: getComputedStyle(th).whiteSpace !== 'nowrap' && th.getBoundingClientRect().height > 30 }));
                return {
                    clipped,
                    rows,
                    smallestFont: Math.min(...fonts),
                    wrappedHeaders: headers.filter(header => header.wraps).map(header => header.text),
                    tablesScroll: [...document.querySelectorAll('.compact-table')].every(table => table.parentElement.classList.contains('table-scroll') && table.parentElement.tabIndex === 0),
                    rowOverflow: [...document.querySelectorAll('.trend-row, .segment-row, .funnel-row, .panel-meta')].filter(row => row.scrollWidth > row.clientWidth + 1).map(row => row.className)
                };
            }
            """);

        Assert.Empty(result.GetProperty("clipped").EnumerateArray());
        foreach (var row in result.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal("left", row.GetProperty("align").GetString());
            // Button rows never fall back to the browser's light-grey button face.
            Assert.NotEqual("rgb(239, 239, 239)", row.GetProperty("background").GetString());
        }
        Assert.True(result.GetProperty("smallestFont").GetDouble() >= 12, $"Smallest diagnostics text is {result.GetProperty("smallestFont").GetDouble()}px.");
        Assert.Empty(result.GetProperty("rowOverflow").EnumerateArray());
        Assert.Empty(result.GetProperty("wrappedHeaders").EnumerateArray());
        Assert.True(result.GetProperty("tablesScroll").GetBoolean());
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task Statistics_LoadingShowsSkeletonAndErrorsBecomeACallout()
    {
        await using var context = await NewContextAsync(1440, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource();
        await page.RouteAsync("**/api/site-statistics/dashboard**", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new RouteFulfillOptions { Status = 500, ContentType = "application/json", Body = "{}" });
        });
        await page.GotoAsync(Statistics, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Assertions.Expect(page.Locator(".stats-page")).ToHaveAttributeAsync("aria-busy", "true");
        var skeleton = await page.Locator("#trafficOverview").EvaluateAsync<double>(
            "host => host.innerHTML === '' ? host.getBoundingClientRect().height : 0");
        Assert.True(skeleton >= 200, $"Loading overview reserved {skeleton}px.");

        release.SetResult();
        await Assertions.Expect(page.Locator("#statsStatus.error")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".stats-page")).ToHaveAttributeAsync("aria-busy", "false");
        var callout = await page.Locator("#statsStatus").EvaluateAsync<JsonElement>(
            "status => ({ stripe: getComputedStyle(status).borderLeftWidth, padding: parseFloat(getComputedStyle(status).paddingTop) })");
        Assert.Equal("4px", callout.GetProperty("stripe").GetString());
        Assert.True(callout.GetProperty("padding").GetDouble() >= 12);
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1440, ColorScheme.Dark)]
    [InlineData(1024, ColorScheme.Dark)]
    public async Task Designer_PreviewsShareOneColumnWidthInOneCardLanguage(int width, ColorScheme scheme)
    {
        await using var context = await NewContextAsync(width, scheme);
        var page = await context.NewPageAsync();
        await page.GotoAsync(Designer);
        await page.Locator("#socialPreviewList .preview-block").First.WaitForAsync();

        var layout = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const blocks = [...document.querySelectorAll('.preview-block')].filter(block => block.offsetParent);
                const column = document.querySelector('.preview-stack').getBoundingClientRect().width;
                const chips = [...document.querySelectorAll('.check')].map(chip => Math.round(chip.getBoundingClientRect().width));
                const checked = getComputedStyle(document.querySelector('#sendWebpage').closest('.check')).backgroundColor;
                const unchecked = getComputedStyle(document.querySelector('#sendThreads').closest('.check')).backgroundColor;
                return {
                    widths: blocks.map(block => Math.round(block.getBoundingClientRect().width)),
                    column: Math.round(column),
                    chipSpread: Math.max(...chips) - Math.min(...chips),
                    checkedDiffers: checked !== unchecked,
                    titleCase: getComputedStyle(document.querySelector('.preview-title h2')).textTransform,
                    headerStrip: getComputedStyle(document.querySelector('.preview-title')).borderBottomWidth,
                    outputFont: getComputedStyle(document.querySelector('#commandOutput')).fontFamily,
                    bodyFont: getComputedStyle(document.body).fontFamily
                };
            }
            """);

        var column = layout.GetProperty("column").GetInt32();
        Assert.True(layout.GetProperty("widths").GetArrayLength() >= 3);
        Assert.All(layout.GetProperty("widths").EnumerateArray(), value => Assert.InRange(value.GetInt32(), column - 1, column + 1));
        Assert.True(layout.GetProperty("chipSpread").GetInt32() <= 1, "Destination chips should form an even grid.");
        Assert.True(layout.GetProperty("checkedDiffers").GetBoolean());
        Assert.Equal("uppercase", layout.GetProperty("titleCase").GetString());
        Assert.Equal("1px", layout.GetProperty("headerStrip").GetString());
        Assert.Contains("monospace", layout.GetProperty("outputFont").GetString());
        Assert.StartsWith("Roboto", layout.GetProperty("bodyFont").GetString()!.Trim('"'));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Dark)]
    [InlineData(195, ColorScheme.Dark)]
    [InlineData(160, ColorScheme.Dark)]
    public async Task Designer_AllDestinationsFitPhonesInOneColumn(int width, ColorScheme scheme)
    {
        await using var context = await NewContextAsync(width, scheme);
        var page = await context.NewPageAsync();
        await page.GotoAsync(Designer);
        foreach (var id in new[] { "sendThreads", "sendFacebook", "sendMastodon", "sendNostr", "sendReddit", "sendBluesky", "sendWebPush", "sendInstagram" })
            await page.Locator("#" + id).CheckAsync();
        await page.Locator("#webPushPreview").WaitForAsync();

        var layout = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const editor = document.querySelector('.editor-panel').getBoundingClientRect();
                const blocks = [...document.querySelectorAll('.preview-block')].filter(block => block.offsetParent)
                    .map(block => block.getBoundingClientRect());
                return {
                    editorWidth: Math.round(editor.width),
                    blockWidths: blocks.map(rect => Math.round(rect.width)),
                    columns: getComputedStyle(document.querySelector('.checks')).gridTemplateColumns.split(' ').length
                };
            }
            """);

        var editorWidth = layout.GetProperty("editorWidth").GetInt32();
        Assert.All(layout.GetProperty("blockWidths").EnumerateArray(), value => Assert.InRange(value.GetInt32(), editorWidth - 1, editorWidth + 1));
        Assert.Equal(width <= 280 ? 1 : 2, layout.GetProperty("columns").GetInt32());
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("Onboarding Diagnostics")]
    [InlineData("Challenge Diagnostics")]
    [InlineData("Source Quality")]
    [InlineData("Reliability Diagnostics")]
    [InlineData("Review Queue Diagnostics")]
    [InlineData("Public Event Diagnostics")]
    public async Task Statistics_DrilldownRetainsItsViewAndKeyboardFocus(string tab)
    {
        await using var context = await NewContextAsync(1181, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        await RouteDashboardAsync(page, DashboardPayload(diagnostics: true));
        await page.GotoAsync(Statistics + "?tab=" + Uri.EscapeDataString(tab));
        await page.Locator("#primaryFunnel .funnel-row").First.WaitForAsync();
        var names = await page.Locator("#primaryFunnel .funnel-name").AllTextContentsAsync();
        var selectedName = names[1];
        await page.Locator("#primaryFunnel .funnel-row").Nth(1).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(names, await page.Locator("#primaryFunnel .funnel-name").AllTextContentsAsync());
        Assert.Equal(selectedName, await page.EvaluateAsync<string>("() => document.activeElement.querySelector('.funnel-name')?.textContent || ''"));
        if (tab == "Onboarding Diagnostics")
        {
            await page.GotoAsync(Statistics + "?tab=" + Uri.EscapeDataString(tab));
            var sample = page.Locator("#eventSamples .sample-row").Nth(1);
            await sample.WaitForAsync();
            var sampleText = await sample.TextContentAsync();
            await sample.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            Assert.Equal(sampleText, await page.EvaluateAsync<string>("() => document.activeElement.textContent"));
            await page.Locator("#flowSelectors .flow-card").Nth(1).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            Assert.Equal("bortz age", await page.EvaluateAsync<string>("() => document.activeElement.querySelector('strong')?.textContent || ''"));
        }
    }

    [Theory]
    [InlineData(390)]
    [InlineData(320)]
    public async Task Statistics_LargeCountsRemainReadableInsideTheirCards(int width)
    {
        await using var context = await NewContextAsync(width, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        var payload = JsonNode.Parse(DashboardPayload(diagnostics: false))!;
        payload["trafficSummary"]!["totals"]!["sessions"] = int.MaxValue;
        payload["trafficSummary"]!["totals"]!["pageViews"] = int.MaxValue;
        var longLabel = string.Concat(Enumerable.Repeat("LongSyntheticRow", 20));
        payload["trafficSummary"]!["topPages"] = JsonSerializer.SerializeToNode(new[] { new { route = "/" + longLabel, sessions = int.MaxValue, pageViews = int.MaxValue } });
        payload["trafficSummary"]!["sources"] = JsonSerializer.SerializeToNode(new[] { new { label = longLabel, sessions = int.MaxValue, pageViews = int.MaxValue, events = int.MaxValue } });
        await RouteDashboardAsync(page, payload.ToJsonString());
        await page.GotoAsync(Statistics);
        await page.Locator("#trafficDailyChart").WaitForAsync();
        var escaped = await page.Locator(".traffic-metric, .traffic-ranked-row").EvaluateAllAsync<string[]>(
            """
            metrics => metrics.flatMap(metric => {
                const box = metric.getBoundingClientRect();
                return [...metric.children].flatMap(child => {
                    const range = document.createRange();
                    range.selectNodeContents(child);
                    return [...range.getClientRects()].some(rect => rect.left < box.left - 1 || rect.right > box.right + 1)
                        ? [child.textContent] : [];
                });
            })
            """);
        Assert.Empty(escaped);
        var count = await page.Locator(".traffic-metric > strong").First.TextContentAsync();
        Assert.Equal(int.MaxValue.ToString(CultureInfo.InvariantCulture), string.Concat(count!.Where(char.IsDigit)));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task Statistics_FunnelPreservesReachAndComparesAdjacentSessionCohorts()
    {
        await using var context = await NewContextAsync(1181, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        var payload = JsonNode.Parse(DashboardPayload(diagnostics: false))!;
        var at = DateTime.UtcNow.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
        object Step(string name, string session) => Event(name, "challenge", session, "/longevitymaxxing", "viewed", at, "desktop", "direct");
        payload["events"] = JsonSerializer.SerializeToNode(new[]
        {
            Step("challenge_page_viewed", "S-a"), Step("challenge_page_viewed", "S-b"),
            Step("challenge_public_state_loaded", "S-b"), Step("challenge_public_state_loaded", "S-c"), Step("challenge_public_state_loaded", "S-d"),
            Step("challenge_signup_started", "S-a"),
            Step("challenge_scored_checkin_submitted", "S-returning")
        });
        await RouteDashboardAsync(page, payload.ToJsonString());
        await page.GotoAsync(Statistics + "?tab=Challenge%20Diagnostics");
        var rows = page.Locator("#primaryFunnel .funnel-row");
        await Assertions.Expect(rows.Nth(1).Locator(".funnel-number")).ToHaveTextAsync("3");
        await Assertions.Expect(rows.Nth(1).Locator(".funnel-rate")).ToHaveTextAsync("50%");
        await Assertions.Expect(rows.Nth(1).Locator(".funnel-drop")).ToHaveTextAsync("drop 1");
        await Assertions.Expect(rows.Nth(2).Locator(".funnel-number")).ToHaveTextAsync("0");
        await Assertions.Expect(rows.Nth(2).Locator(".funnel-drop")).ToHaveTextAsync("drop 3");
        await Assertions.Expect(rows.Nth(3).Locator(".funnel-rate")).ToHaveTextAsync("0%");
        await Assertions.Expect(rows.Nth(3).Locator(".funnel-drop")).ToHaveTextAsync("drop 0");
        Assert.Equal("100%", await rows.Nth(1).Locator(".funnel-fill").EvaluateAsync<string>("e => e.style.width"));
        Assert.Equal(0, await rows.Nth(2).Locator(".funnel-fill").EvaluateAsync<double>("e => e.getBoundingClientRect().width"));
        var widths = await page.Locator(".funnel-fill").EvaluateAllAsync<double[]>("fills => fills.map(e => parseFloat(e.style.width))");
        Assert.All(widths, value => Assert.True(double.IsFinite(value) && value >= 0 && value <= 100));
    }

    [Fact]
    public async Task Designer_PushPreviewRendersItsIconWithoutAnExternalFont()
    {
        await using var context = await NewContextAsync(390, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        await page.GotoAsync(Designer);
        await page.Locator("#sendWebPush").CheckAsync();
        var icon = page.Locator("#webPushPreview svg.push-preview-icon");
        await Assertions.Expect(icon).ToBeVisibleAsync();
        var bounds = await icon.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(bounds.Width >= 22 && bounds.Height >= 22);
    }

    private Task<IBrowserContext> NewContextAsync(int width, ColorScheme scheme)
        => AestheticSystemBrowserTests.NewContextAsync(Browser, App, new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = width < 768 ? 844 : 900 },
            ColorScheme = scheme,
            ReducedMotion = ReducedMotion.Reduce,
            TimezoneId = "UTC"
        });

    private static Task RouteDashboardAsync(IPage page, string payload)
        => page.RouteAsync("**/api/site-statistics/dashboard**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "application/json",
            Body = payload
        }));

    private static string DashboardPayload(bool diagnostics)
    {
        var now = DateTime.UtcNow;
        string At(double hoursAgo) => now.AddHours(-hoursAgo).ToString("O", CultureInfo.InvariantCulture);
        var events = new List<object>();
        var previous = new List<object>();
        if (diagnostics)
        {
            for (var i = 0; i < 36; i++)
            {
                var session = $"S-{i:X8}";
                var device = i % 3 == 0 ? "mobile" : "desktop";
                var source = i % 4 == 0 ? "search" : i % 5 == 0 ? "social" : "direct";
                events.Add(Event("onboarding_entry_viewed", "onboarding", session, "/join", "viewed", At(i + 1), device, source));
                if (i % 2 == 0) events.Add(Event("onboarding_clock_selected", "pheno", session, "/join", "selected", At(i + .9), device, source));
                if (i % 3 != 2) events.Add(Event("calculator_started", "pheno", session, "/pheno-age", "started", At(i + .8), device, source));
                if (i % 4 == 1) events.Add(Event("calculator_result_generated", "pheno", session, "/pheno-age", "succeeded", At(i + .7), device, source));
                if (i % 6 == 0) events.Add(Event("client_error_observed", "site", session, "/pheno-age", "failed", At(i + .6), device, source, "TypeError: an unusually long client error message"));
                if (i % 9 == 0) events.Add(Event("proof_flow_opened", "application", session, "/apply", "opened", At(i + .5), device, source));
                if (i < 20) previous.Add(Event("onboarding_entry_viewed", "onboarding", "P" + session, "/join", "viewed", At(24 * 8 + i), device, source));
                if (i < 20 && i % 2 == 0) previous.Add(Event("calculator_started", "pheno", "P" + session, "/pheno-age", "started", At(24 * 8 + i - .5), device, source));
            }
        }

        var daily = Enumerable.Range(0, 8).Select(day => new
        {
            day = now.Date.AddDays(day - 7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sessions = 20 + day * 7, pageViews = 34 + day * 9, events = 90 + day * 11, successSessions = day % 3, successActions = day % 4
        });
        return JsonSerializer.Serialize(new
        {
            generatedAtUtc = At(0),
            filters = new { range = "7d", flow = "all", device = "all", source = "all", fromUtc = At(24 * 7), toUtc = At(0), previousFromUtc = At(24 * 14), previousToUtc = At(24 * 7), limit = 5000 },
            trafficSummary = new
            {
                totals = new { sessions = 420, pageViews = 690, events = 1800 },
                previousTotals = new { sessions = 300, pageViews = 512, events = 1300 },
                cleanTotals = new { sessions = 412, pageViews = 660, events = 1750 },
                quality = new { rawSessions = 420, cleanSessions = 412, noisySessions = 8, topSessionEvents = 60, topSessionShare = .03, repeatedPageViewSessions = 5, pageViewDominantSessions = 3, noisyPageViews = 30, noisyPageViewShare = .04 },
                daily,
                topPages = new[] { new { route = "/", sessions = 200, pageViews = 260 }, new { route = "/leaderboard", sessions = 120, pageViews = 150 }, new { route = "/join", sessions = 60, pageViews = 80 }, new { route = "/pheno-age", sessions = 40, pageViews = 52 }, new { route = "/apply", sessions = 12, pageViews = 16 } },
                sources = new[] { new { label = "direct", sessions = 240, pageViews = 380, events = 900 }, new { label = "search", sessions = 90, pageViews = 150, events = 400 }, new { label = "social", sessions = 50, pageViews = 90, events = 300 }, new { label = "referral", sessions = 30, pageViews = 50, events = 150 }, new { label = "email", sessions = 10, pageViews = 20, events = 50 } },
                referrers = new[] { new { label = "www.google.com", sessions = 80, pageViews = 140, events = 380 } },
                devices = new[] { new { label = "desktop", sessions = 300, pageViews = 500, events = 1300 }, new { label = "mobile", sessions = 120, pageViews = 190, events = 500 } },
                browsers = new[] { new { label = "Chrome", sessions = 300, pageViews = 500, events = 1300 } },
                outcomes = new { sessions = 40, actions = 52, newApplications = 4, challengeSignups = 3, calculatorResults = 30, resultUploads = 2, profileEdits = 1, unknownSubmissions = 0 }
            },
            events,
            previousEvents = previous,
            eventsPage = new { hasMore = false, nextCursor = (string?)null },
            previousEventsPage = new { hasMore = false, nextCursor = (string?)null }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static object Event(string eventName, string flow, string session, string route, string outcome, string at, string device, string source, string? errorCode = null)
        => new
        {
            occurredAtUtc = at,
            sessionHash = session,
            actorHash = (string?)null,
            eventName,
            flow,
            route,
            component = flow == "site" ? "client" : "calculator",
            step = (string?)null,
            outcome,
            errorCode,
            durationMs = (long?)null,
            deviceClass = device,
            browserFamily = "Chromium",
            referrerDomain = source == "search" ? "www.google.com" : null,
            source,
            landingRoute = "/join",
            firstReferrerDomain = source == "search" ? "www.google.com" : null,
            firstSource = source,
            firstCampaign = (string?)null,
            firstUtmSource = (string?)null,
            firstUtmMedium = (string?)null,
            firstUtmCampaign = (string?)null,
            firstUtmTerm = (string?)null,
            firstUtmContent = (string?)null,
            metadata = new Dictionary<string, string>()
        };
}
