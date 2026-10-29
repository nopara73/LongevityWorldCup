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
                    if (window.__motion.holdMotion && (this.dataset?.athleteMotionCopy || this.classList.contains('modal-content') || this.id === 'athlete-profile')) animation.pause();
                } catch { }
                return animation;
            };
            // Playwright may scroll again while making the link actionable.
            // Measure the row at the actual click, before the dialog locks it.
            window.addEventListener('click', event => {
                const row = event.target instanceof Element
                    ? event.target.closest('tr[data-athlete-name], .podium-item[data-athlete-name]') : null;
                if (row && !row.closest('#detailsModal'))
                    window.__motion.source = { rect: row.getBoundingClientRect().toJSON(), scrollY };
            }, true);
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
    public async Task OpeningARow_KeepsTheWholeProfileReadableDuringItsEntrance(string path, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, height, dark, reducedMotion: false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, path);
        var source = path == "/" ? page.Locator(".podium-item[data-athlete-name]").First : page.Locator(Rows + ":visible").Nth(4);
        await source.ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("window.__motion.holdMotion = true");
        await source.Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content').getAnimations().some(a => a.playState === 'paused')");
        await ExpectReadableProfileAsync(page);
        // Controls remain real hit targets in the intermediate frame.
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const close = document.getElementById('closeAthleteDetailsModal');
                const r = close.getBoundingClientRect();
                return document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.closest('button') === close;
            }
            """));
        await page.EvaluateAsync("() => { window.__motion.holdMotion = false; document.getAnimations().filter(a => a.playState === 'paused').forEach(a => a.finish()); }");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1280, 860, false)]
    [InlineData(390, 844, true)]
    public async Task ClosingAfterAStep_KeepsContentAttachedAndReturnsFocusToItsRow(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, height, dark, reducedMotion: false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        await rows.Nth(4).EvaluateAsync("row => row.scrollIntoView({ block: 'center', behavior: 'instant' })");
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 8).map(row => row.dataset.athleteName)");

        await rows.Nth(3).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        var scrolled = await page.EvaluateAsync<double>("() => window.__motion.source.scrollY");
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(names[4]);
        await WaitForProfileAsync(page);
        await page.WaitForFunctionAsync("() => document.getElementById('athlete-profile').getAnimations().every(a => a.playState !== 'running')");

        await ClearLogAsync(page);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();

        // The profile returned to the stepped-to athlete's row, which stayed in view.
        Assert.Equal(scrolled, await page.EvaluateAsync<double>("() => scrollY"), 1.0);
        var target = await page.Locator($"{Rows}[data-athlete-name=\"{names[4]}\"]").BoundingBoxAsync();
        Assert.NotNull(target);
        Assert.DoesNotContain(await ReadAnimationsAsync(page), entry => entry.Copy is not null);

        Assert.Equal(names[4], await page.EvaluateAsync<string?>("() => document.activeElement?.closest('tr')?.dataset.athleteName ?? null"));
        Assert.Equal("athlete-name", await page.EvaluateAsync<string?>("() => document.activeElement?.className ?? null"));
    }

    [Fact]
    public async Task ReducedMotion_ReturnsFromAFarStepToThatRowWithoutAnimating()
    {
        await using var context = await NewContextAsync(390, 844, dark: false, reducedMotion: true);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
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
    public async Task Stepping_MovesTheNewProfileWithoutHidingItOrLeavingAnOldIdentity()
    {
        await using var context = await NewContextAsync(1280, 860, dark: true, reducedMotion: false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 4).map(row => row.dataset.athleteName)");
        await rows.Nth(1).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        foreach (var (key, name) in new[] { ("ArrowRight", names[2]), ("ArrowLeft", names[1]) })
        {
            await ClearLogAsync(page);
            await page.EvaluateAsync("window.__motion.holdMotion = true");
            await page.Keyboard.PressAsync(key);
            await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(name);
            await ExpectReadableProfileAsync(page);
            var entries = await ReadAnimationsAsync(page);
            Assert.Contains(entries, entry => entry.Id == "athlete-profile" && entry.First.Contains("translateX", StringComparison.Ordinal));
            Assert.DoesNotContain(entries, entry => entry.Copy is not null);
            await page.EvaluateAsync("() => { window.__motion.holdMotion = false; document.getAnimations().filter(a => a.playState === 'paused').forEach(a => a.finish()); }");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Swiping_TheProfileStepsToTheNeighbours_AndNextTapCloses(bool reducedMotion)
    {
        await using var context = await NewContextAsync(390, 844, dark: false, reducedMotion);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 4).map(row => row.dataset.athleteName)");
        await rows.Nth(1).Locator("a.athlete-name").TapAsync();
        await WaitForProfileAsync(page);

        var touch = await context.NewCDPSessionAsync(page);
        foreach (var (from, to, name) in new[] { (320, 120, names[2]), (70, 290, names[1]) })
        {
            // Start on noninteractive profile content, not a chart or button
            // whose height can change as the athlete's data renders.
            var titleBox = await page.Locator("#athleteName").BoundingBoxAsync();
            Assert.NotNull(titleBox);
            var gestureY = titleBox!.Y + titleBox.Height / 2;
            await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchStart",
                ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = from, ["y"] = gestureY, ["id"] = 1 } }
            });
            var dragged = 0d;
            for (var step = 1; step <= 10; step++)
            {
                await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchMove",
                    ["touchPoints"] = new[] { new Dictionary<string, object> { ["x"] = from + (to - from) * step / 10, ["y"] = gestureY + 4, ["id"] = 1 } }
                });
                dragged = await page.EvaluateAsync<double>("() => parseFloat(document.getElementById('athlete-profile').style.translate) || 0");
            }
            if (reducedMotion) Assert.Equal(0, dragged);
            else Assert.True(Math.Sign(dragged) == Math.Sign(to - from) && Math.Abs(dragged) > 40, $"The profile should follow the finger, not sit at {dragged}px.");
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
        await touch.DetachAsync();
        // A handled horizontal swipe must not leave a native scroll sequence
        // that consumes the next real tap before it can generate a click.
        await page.Locator("#closeAthleteDetailsModal").TapAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(rows.Nth(1).Locator("a.athlete-name")).ToBeFocusedAsync();
        Assert.NotEqual("hidden", await page.EvaluateAsync<string>("() => getComputedStyle(document.body).overflow"));
    }

    [Fact]
    public async Task Ages_CountInWhenTheyComeIntoViewWhileTheirTextStaysExact()
    {
        await using var context = await NewContextAsync(1440, 900, dark: false, reducedMotion: false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
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
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        var pheno = page.Locator("label[for='view-pheno']").First;
        await pheno.ScrollIntoViewIfNeededAsync();

        foreach (var pass in new[] { "pheno", "default" })
        {
            await ClearLogAsync(page);
            await pheno.ClickAsync();
            await page.WaitForFunctionAsync("() => window.__motion.animations.some(entry => entry.tag === 'TR')");
            var rows = (await ReadAnimationsAsync(page)).Where(entry => entry.Tag == "TR").ToArray();
            var moved = rows.Count(entry => entry.First.Contains("\"translate\":", StringComparison.Ordinal));
            Assert.True(moved >= 2, $"{pass}: rows should glide from their old places ({moved} moved).");
            Assert.All(rows, entry =>
            {
                using var frame = JsonDocument.Parse(entry.Last);
                var translation = frame.RootElement.GetProperty("translate").GetString()!;
                Assert.All(translation.Split(' '), component => Assert.Contains(component, new[] { "0", "0px" }));
            });
            Assert.True(await page.EvaluateAsync<bool>(
                "() => { const rows = window.__motion.animations.filter(entry => entry.tag === 'TR'); return new Set(rows.map(entry => entry.row)).size === rows.length; }"),
                "The shared motion controller should animate each row only once.");
            await page.WaitForTimeoutAsync(400);
        }
    }

    private static Task ClearLogAsync(IPage page) => page.EvaluateAsync("() => { window.__motion.animations.length = 0; window.__motion.counts.length = 0; }");

    [Theory]
    [InlineData("second-touch")]
    [InlineData("cancel")]
    [InlineData("lost-capture")]
    [InlineData("blur")]
    public async Task InterruptedSwipe_DoesNotStepOrLeaveTheProfileDisplaced(string interruption)
    {
        await using var context = await NewContextAsync(390, 844, false, false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        await page.Locator(Rows + ":visible").Nth(1).Locator("a.athlete-name").TapAsync();
        await WaitForProfileAsync(page);
        var name = await page.Locator("#athleteName").InnerTextAsync();
        await page.EvaluateAsync(
            """
            interruption => {
                const profile = document.getElementById('athlete-profile');
                const send = (type, extra = {}) => profile.dispatchEvent(new PointerEvent(type, {
                    bubbles: true, pointerType: 'touch', pointerId: 17, isPrimary: true,
                    clientX: 300, clientY: 450, ...extra
                }));
                send('pointerdown');
                send('pointermove', { clientX: 160 });
                if (interruption === 'second-touch') send('pointerdown', { pointerId: 18, isPrimary: false });
                else if (interruption === 'blur') window.dispatchEvent(new Event('blur'));
                else send(interruption === 'cancel' ? 'pointercancel' : 'lostpointercapture');
                send('pointerup', { clientX: 160 });
            }
            """, interruption);
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(name, await page.Locator("#athleteName").InnerTextAsync());
        Assert.Equal("", await page.Locator("#athlete-profile").EvaluateAsync<string>("p => p.style.translate"));
        Assert.Equal("", await page.Locator("#athlete-profile").EvaluateAsync<string>("p => p.style.opacity"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseThenReopenInTheSameTask_ReleasesOldMotionAndKeepsTheNewDialogLocked(bool reducedMotion)
    {
        await using var context = await NewContextAsync(390, 844, false, reducedMotion);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        var rows = page.Locator(Rows + ":visible");
        var next = await rows.Nth(2).Locator("a.athlete-name").GetAttributeAsync("href");
        // Hold copies when they are created: waiting until profile readiness can
        // miss the entire short animation on a loaded browser worker.
        if (!reducedMotion) await page.EvaluateAsync("() => window.__motion.holdMotion = true");
        await rows.Nth(1).Locator("a.athlete-name").TapAsync();
        await WaitForProfileAsync(page);
        if (!reducedMotion)
        {
            await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content').getAnimations().some(a => a.playState === 'paused')");
        }
        var staleCopies = await page.EvaluateAsync<int>(
            """
            href => {
                window.__motion.holdMotion = false;
                window.closeModal({ fromHistory: true });
                window.openAthleteModalBySlug(href.split('/').pop(), { suppressGuessMyAge: true });
                return document.querySelector('.athlete-motion-layer')?.childElementCount || 0;
            }
            """, next);
        Assert.Equal(0, staleCopies);
        await WaitForProfileAsync(page);
        await page.WaitForTimeoutAsync(600);
        Assert.Equal("fixed", await page.EvaluateAsync<string>("() => document.body.style.position"));
        Assert.True(await page.Locator("#detailsModal").IsVisibleAsync());
        Assert.Equal(1, await page.Locator("#detailsModal .modal-content").EvaluateAsync<double>("c => +getComputedStyle(c).opacity"));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.querySelector('.athlete-motion-layer')?.childElementCount || 0"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("", await page.EvaluateAsync<string>("() => document.body.style.position"));
        Assert.False(await page.EvaluateAsync<bool>("() => document.body.classList.contains('no-scroll')"));
        Assert.False(await page.EvaluateAsync<bool>("() => !!document.activeElement?.closest('[inert]')"));
    }

    [Fact]
    public async Task AnimationFailure_DoesNotLeaveCopiesOrHideRealContent()
    {
        await using var context = await NewContextAsync(1440, 900, false, false);
        await context.AddInitScriptAsync(
            """
            const originalAnimate = Element.prototype.animate;
            Element.prototype.animate = function (...args) {
                if (this.classList.contains('modal-content')) throw new Error('Animation unavailable');
                return originalAnimate.apply(this, args);
            };
            """);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        await page.Locator(Rows + ":visible").Nth(1).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.WaitForTimeoutAsync(500);
        Assert.DoesNotContain(errors, error => error.Contains("Animation unavailable", StringComparison.Ordinal));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.querySelector('.athlete-motion-layer')?.childElementCount || 0"));
        Assert.Equal(1, await page.Locator("#modalProfilePic").EvaluateAsync<double>("p => +getComputedStyle(p).opacity"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("", await page.EvaluateAsync<string>("() => document.body.style.position"));
    }

    [Fact]
    public async Task CountOverlays_PreserveSignedAndMissingValuesAndClearWhenMotionChanges()
    {
        await using var context = await NewContextAsync(1440, 900, false, false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        await page.Locator(Rows + "[data-tier='pro']:visible").Nth(4).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await page.Locator("#athlete-stats").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(600);
        // Synthetic values isolate formatting from the athlete calculation pipeline.
        foreach (var value in new[] { "−5.4", "+3.2", "0.84", "—" })
        {
            // An old counter can tick between separate host calls. Begin the
            // recording window with the value change in one browser task.
            await page.Locator("#lowestBortzAge").EvaluateAsync(
                "(f, value) => { window.__motion.animations.length = 0; window.__motion.counts.length = 0; f.textContent = value; }", value);
            await page.WaitForTimeoutAsync(600);
            Assert.Equal(value, await page.Locator("#lowestBortzAge").InnerTextAsync());
            Assert.False(await page.Locator("#lowestBortzAge").EvaluateAsync<bool>("f => f.hasAttribute('data-lwc-count')"));
            Assert.False(await page.EvaluateAsync<bool>("() => window.__motion.counts.some(c => c.id === 'lowestBortzAge' && c.text !== document.getElementById('lowestBortzAge').textContent)"));
            if (value == "—") Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__motion.counts.filter(c => c.id === 'lowestBortzAge').length"));
        }
        await page.Locator("#lowestBortzAge").EvaluateAsync("f => { f.textContent = '28.0'; }");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-lwc-count]')");
        Assert.Equal("28.0", await page.Locator("#lowestBortzAge").InnerTextAsync());
    }

    [Fact]
    public async Task TouchVerticalPanScrollsWithoutStepping_AndControlsDoNotStartSwipes()
    {
        await using var context = await NewContextAsync(390, 844, false, false);
        var page = await context.NewPageAsync();
        await NavigateToLeaderboardAsync(page, "/leaderboard");
        await page.Locator(Rows + ":visible").Nth(1).Locator("a.athlete-name").TapAsync();
        await WaitForProfileAsync(page);
        var name = await page.Locator("#athleteName").InnerTextAsync();
        var touch = await context.NewCDPSessionAsync(page);
        for (var step = 0; step <= 10; step++)
            await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = step == 0 ? "touchStart" : "touchMove",
                ["touchPoints"] = new[] { new { x = 190, y = 700 - step * 30, id = 1 } }
            });
        await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content').scrollTop > 100");
        Assert.Equal(name, await page.Locator("#athleteName").InnerTextAsync());
        await page.Locator("#shareAthleteProfile").EvaluateAsync(
            """
            control => {
                for (const [type, x] of [['pointerdown', 300], ['pointermove', 100], ['pointerup', 100]])
                    control.dispatchEvent(new PointerEvent(type, { bubbles: true, pointerType: 'touch', pointerId: 19, isPrimary: true, clientX: x, clientY: 450 }));
            }
            """);
        await page.WaitForTimeoutAsync(300);
        Assert.Equal(name, await page.Locator("#athleteName").InnerTextAsync());
        Assert.Equal("", await page.Locator("#athlete-profile").EvaluateAsync<string>("p => p.style.translate"));
    }

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

    private static async Task ExpectReadableProfileAsync(IPage page)
    {
        var failures = await page.EvaluateAsync<string[]>("""
            () => {
                const card = document.querySelector('#detailsModal .modal-content');
                const bounds = card.getBoundingClientRect();
                const errors = [];
                for (const selector of ['#athlete-profile', '#athleteName', '#modalProfilePic', '.athlete-links']) {
                    const element = card.querySelector(selector);
                    const rect = element.getBoundingClientRect();
                    let opacity = 1;
                    for (let node = element; node && node !== card.parentElement; node = node.parentElement)
                        opacity *= Number(getComputedStyle(node).opacity);
                    if (opacity < .85) errors.push(selector + ' is hidden during motion: ' + opacity);
                    if (rect.left < bounds.left - 1 || rect.right > bounds.right + 1) errors.push(selector + ' detached from card');
                }
                for (const copy of document.querySelectorAll('[data-athlete-motion-copy]')) {
                    const rect = copy.getBoundingClientRect();
                    if (rect.width * rect.height > bounds.width * bounds.height / 2 && Number(getComputedStyle(copy).opacity) > .5)
                        errors.push('opaque empty surface covers profile');
                }
                return errors;
            }
            """);
        Assert.Empty(failures);
    }

    private static async Task NavigateToLeaderboardAsync(IPage page, string path)
    {
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, path);
        // The server-rendered podium is replaced when athlete data arrives.
        // Fonts and styles can settle before that replacement, so measure and
        // interact with rows only after the leaderboard finishes hydrating.
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
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
