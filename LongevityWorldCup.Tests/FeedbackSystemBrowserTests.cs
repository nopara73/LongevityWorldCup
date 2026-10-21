using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// Banners, field errors, the copied toast and the loading dialog are one family:
// the same mark, the same tones, the same card geometry on every page.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FeedbackSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    // [rail width, radius, mark size, mark glyph, mark colour, background, tone-soft]
    private const string BannerShapeScript = """
        element => {
            const probe = document.createElement('span');
            document.body.append(probe);
            const tone = name => { probe.style.color = `var(--lwc-${name})`; return getComputedStyle(probe).color; };
            const style = getComputedStyle(element);
            const mark = getComputedStyle(element, '::before');
            const soft = ['danger-soft', 'warning-soft', 'success-soft', 'accent-soft'].map(tone);
            const result = [
                style.borderInlineStartWidth,
                style.borderTopLeftRadius,
                mark.width,
                mark.content.split(' / ')[0].replaceAll('"', ''),
                mark.backgroundColor,
                style.backgroundColor,
                String(soft.includes(style.backgroundColor))
            ];
            probe.remove();
            return result;
        }
        """;

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(390, 844, "dark")]
    public async Task Banners_ShareOneShapeMarkAndTintAcrossPages(int width, int height, string theme)
    {
        await using var context = await NewThemedContextAsync(width, height, theme);
        var shapes = new List<(string Where, string[] Shape)>();

        var home = await context.NewPageAsync();
        await NavigateAndSettleAsync(home, "/");
        await home.EvaluateAsync("""
            () => {
                const status = document.getElementById('newsletterStatus');
                status.textContent = 'Couldn’t confirm the subscription. Try again.';
                status.classList.add('is-error');
            }
            """);
        shapes.Add(("newsletter", await home.Locator("#newsletterStatus").EvaluateAsync<string[]>(BannerShapeScript)));

        var unsubscribe = await context.NewPageAsync();
        await unsubscribe.RouteAsync("**/api/home/unsubscribe", route => route.FulfillAsync(new() { Status = 500, Body = "" }));
        await NavigateAndSettleAsync(unsubscribe, "/unsubscribe");
        await unsubscribe.Locator("#email").FillAsync("athlete@example.com");
        await unsubscribe.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(unsubscribe.Locator("#message")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-error"));
        shapes.Add(("unsubscribe", await unsubscribe.Locator("#message").EvaluateAsync<string[]>(BannerShapeScript)));
        await ExpectNoHorizontalOverflowAsync(unsubscribe);

        var events = await context.NewPageAsync();
        await events.RouteAsync("**/api/events", route => route.FulfillAsync(new() { Status = 500, Body = "" }));
        await NavigateAndSettleAsync(events, "/events");
        var recovery = events.Locator(".events-recovery");
        await Assertions.Expect(recovery).ToBeVisibleAsync();
        shapes.Add(("events", await recovery.EvaluateAsync<string[]>(BannerShapeScript)));
        var overlap = await recovery.EvaluateAsync<double>("""
            element => {
                const mark = getComputedStyle(element, '::before');
                const words = element.querySelector('p').getBoundingClientRect();
                const box = element.getBoundingClientRect();
                return box.left + parseFloat(getComputedStyle(element).borderInlineStartWidth) + parseFloat(getComputedStyle(element).paddingInlineStart) + parseFloat(mark.width) - words.left;
            }
            """);
        Assert.True(overlap <= 0, $"The events retry row draws its words {overlap}px under the mark.");
        await ExpectNoHorizontalOverflowAsync(events);

        foreach (var (where, shape) in shapes)
        {
            Assert.True(shape[0] == "4px", $"{where}: rail {shape[0]}");
            Assert.True(shape[1] == "8px", $"{where}: radius {shape[1]}");
            Assert.True(shape[2] == "22px", $"{where}: mark {shape[2]}");
            Assert.True(shape[3] == "✕", $"{where}: glyph '{shape[3]}'");
            Assert.True(shape[6] == "true", $"{where}: background {shape[5]} is not a tone tint");
        }

        Assert.Single(shapes.Select(shape => shape.Shape[4]).Distinct());
        Assert.Single(shapes.Select(shape => shape.Shape[5]).Distinct());
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task FieldErrors_CarryTheMarkAndTurnTheirInputRed(string theme)
    {
        await using var context = await NewThemedContextAsync(390, 844, theme);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("""
            () => {
                for (const [id, text] of [['name', 'Name must be at least 3 characters.'], ['flag', 'Please select a flag.']]) {
                    document.getElementById(id).setAttribute('aria-invalid', 'true');
                    document.getElementById(id + 'Error').textContent = text;
                }
            }
            """);

        foreach (var id in new[] { "name", "flag" })
        {
            var field = await page.Locator($"#{id}Error").EvaluateAsync<string[]>("""
                element => {
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--lwc-danger)';
                    document.body.append(probe);
                    const danger = getComputedStyle(probe).color;
                    probe.remove();
                    const mark = getComputedStyle(element, '::before');
                    const style = getComputedStyle(element);
                    return [danger, style.color, style.backgroundColor, style.borderInlineStartWidth,
                        mark.content.split(' / ')[0].replaceAll('"', ''), mark.backgroundColor];
                }
                """);
            Assert.Equal(field[0], field[1]);
            Assert.Equal("rgba(0, 0, 0, 0)", field[2]);
            Assert.Equal("0px", field[3]);
            Assert.Equal("✕", field[4]);
            Assert.Equal(field[0], field[5]);

            await page.WaitForFunctionAsync("""
                id => {
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--lwc-danger)';
                    document.body.append(probe);
                    const danger = getComputedStyle(probe).color;
                    probe.remove();
                    return getComputedStyle(document.getElementById(id)).borderTopColor === danger;
                }
                """, id, new() { Timeout = 5000 });
        }
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(390, 844, "dark")]
    public async Task LoadingDialog_IsTheAlertPanelWithItsMedallionTurning(int width, int height, string theme)
    {
        await using var context = await NewThemedContextAsync(width, height, theme);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await page.EvaluateAsync("() => { showLoading(); }");
        await Assertions.Expect(page.Locator("#loading-dialog")).ToBeVisibleAsync();

        var loading = await page.EvaluateAsync<string[]>("""
            () => {
                const panel = getComputedStyle(document.querySelector('.loading-dialog-panel'));
                const loader = getComputedStyle(document.querySelector('.loader'));
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-accent)';
                document.body.append(probe);
                const accent = getComputedStyle(probe).color;
                probe.style.color = 'var(--lwc-surface)';
                const surface = getComputedStyle(probe).color;
                probe.remove();
                return [panel.borderTopWidth, panel.borderTopColor, accent, panel.borderTopLeftRadius,
                    panel.backgroundColor, surface, loader.width, loader.borderTopColor];
            }
            """);
        await page.EvaluateAsync("() => { hideLoading(); void customAlert('Proof upload failed. Please try again with an image or PDF file.'); }");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeVisibleAsync();
        var alert = await page.EvaluateAsync<string[]>("""
            () => {
                const panel = getComputedStyle(document.querySelector('.custom-alert-panel'));
                const mark = getComputedStyle(document.querySelector('.custom-alert-mark'));
                return [panel.borderTopWidth, panel.borderTopLeftRadius, mark.width];
            }
            """);

        Assert.Equal(alert[0], loading[0]);
        Assert.Equal(loading[2], loading[1]);
        Assert.Equal(alert[1], loading[3]);
        Assert.Equal(loading[5], loading[4]);
        Assert.Equal(alert[2], loading[6]);
        Assert.Equal(loading[2], loading[7]);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task CopiedConfirmation_IsASuccessPillWithTheSharedMark(string theme)
    {
        await using var context = await NewThemedContextAsync(390, 844, theme);
        await context.RouteAsync("**/api/bitcoin/donation-address", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = "{\"address\":\"bc1qfeedbackfixture\"}"
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await page.EvaluateAsync("""
            () => Object.defineProperty(navigator, 'clipboard', {
                configurable: true, value: { writeText: async () => {} }
            })
            """);
        await page.Clock.InstallAsync();
        await page.Locator("#bitcoinCopyButton").ClickAsync();
        await Assertions.Expect(page.Locator("#copiedText")).ToHaveTextAsync("Copied");
        await Assertions.Expect(page.Locator("#bitcoinCopyStatus")).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(page.Locator("#bitcoinCopyStatus")).ToHaveAttributeAsync("aria-live", "polite");
        Assert.Null(await page.Locator("#bitcoinCopyStatus").GetAttributeAsync("hidden"));
        Assert.DoesNotContain("✓", await page.Locator("#bitcoinCopyStatus").AriaSnapshotAsync());
        var copied = await page.EvaluateAsync<string[]>("""
            () => {
                const element = document.getElementById('copiedText');
                const probe = document.createElement('span');
                document.body.append(probe);
                probe.style.color = 'var(--lwc-success)';
                const success = getComputedStyle(probe).color;
                probe.style.color = 'var(--lwc-success-soft)';
                const soft = getComputedStyle(probe).color;
                probe.remove();
                const style = getComputedStyle(element);
                const mark = getComputedStyle(element, '::before');
                const pill = element.getBoundingClientRect().width < element.closest('.btc-address').getBoundingClientRect().width;
                return [style.display.endsWith('flex') && pill ? 'pill' : style.display, style.backgroundColor, soft, style.borderTopLeftRadius,
                    mark.content.split(' / ')[0].replaceAll('"', ''), mark.backgroundColor, success];
            }
            """);

        Assert.Equal("pill", copied[0]);
        Assert.Equal(copied[2], copied[1]);
        Assert.Equal("999px", copied[3]);
        Assert.Equal("✓", copied[4]);
        Assert.Equal(copied[6], copied[5]);
        await page.Clock.FastForwardAsync(1500);
        await page.Locator("#bitcoinCopyButton").ClickAsync();
        await page.Clock.FastForwardAsync(600);
        await Assertions.Expect(page.Locator("#copiedText")).ToBeVisibleAsync();
        await page.Clock.FastForwardAsync(1400);
        await Assertions.Expect(page.Locator("#copiedText")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#copiedText")).ToBeEmptyAsync();
        await Assertions.Expect(page.Locator("#btcAddressLink")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task NoMatchingAthlete_IsAStatusRatherThanAnInvalidField(string theme)
    {
        await using var context = await NewThemedContextAsync(390, 844, theme);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEnabledAsync();
        await input.FillAsync("zzzz no matching athlete zzzz");
        await Assertions.Expect(page.Locator("#playAthleteError")).ToHaveTextAsync("No matching athlete.");
        await input.BlurAsync();
        Assert.NotEqual("true", await input.GetAttributeAsync("aria-invalid"));
        await page.WaitForFunctionAsync("""
            () => {
                const field = document.getElementById('playAthleteInput');
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-danger)';
                document.body.append(probe);
                const validBorder = getComputedStyle(field).borderTopColor !== getComputedStyle(probe).color;
                probe.remove();
                return validBorder;
            }
            """);
        Assert.Equal("none", await page.Locator("#playAthleteError").EvaluateAsync<string>(
            "element => getComputedStyle(element, '::before').content"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loading_RepeatedCallsRestoreFocusAndPreserveTheExistingScrollLock(bool initiallyLocked)
    {
        await using var context = await NewThemedContextAsync(390, 844, "dark");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.Locator("#dob-year").FocusAsync();
        await page.EvaluateAsync("locked => { document.body.classList.toggle('no-scroll', locked); showLoading(); showLoading(); hideLoading(); hideLoading(); }", initiallyLocked);
        await Assertions.Expect(page.Locator("#dob-year")).ToBeFocusedAsync();
        Assert.Equal(initiallyLocked, await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
        await page.EvaluateAsync("() => { showLoading(); document.getElementById('dob-month').focus(); hideLoading(); }");
        await Assertions.Expect(page.Locator("#dob-month")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task LoadingAndAlert_TransferFocusAndScrollLockInEitherOrder(bool loadingFirst, bool loadingClosesFirst)
    {
        await using var context = await NewThemedContextAsync(390, 844, "dark");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.Locator("#dob-year").FocusAsync();
        await page.EvaluateAsync("first => { if (first) showLoading(); void customAlert('Verification error'); if (!first) showLoading(); }", loadingFirst);
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        if (loadingClosesFirst)
        {
            await page.EvaluateAsync("() => hideLoading()");
            await page.Locator("#custom-alert-close").ClickAsync();
        }
        else
        {
            await page.Locator("#custom-alert-close").ClickAsync();
            await Assertions.Expect(page.Locator(".loading-dialog-panel")).ToBeFocusedAsync();
            await page.EvaluateAsync("() => hideLoading()");
        }
        await Assertions.Expect(page.Locator("#dob-year")).ToBeFocusedAsync();
        Assert.False(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
    }

    [Theory]
    [InlineData(160, 284)]
    [InlineData(390, 195)]
    public async Task Loading_FitsNarrowAndShortViewportsAndRespectsReducedMotion(int width, int height)
    {
        await using var context = await NewThemedContextAsync(width, height, "dark");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await page.EvaluateAsync("() => showLoading()");
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Loading", Exact = true })).ToBeVisibleAsync();
        Assert.True(await page.Locator(".loading-dialog-panel").EvaluateAsync<bool>("""
            panel => {
                const box = panel.getBoundingClientRect();
                const mark = panel.querySelector('.loader');
                const disc = mark.getBoundingClientRect();
                return box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight
                    && disc.left - 7 >= box.left && disc.right + 7 <= box.right
                    && !panel.getAnimations().length && !mark.getAnimations().length;
            }
            """));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".loading-dialog-panel")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task AthleteLoadFailure_UsesTheReadableBannerAndKeepsRetryUsable(string theme)
    {
        await using var context = await NewThemedContextAsync(390, 844, theme);
        var fail = true;
        await context.RouteAsync("**/api/data/athletes", route => fail ? route.AbortAsync() : route.ContinueAsync());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/athlete/michael-lustgarten");
        var error = page.Locator("#athleteLoadError");
        await Assertions.Expect(error).ToBeVisibleAsync();
        var shape = await error.EvaluateAsync<string[]>(BannerShapeScript);
        Assert.Equal("4px", shape[0]);
        Assert.Equal("✕", shape[3]);
        Assert.True(await error.EvaluateAsync<bool>("""
            element => {
                const words = element.querySelector('p');
                const range = document.createRange();
                range.selectNodeContents(words);
                const text = range.getClientRects()[0];
                const box = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                const mark = getComputedStyle(element, '::before');
                const right = box.left + parseFloat(style.borderLeftWidth) + parseFloat(mark.left) + parseFloat(mark.width);
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-ink)';
                document.body.append(probe);
                const readable = getComputedStyle(words).color === getComputedStyle(probe).color;
                probe.remove();
                return readable && text.left >= right + 4 && text.right <= box.right;
            }
            """));
        Assert.DoesNotContain("✕", await error.AriaSnapshotAsync());
        await page.Locator("#retryAthleteLoad").FocusAsync();
        await Assertions.Expect(page.Locator("#retryAthleteLoad")).ToBeFocusedAsync();
        fail = false;
        await page.Locator("#retryAthleteLoad").ClickAsync();
        await Assertions.Expect(error).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1440, 900, "dark")]
    [InlineData(390, 844, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(160, 284, "light")]
    [InlineData(160, 284, "dark")]
    public async Task DirectAthleteLoading_RendersItsMedallionClearOfTheCloseControl(int width, int height, string theme)
    {
        await using var context = await NewThemedContextAsync(width, height, theme);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await NavigateAndSettleAsync(page, "/athlete/michael-lustgarten?guessmyage=1");
            var panel = page.Locator("#detailsModal .modal-content[data-server-rendered-loading].is-loading");
            await Assertions.Expect(panel).ToBeVisibleAsync();
            Assert.NotEqual("none", await panel.EvaluateAsync<string>("element => getComputedStyle(element, '::after').display"));

            // A pseudo-element can have the expected computed size while a
            // shared display:none rule prevents it from drawing any box.
            var cdp = await context.NewCDPSessionAsync(page);
            try
            {
                var document = (await cdp.SendAsync("DOM.getDocument", new() { ["depth"] = 0 }))!.Value;
                var node = (await cdp.SendAsync("DOM.querySelector", new()
                {
                    ["nodeId"] = document.GetProperty("root").GetProperty("nodeId").GetInt32(),
                    ["selector"] = "#detailsModal .modal-content"
                }))!.Value;
                var description = (await cdp.SendAsync("DOM.describeNode", new()
                {
                    ["nodeId"] = node.GetProperty("nodeId").GetInt32(), ["depth"] = 0
                }))!.Value;
                var mark = description.GetProperty("node").GetProperty("pseudoElements").EnumerateArray()
                    .Single(element => element.GetProperty("pseudoType").GetString() == "after");
                var box = (await cdp.SendAsync("DOM.getBoxModel", new()
                {
                    ["backendNodeId"] = mark.GetProperty("backendNodeId").GetInt32()
                }))!.Value.GetProperty("model");
                Assert.Equal(56, box.GetProperty("width").GetInt32());
                Assert.Equal(56, box.GetProperty("height").GetInt32());
                var points = box.GetProperty("border").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                var close = Assert.IsType<LocatorBoundingBoxResult>(await page.Locator("#closeAthleteDetailsModal").BoundingBoxAsync());
                var horizontalOverlap = Math.Min(points[2] + 7, close.X + close.Width) - Math.Max(points[0] - 7, close.X);
                var verticalOverlap = Math.Min(points[5] + 7, close.Y + close.Height) - Math.Max(points[1] - 7, close.Y);
                Assert.True(horizontalOverlap <= 0 || verticalOverlap <= 0, "The medallion's halo must clear Close.");
                Assert.True(points[0] - 7 >= 0 && points[2] + 7 <= width && points[5] + 7 <= height,
                    "The medallion and its halo must remain inside the viewport.");
            }
            finally { await cdp.DetachAsync(); }
            await page.Locator("#closeAthleteDetailsModal").ClickAsync();
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        }
        finally { release.TrySetResult(); }
    }

    private Task<IBrowserContext> NewThemedContextAsync(int width, int height, string theme) =>
        NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
}
