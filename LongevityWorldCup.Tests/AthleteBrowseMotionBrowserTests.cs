using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// Browsing the competition as one motion: a row grows into the athlete's
// profile and shrinks back into the row of whoever is on screen, steps slide
// athletes in and out (also by swipe), ages count in, rows glide when the
// ranking changes, and scroll and focus come back to the right row. Reduced
// motion keeps the returns and drops every animation.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteBrowseMotionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Rows = ".leaderboard table tbody tr[data-athlete-name]";

    // Records every Web Animations call and every counting frame, so the
    // assertions do not race the 200-300ms motion on a loaded machine.
    private const string Recorder =
        """
        (() => {
            window.__motion = { animations: [], counts: [] };
            const animate = Element.prototype.animate;
            Element.prototype.animate = function (keyframes, options) {
                const animation = animate.call(this, keyframes, options);
                try {
                    const frames = Array.isArray(keyframes) ? keyframes : [];
                    window.__motion.animations.push({
                        copy: this.dataset?.athleteMotionCopy ?? null,
                        tag: this.tagName,
                        id: this.id || '',
                        row: this.dataset?.athleteName ?? '',
                        first: JSON.stringify(frames[0] ?? {}),
                        last: JSON.stringify(frames[frames.length - 1] ?? {})
                    });
                } catch { }
                return animation;
            };
            new MutationObserver(records => records.forEach(record => {
                const figure = record.target;
                if (figure.dataset.lwcCount) {
                    window.__motion.counts.push({ id: figure.id, shown: figure.dataset.lwcCount, text: figure.textContent });
                }
            })).observe(document, { subtree: true, attributes: true, attributeFilter: ['data-lwc-count'] });
            localStorage.setItem('gmaSkipAll', 'true');
        })();
        """;

    [Theory]
    [InlineData("/leaderboard", 1440, 900, false)]
    [InlineData("/leaderboard", 390, 844, true)]
    [InlineData("/", 1440, 900, false)]
    public async Task OpeningARow_GrowsItsPortraitNameAndRankIntoTheProfile(string path, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, height, dark, reducedMotion: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, path);

        var source = path == "/" ? page.Locator(".podium-item[data-athlete-name]").First : page.Locator(Rows + ":visible").Nth(4);
        await source.ScrollIntoViewIfNeededAsync();
        var rowBox = await source.BoundingBoxAsync();
        Assert.NotNull(rowBox);
        await ClearLogAsync(page);
        await source.Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.WaitForFunctionAsync("() => window.__motion.animations.some(entry => entry.copy === 'portrait')");

        var log = await ReadAnimationsAsync(page);
        Assert.Contains(log, entry => entry.Copy == "surface");
        Assert.Contains(log, entry => entry.Copy == "name");
        Assert.Contains(log, entry => entry.Copy == "rank");

        // The card's copy starts as the tapped row and grows to the card.
        var surface = log.First(entry => entry.Copy == "surface");
        Assert.InRange(Px(surface.First, "top"), rowBox!.Y - 2, rowBox.Y + 2);
        Assert.InRange(Px(surface.First, "height"), rowBox.Height - 2, rowBox.Height + 2);

        // The portrait copy starts at the row's small portrait: scaled down, then released.
        var portrait = log.First(entry => entry.Copy == "portrait");
        Assert.Contains("translate(", portrait.First, StringComparison.Ordinal);
        Assert.InRange(Scale(portrait.First), 0.05, 0.9);
        Assert.Contains("\"transform\":\"none\"", portrait.Last, StringComparison.Ordinal);

        // The real header waits under its copy, and the row's own portrait lifts off.
        Assert.Contains(log, entry => entry.Id == "modalProfilePic" && entry.First.Contains("\"opacity\":0", StringComparison.Ordinal));
        Assert.Contains(log, entry => entry.Id == "athleteName" && entry.First.Contains("\"opacity\":0", StringComparison.Ordinal));

        // Moving copies never take input, and the dialog is usable while they fly.
        var layer = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const layer = document.querySelector('.athlete-motion-layer');
                const close = document.getElementById('closeAthleteDetailsModal').getBoundingClientRect();
                const hit = document.elementFromPoint(close.left + close.width / 2, close.top + close.height / 2);
                return { pointer: getComputedStyle(layer).pointerEvents, inert: layer.inert, hidden: layer.getAttribute('aria-hidden'), hit: !!hit?.closest('#detailsModal') };
            }
            """);
        Assert.Equal("none", layer.GetProperty("pointer").GetString());
        Assert.True(layer.GetProperty("inert").GetBoolean());
        Assert.Equal("true", layer.GetProperty("hidden").GetString());
        Assert.True(layer.GetProperty("hit").GetBoolean(), "The close button must take input during the morph.");

        // Everything lands and clears up.
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('.athlete-motion-layer').childElementCount === 0
                && parseFloat(getComputedStyle(document.getElementById('modalProfilePic')).opacity) === 1
                && parseFloat(getComputedStyle(document.querySelector('#detailsModal .modal-content')).opacity) === 1
            """);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1280, 860, false)]
    [InlineData(390, 844, true)]
    public async Task ClosingAfterAStep_ShrinksIntoTheRowOnScreenAndFocusesIt(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, height, dark, reducedMotion: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        await rows.Nth(4).ScrollIntoViewIfNeededAsync();
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 8).map(row => row.dataset.athleteName)");
        var scrolled = await page.EvaluateAsync<double>("() => scrollY");

        await rows.Nth(3).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(names[4]);
        await WaitForProfileAsync(page);
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-motion-layer')?.childElementCount === 0");

        await ClearLogAsync(page);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();

        // The profile returned to the stepped-to athlete's row, which stayed in view.
        Assert.Equal(scrolled, await page.EvaluateAsync<double>("() => scrollY"), 1.0);
        var target = await page.Locator($"{Rows}[data-athlete-name=\"{names[4]}\"]").BoundingBoxAsync();
        Assert.NotNull(target);
        var log = await ReadAnimationsAsync(page);
        var surface = log.First(entry => entry.Copy == "surface");
        Assert.InRange(Px(surface.Last, "top"), target!.Y - 2, target.Y + 2);
        Assert.InRange(Px(surface.Last, "height"), target.Height - 2, target.Height + 2);
        Assert.Contains(log, entry => entry.Copy == "portrait");
        Assert.Contains(log, entry => entry.Copy == "name");

        Assert.Equal(names[4], await page.EvaluateAsync<string?>("() => document.activeElement?.closest('tr')?.dataset.athleteName ?? null"));
        Assert.Equal("athlete-name", await page.EvaluateAsync<string?>("() => document.activeElement?.className ?? null"));
    }

    [Fact]
    public async Task ReducedMotion_ReturnsFromAFarStepToThatRowWithoutAnimating()
    {
        await using var context = await NewContextAsync(390, 844, dark: false, reducedMotion: true);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        await rows.Nth(1).ScrollIntoViewIfNeededAsync();
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 20).map(row => row.dataset.athleteName)");

        await rows.Nth(1).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.EvaluateAsync("() => document.getElementById('athlete-stats').scrollIntoView()");
        for (var index = 2; index <= 16; index++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
            await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(names[index]);
            await WaitForProfileAsync(page);
        }
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();

        var row = page.Locator($"{Rows}[data-athlete-name=\"{names[16]}\"]");
        var box = await row.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box!.Y >= 844 * 0.15 && box.Y + box.Height <= 844 * 0.9,
            $"The athlete on screen at close should be in view, not at y={box.Y:0}.");
        Assert.Equal(names[16], await page.EvaluateAsync<string?>("() => document.activeElement?.closest('tr')?.dataset.athleteName ?? null"));

        var motion = await page.EvaluateAsync<JsonElement>(
            """
            () => ({
                copies: window.__motion.animations.filter(entry => entry.copy !== null).length,
                counts: window.__motion.counts.length,
                layer: document.querySelector('.athlete-motion-layer')?.childElementCount ?? 0
            })
            """);
        Assert.Equal(0, motion.GetProperty("copies").GetInt32());
        Assert.Equal(0, motion.GetProperty("counts").GetInt32());
        Assert.Equal(0, motion.GetProperty("layer").GetInt32());
    }

    [Fact]
    public async Task Stepping_SlidesTheOldAthleteOutTowardsWhereItCameFrom()
    {
        await using var context = await NewContextAsync(1280, 860, dark: true, reducedMotion: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 4).map(row => row.dataset.athleteName)");
        await rows.Nth(1).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);

        foreach (var (key, name, sign) in new[] { ("ArrowRight", names[2], -1), ("ArrowLeft", names[1], 1) })
        {
            await page.WaitForFunctionAsync("() => document.querySelector('.athlete-motion-layer')?.childElementCount === 0");
            await ClearLogAsync(page);
            await page.Keyboard.PressAsync(key);
            await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(name);
            var leaving = (await ReadAnimationsAsync(page)).Where(entry => entry.Copy == "leaving").ToArray();
            Assert.Equal(2, leaving.Length);
            foreach (var entry in leaving)
            {
                using var last = JsonDocument.Parse(entry.Last);
                var shift = double.Parse(last.RootElement.GetProperty("translate").GetString()!.Split("px")[0], CultureInfo.InvariantCulture);
                Assert.True(Math.Sign(shift) == sign && Math.Abs(shift) >= 40, $"{key}: the old athlete should leave by {sign * 40}px or more, not {shift}px.");
            }
            await page.WaitForFunctionAsync("() => document.getElementById('athlete-profile').getAnimations().every(animation => animation.playState !== 'running')");
        }
    }

    [Fact]
    public async Task Swiping_TheProfileStepsToTheNeighbours()
    {
        await using var context = await NewContextAsync(390, 844, dark: false, reducedMotion: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 4).map(row => row.dataset.athleteName)");
        await rows.Nth(1).Locator("a.athlete-name").TapAsync();
        await WaitForProfileAsync(page);

        var touch = await context.NewCDPSessionAsync(page);
        foreach (var (from, to, name) in new[] { (320, 120, names[2]), (70, 290, names[1]) })
        {
            await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchStart",
                ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = from, ["y"] = 620, ["id"] = 1 } }
            });
            var dragged = 0d;
            for (var step = 1; step <= 10; step++)
            {
                await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchMove",
                    ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = from + (to - from) * step / 10, ["y"] = 624, ["id"] = 1 } }
                });
                dragged = await page.EvaluateAsync<double>("() => parseFloat(document.getElementById('athlete-profile').style.translate) || 0");
            }
            Assert.True(Math.Sign(dragged) == Math.Sign(to - from) && Math.Abs(dragged) > 40, $"The profile should follow the finger, not sit at {dragged}px.");
            await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchEnd",
                ["touchPoints"] = Array.Empty<object>()
            });
            await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(name);
            await WaitForProfileAsync(page);
            Assert.Equal("", await page.EvaluateAsync<string>("() => document.getElementById('athlete-profile').style.translate"));
        }

        // A vertical drag scrolls the profile and never steps.
        Assert.Equal("pan-y pinch-zoom", await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('athlete-profile')).touchAction"));
    }

    [Fact]
    public async Task Ages_CountInWhenTheyComeIntoViewWhileTheirTextStaysExact()
    {
        await using var context = await NewContextAsync(1440, 900, dark: false, reducedMotion: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var row = page.Locator($"{Rows}[data-tier='pro']:visible").Nth(4);
        await row.Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.EvaluateAsync("() => document.getElementById('athlete-stats').scrollIntoView({ block: 'center' })");
        await page.WaitForFunctionAsync(
            """
            () => window.__motion.counts.some(entry => entry.id === 'lowestBortzAge')
                && !document.querySelector('#detailsModal [data-lwc-count]')
            """);

        var result = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const counts = window.__motion.counts.filter(entry => entry.id === 'lowestBortzAge');
                return {
                    chrono: parseFloat(document.getElementById('chronologicalAge').textContent),
                    final: document.getElementById('lowestBortzAge').textContent,
                    first: parseFloat(counts[0].shown),
                    steps: new Set(counts.map(entry => entry.shown)).size,
                    textChanged: counts.some(entry => entry.text !== document.getElementById('lowestBortzAge').textContent),
                    after: getComputedStyle(document.getElementById('lowestBortzAge')).color
                };
            }
            """);
        // A biological age counts from the calendar age to the result.
        Assert.Equal(result.GetProperty("chrono").GetDouble(), result.GetProperty("first").GetDouble(), 0.6);
        Assert.True(result.GetProperty("steps").GetInt32() >= 4, "The age should visibly count, not jump.");
        Assert.False(result.GetProperty("textChanged").GetBoolean(), "The figure's own text must stay the final value while it counts.");
        Assert.NotEqual("rgba(0, 0, 0, 0)", result.GetProperty("after").GetString());
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task ChangingTheRanking_RowsGlideToTheirNewPlaces(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, height, dark, reducedMotion: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var pheno = page.Locator("label[for='view-pheno']").First;
        await pheno.ScrollIntoViewIfNeededAsync();

        foreach (var pass in new[] { "pheno", "default" })
        {
            await ClearLogAsync(page);
            await pheno.ClickAsync();
            await page.WaitForFunctionAsync("() => window.__motion.animations.some(entry => entry.tag === 'TR')");
            var rows = (await ReadAnimationsAsync(page)).Where(entry => entry.Tag == "TR").ToArray();
            var moved = rows.Count(entry => entry.First.Contains("translate(", StringComparison.Ordinal));
            Assert.True(moved >= 2, $"{pass}: rows should glide from their old places ({moved} moved).");
            Assert.All(rows, entry => Assert.Contains("\"transform\":\"none\"", entry.Last, StringComparison.Ordinal));
            await page.WaitForTimeoutAsync(400);
        }
    }

    private static Task ClearLogAsync(IPage page) => page.EvaluateAsync("() => { window.__motion.animations.length = 0; window.__motion.counts.length = 0; }");

    private static async Task<MotionEntry[]> ReadAnimationsAsync(IPage page)
    {
        var entries = await page.EvaluateAsync<JsonElement>("() => window.__motion.animations");
        return entries.EnumerateArray().Select(entry => new MotionEntry(
            entry.GetProperty("copy").ValueKind == JsonValueKind.String ? entry.GetProperty("copy").GetString() : null,
            entry.GetProperty("tag").GetString()!,
            entry.GetProperty("id").GetString()!,
            entry.GetProperty("first").GetString()!,
            entry.GetProperty("last").GetString()!)).ToArray();
    }

    private static double Px(string keyframe, string property)
    {
        using var document = JsonDocument.Parse(keyframe);
        return double.Parse(document.RootElement.GetProperty(property).GetString()!.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
    }

    private static double Scale(string keyframe)
    {
        using var document = JsonDocument.Parse(keyframe);
        var transform = document.RootElement.GetProperty("transform").GetString()!;
        var start = transform.IndexOf("scale(", StringComparison.Ordinal) + "scale(".Length;
        return double.Parse(transform[start..transform.IndexOf(')', start)], CultureInfo.InvariantCulture);
    }

    private static Task WaitForProfileAsync(IPage page) => page.WaitForFunctionAsync(
        "() => { const content = document.querySelector('#detailsModal .modal-content'); return document.getElementById('detailsModal').style.display === 'block' && content && !content.classList.contains('is-loading'); }");

    private async Task<IBrowserContext> NewContextAsync(int width, int height, bool dark, bool reducedMotion)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(Recorder);
        return context;
    }

    private sealed record MotionEntry(string? Copy, string Tag, string Id, string First, string Last);
}
