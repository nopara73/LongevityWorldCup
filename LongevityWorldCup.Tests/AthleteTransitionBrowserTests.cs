using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteTransitionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task RapidPagingWhileDataIsPending_UsesEveryPressAndOnlyCommitsTheLatestIdentity(int width, bool dark)
    {
        await using var context = await NewContextAsync(width, dark);
        var page = await OpenAsync(context);
        var expected = await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name")
            .EvaluateAllAsync<string[]>("links => links.map(link => new URL(link.href).pathname)");
        await HoldAthleteReadsAsync(page);
        var requests = await page.EvaluateAsync<int>("""
            () => {
                const steps = document.querySelector('.modal-content > .athlete-steps');
                for (const direction of ['next', 'next', 'next', 'previous'])
                    steps.querySelector(`[data-step=${direction}]`).click();
                return window.pendingAthleteReads.length;
            }
            """);
        Assert.Equal(4, requests);
        Assert.Equal(expected[3], new Uri(page.Url).AbsolutePath);
        await ExpectCompactLoadingAsync(page);
        await page.EvaluateAsync("() => window.pendingAthleteReads.at(-1)(window.heldAthletes)");
        await WaitForProfileAsync(page);
        var name = await page.Locator("#athleteName").InnerTextAsync();
        var portrait = await page.Locator("#modalProfilePic").GetAttributeAsync("src");
        var bio = await page.Locator("#athleteBio").TextContentAsync();
        await page.EvaluateAsync("() => window.pendingAthleteReads.slice(0, -1).reverse().forEach(resolve => resolve(window.heldAthletes))");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.Equal(name, await page.Locator("#athleteName").InnerTextAsync());
        Assert.Equal(portrait, await page.Locator("#modalProfilePic").GetAttributeAsync("src"));
        Assert.Equal(bio, await page.Locator("#athleteBio").TextContentAsync());
        Assert.Equal(expected[3], new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator(".modal-content > .athlete-steps [data-step=next]")).ToBeEnabledAsync();
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task PagerChart_ChangesWithIdentityBeforeCanvasLoadAndRejectsObsoleteBuilds(int width, bool dark)
    {
        await using var context = await NewContextAsync(width, dark);
        var dates = new Dictionary<string, string>();
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            // Controlled dates keep the two real profiles distinct at every frame.
            var profiles = new[] { "Benjamin Garden", "Valerie Orsoni" }.Select(name =>
                athletes.OfType<JsonObject>().Single(a => a["Name"]!.GetValue<string>() == name).DeepClone().AsObject()).ToArray();
            athletes.Clear();
            for (var index = 0; index < profiles.Length; index++)
            {
                var profile = profiles[index];
                var reading = profile["Biomarkers"]!.AsArray().Last()!.DeepClone();
                var date = index == 0 ? "2025-01-12" : "2026-03-24";
                reading["Date"] = date;
                profile["Biomarkers"] = new JsonArray(reading);
                dates[profile["Name"]!.GetValue<string>()] = date;
                athletes.Add(profile);
            }
        });
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var rows = page.Locator("#athleteLeaderboard tr[data-athlete-name]");
        await Assertions.Expect(rows).ToHaveCountAsync(2);
        var firstName = (await rows.First.GetAttributeAsync("data-athlete-name"))!;
        var nextName = (await rows.Nth(1).GetAttributeAsync("data-athlete-name"))!;
        var nextFigure = await rows.Nth(1).Locator(".age-reduction").InnerTextAsync();
        static double Figure(string text) => double.Parse(System.Text.RegularExpressions.Regex.Match(text,
            @"[+\-−]?\d+(?:\.\d+)?").Value.Replace('−', '-'), System.Globalization.CultureInfo.InvariantCulture);
        await page.EvaluateAsync("""
            () => {
                window.chartBuilds = [];
                window.ensureChartJs = () => Promise.resolve();
                window.Chart = function(context, config) {
                    chartBuilds.push({ duration: config.options.animation.duration });
                    this.data = config.data;
                    this.options = config.options;
                    this.destroy = () => {};
                    this.resize = () => {};
                    this.update = () => {};
                };
            }
            """);
        await rows.First.Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#ageResultDate")).ToHaveAttributeAsync("datetime", dates[firstName]);
        await page.WaitForFunctionAsync("window.chartBuilds.length === 1");
        await page.EvaluateAsync("""
            () => {
                window.heldChartBuilds = [];
                window.chartBuilds = [];
                window.chartFrames = [];
                window.ensureChartJs = () => new Promise(resolve => heldChartBuilds.push(resolve));
                document.addEventListener('click', event => {
                    if (!event.target.closest('.modal-content > .athlete-steps [data-step]')) return;
                    requestAnimationFrame(() => chartFrames.push({
                        name: document.getElementById('athleteName').textContent,
                        date: document.getElementById('ageResultDate').getAttribute('datetime'),
                        value: document.getElementById('ageRadarCenterValue').textContent,
                        builds: chartBuilds.length
                    }));
                }, true);
            }
            """);
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await page.WaitForFunctionAsync("window.chartFrames.length === 1 && window.heldChartBuilds.length === 1");
        var firstFrame = await page.EvaluateAsync<System.Text.Json.JsonElement>("window.chartFrames[0]");
        Assert.Contains(nextName, firstFrame.GetProperty("name").GetString());
        Assert.Equal(dates[nextName], firstFrame.GetProperty("date").GetString());
        Assert.Equal(Figure(nextFigure), Figure(firstFrame.GetProperty("value").GetString()!), 1);
        Assert.Equal(0, firstFrame.GetProperty("builds").GetInt32());
        await page.Locator(".modal-content > .athlete-steps [data-step=previous]").ClickAsync();
        await page.WaitForFunctionAsync("window.chartFrames.length === 2 && window.heldChartBuilds.length === 2");
        var reversed = await page.EvaluateAsync<System.Text.Json.JsonElement>("window.chartFrames[1]");
        Assert.Equal(dates[firstName], reversed.GetProperty("date").GetString());
        await page.EvaluateAsync("window.heldChartBuilds[0]()");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.chartBuilds.length"));
        await page.Locator("#ageTabPheno").ClickAsync();
        var selectedValue = await page.Locator("#ageRadarCenterValue").InnerTextAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.EvaluateAsync("window.heldChartBuilds[1]()");
        await page.WaitForFunctionAsync("window.chartBuilds.length === 1");
        Assert.Equal(selectedValue, await page.Locator("#ageRadarCenterValue").InnerTextAsync());
        await Assertions.Expect(page.Locator("#ageTabPheno")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.chartBuilds[0].duration"));
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await page.WaitForFunctionAsync("window.heldChartBuilds.length === 3");
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync("window.heldChartBuilds[2]()");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.chartBuilds.length"));
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task EnlargedBadgeNames_KeepTheirFullTitlesWithoutClipping()
    {
        await using var context = await NewContextAsync(1280, false);
        await AddBadgeFixtureAsync(context);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/michael-lustgarten");
        await Assertions.Expect(page.Locator("#modalBadgeStrip .modal-badge-name")).ToHaveCountAsync(5);
        var clipped = await page.Locator("#modalBadgeStrip .modal-badge-name").EvaluateAllAsync<string[]>("""
            labels => {
                const sizes = labels.map(label => parseFloat(getComputedStyle(label).fontSize) * 2);
                labels.forEach((label, i) => label.style.fontSize = sizes[i] + 'px');
                if (!labels.length) return ['No earned titles rendered'];
                return labels.filter(label => label.scrollHeight > label.clientHeight + 1
                    || label.scrollWidth > label.clientWidth + 1).map(label =>
                    `${label.textContent}: ${label.scrollWidth}x${label.scrollHeight} inside ${label.clientWidth}x${label.clientHeight}`);
            }
            """);
        Assert.Empty(clipped);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task BadgeArrival_NeverHidesTitlesOrDimsEarnedBadgesAfterTheyRender(int width)
    {
        await using var context = await NewContextAsync(width, false);
        await AddBadgeFixtureAsync(context);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/michael-lustgarten");
        await Assertions.Expect(page.Locator("#modalBadgeStrip .modal-badge-name")).ToHaveCountAsync(5);
        await page.Keyboard.PressAsync("Tab");
        var minimum = await page.EvaluateAsync<double[]>("""
            () => new Promise(resolve => {
                const start = performance.now();
                let title = 1, badge = 1;
                const sample = () => {
                    document.querySelectorAll('#modalBadgeStrip .modal-badge-name').forEach(el =>
                        title = Math.min(title, Number(getComputedStyle(el).opacity)));
                    document.querySelectorAll('#modalBadgeStrip .badge-class').forEach(el =>
                        badge = Math.min(badge, Number(getComputedStyle(el).opacity)));
                    if (performance.now() - start < 1600) requestAnimationFrame(sample);
                    else resolve([title, badge]);
                };
                sample();
            })
            """);
        Assert.True(minimum[0] >= .9 && minimum[1] >= .85,
            $"Earned badge content disappeared during its delayed arrival: title opacity {minimum[0]}, badge opacity {minimum[1]}.");
    }

    [Fact]
    public async Task PagerCounterUpdates_MeasureFiguresBeforeChangingTheirStyles()
    {
        await using var context = await NewContextAsync(1440, false);
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                window.counterLayoutAudit = { writes: 0, readsAfterWrite: 0, readsBeforePaint: 0 };
                let wrote = false;
                let beforePaint = false;
                document.addEventListener('click', event => {
                    if (!event.target.closest('[data-step]')) return;
                    beforePaint = true;
                    requestAnimationFrame(() => beforePaint = false);
                }, true);
                for (const name of ['setProperty', 'removeProperty']) {
                    const original = CSSStyleDeclaration.prototype[name];
                    CSSStyleDeclaration.prototype[name] = function(property, ...args) {
                        if (property === '--lwc-count-color') {
                            window.counterLayoutAudit.writes++;
                            wrote = true;
                            queueMicrotask(() => wrote = false);
                        }
                        return original.call(this, property, ...args);
                    };
                }
                for (const name of ['getClientRects', 'getBoundingClientRect']) {
                    const original = Element.prototype[name];
                    Element.prototype[name] = function(...args) {
                        if (this.matches('.age-board-figure,#ageRadarCenterValue,#ageRadarCenterBio')) {
                            if (wrote) window.counterLayoutAudit.readsAfterWrite++;
                            if (beforePaint) window.counterLayoutAudit.readsBeforePaint++;
                        }
                        return original.apply(this, args);
                    };
                }
                const checkVisibility = Element.prototype.checkVisibility;
                Element.prototype.checkVisibility = function(...args) {
                    if (beforePaint && this.id === 'modalProfilePic') window.counterLayoutAudit.readsBeforePaint++;
                    return checkVisibility.apply(this, args);
                };
            }
            """);
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await WaitForProfileAsync(page);
        await page.Locator("#lowestBortzAge").ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => window.counterLayoutAudit.writes > 0");
        var audit = await page.EvaluateAsync<int[]>("""
            () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() =>
                resolve([window.counterLayoutAudit.writes, window.counterLayoutAudit.readsAfterWrite, window.counterLayoutAudit.readsBeforePaint]))))
            """);
        Assert.True(audit[0] > 0, "The real counter update must run during the pager step.");
        Assert.Equal(0, audit[1]);
        Assert.Equal(0, audit[2]);
    }

    [Fact]
    public async Task ClosingPendingStep_CancelsItsCompletionAndRestoresTheCallingPage()
    {
        await using var context = await NewContextAsync(390, false);
        var page = await OpenAsync(context);
        await HoldAthleteReadsAsync(page);
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await ExpectCompactLoadingAsync(page);
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.EvaluateAsync("() => window.pendingAthleteReads.forEach(resolve => resolve(window.heldAthletes))");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("/leaderboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("", await page.EvaluateAsync<string>("document.body.style.position"));
        Assert.False(await page.EvaluateAsync<bool>("!!document.activeElement.closest('[inert]')"));
    }

    [Fact]
    public async Task ReopeningCachedProfile_KeepsTheWholeCardReadableFromItsFirstFrame()
    {
        await using var context = await NewContextAsync(1440, false);
        var page = await OpenAsync(context);
        var slug = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var opacity = await page.EvaluateAsync<double>("""
            async slug => {
                await window.openAthleteModalBySlug(slug);
                const modal = document.getElementById('detailsModal');
                const card = modal.querySelector('.modal-content');
                // Hold the actual entrance at its first frame, including any
                // ancestor fade. A readable child inside a transparent overlay
                // still paints the leaderboard through the profile.
                getComputedStyle(modal).opacity;
                for (const element of [modal, card]) {
                    for (const animation of element.getAnimations()) {
                        animation.pause();
                        animation.currentTime = 0;
                    }
                }
                return Number(getComputedStyle(modal).opacity)
                    * Number(getComputedStyle(card).opacity);
            }
            """, slug);
        Assert.True(opacity >= .85, $"The whole opening card faded through the board: effective opacity {opacity}.");
    }

    [Fact]
    public async Task CloseAnimation_KeepsTheActualContentTogetherAtItsFirstFrame()
    {
        await using var context = await NewContextAsync(1440, false);
        var page = await OpenAsync(context);
        var readable = await page.EvaluateAsync<bool>("""
            () => {
                window.closeModal({fromHistory:true});
                const card = document.querySelector('#detailsModal .modal-content');
                // Inspect the first closing frame synchronously, before its fade.
                return Number(getComputedStyle(card).opacity) > .9
                    && Number(getComputedStyle(document.getElementById('athleteName')).opacity) > .9
                    && !document.querySelector('[data-athlete-motion-copy="surface"]');
            }
            """);
        Assert.True(readable, "The closing card must retain its profile, not become an empty surface with detached identity copies.");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task SwitchingToReducedMotionDuringPaging_SettlesWithoutBlockingNextOrClose()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                const animate = Element.prototype.animate;
                Element.prototype.animate = function(...args) {
                    const animation = animate.apply(this,args);
                    if (this.id === 'athlete-profile') animation.pause();
                    return animation;
                };
            }
            """);
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await WaitForProfileAsync(page);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => document.getElementById('athlete-profile').getAnimations().length === 0");
        var prior = page.Url;
        await page.Locator(".modal-content > .athlete-steps [data-step=previous]").ClickAsync();
        await WaitForProfileAsync(page);
        Assert.NotEqual(prior, page.Url);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }

    private static Task HoldAthleteReadsAsync(IPage page) => page.EvaluateAsync("""
        async () => {
            window.heldAthletes = await window.getSharedAthletes();
            window.pendingAthleteReads = [];
            window.getSharedAthletes = () => new Promise(resolve => window.pendingAthleteReads.push(resolve));
        }
        """);

    private Task AddBadgeFixtureAsync(IBrowserContext context) => RouteAthleteFixtureAsync(context, athletes =>
    {
        var athlete = athletes.OfType<JsonObject>().Single(a => a["AthleteSlug"]!.GetValue<string>() == "michael_lustgarten");
        athlete["Badges"] = new JsonArray(
            new JsonObject { ["BadgeLabel"] = "Age reduction", ["LeagueCategory"] = "Global", ["Place"] = 2 },
            new JsonObject { ["BadgeLabel"] = "Best domain – metabolic", ["Place"] = 1 },
            new JsonObject { ["BadgeLabel"] = "Best domain – immune", ["Place"] = 1 },
            new JsonObject { ["BadgeLabel"] = "Best domain – vitamin D", ["Place"] = 1 },
            new JsonObject { ["BadgeLabel"] = "Pheno pace of aging", ["Place"] = 1 });
    });

    private static async Task ExpectCompactLoadingAsync(IPage page)
    {
        var card = page.Locator("#detailsModal .modal-content");
        await Assertions.Expect(card).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-loading\\b"));
        var height = await card.EvaluateAsync<double>("card => card.getBoundingClientRect().height");
        Assert.InRange(height, 80, 300);
        await Assertions.Expect(page.Locator("#athleteName")).ToHaveTextAsync("Loading...");
        await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#athlete-stats")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").Nth(1).ClickAsync();
        await WaitForProfileAsync(page);
        return page;
    }

    private static Task WaitForProfileAsync(IPage page) => page.WaitForFunctionAsync("""
        () => document.getElementById('detailsModal').style.display === 'block'
            && !document.querySelector('#detailsModal .modal-content').matches('.is-loading, .has-load-error')
        """);

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.NoPreference,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        return context;
    }
}
