using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class AestheticAccessibilityBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about", 720)]
    [InlineData("/about", 350)]
    [InlineData("/ruleset", 720)]
    [InlineData("/ruleset", 350)]
    [InlineData("/history", 720)]
    [InlineData("/history", 350)]
    public async Task DesktopDocumentationNavigation_KeepsEveryLinkReachableOnShortScreens(string path, int height)
    {
        await using var context = await NewContextAsync(
            Browser,
            App,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = height }
            });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.EvaluateAsync("window.scrollTo({ top: 400, behavior: 'instant' })");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var navigation = page.Locator(".documentation-nav");
        var navigationBox = await navigation.BoundingBoxAsync();
        Assert.NotNull(navigationBox);
        Assert.True(navigationBox.Y >= 0 && navigationBox.Y + navigationBox.Height <= height - 8,
            $"{path} contents extend beyond the {height}px viewport: y={navigationBox.Y}, height={navigationBox.Height}.");

        var links = navigation.Locator("a");
        var count = await links.CountAsync();
        Assert.True(count > 1);
        await links.First.FocusAsync();
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
                await page.Keyboard.PressAsync("Tab");

            var link = links.Nth(index);
            Assert.True(await link.EvaluateAsync<bool>("element => element === document.activeElement"),
                $"{path} contents link {index + 1} was skipped by keyboard navigation.");
            var linkBox = await link.BoundingBoxAsync();
            Assert.NotNull(linkBox);
            navigationBox = await navigation.BoundingBoxAsync();
            Assert.NotNull(navigationBox);
            Assert.True(linkBox.Y >= navigationBox.Y - 1 &&
                        linkBox.Y + linkBox.Height <= Math.Min(height, navigationBox.Y + navigationBox.Height) + 1,
                $"{path} focused contents link {index + 1} is clipped at {height}px height.");
        }

        Assert.True(await navigation.Locator(".documentation-source-link")
            .EvaluateAsync<bool>("element => element === document.activeElement"),
            "Keyboard navigation must reach the source link at the end of the contents.");
    }

    [Theory]
    [InlineData(720)]
    [InlineData(350)]
    public async Task DesktopDocumentationNavigation_FollowsReadingWithoutMovingTheDocument(int height)
    {
        await using var context = await NewContextAsync(
            Browser,
            App,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = height }
            });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        var navigation = page.Locator(".documentation-nav");
        var links = navigation.Locator("a[href^='#']");
        var count = await links.CountAsync();

        foreach (var index in new[] { count - 5, 0 })
        {
            var link = links.Nth(index);
            var href = await link.GetAttributeAsync("href");
            var requestedScroll = await page.EvaluateAsync<double>(
                """
                href => {
                    const heading = document.getElementById(decodeURIComponent(href.slice(1)));
                    const top = Math.max(0, heading.getBoundingClientRect().top + scrollY - 70);
                    window.scrollTo({top, behavior: 'instant'});
                    return Math.min(top, document.documentElement.scrollHeight - innerHeight);
                }
                """, href);
            await page.WaitForFunctionAsync(
                "href => document.querySelector('.documentation-nav a.is-active')?.getAttribute('href') === href", href);

            var navBox = await navigation.BoundingBoxAsync();
            var linkBox = await link.BoundingBoxAsync();
            Assert.NotNull(navBox);
            Assert.NotNull(linkBox);
            Assert.True(linkBox.Y >= Math.Max(0, navBox.Y) &&
                        linkBox.Y + linkBox.Height <= Math.Min(height, navBox.Y + navBox.Height),
                $"The current section is outside the contents pane at {height}px height.");
            Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - requestedScroll), 0, 1);

            // Browsing the contents independently must not snap back on every document scroll event.
            await navigation.EvaluateAsync("element => element.scrollTop = 0");
            await page.EvaluateAsync("window.scrollTo({top: scrollY + 1, behavior: 'instant'})");
            await page.EvaluateAsync(
                "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            Assert.Equal(0, await navigation.EvaluateAsync<double>("element => element.scrollTop"));
        }
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(1280, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task DocumentContentsNavigation_ArrivesAtTheChosenSectionWithoutIntermediatePaneJumps(int width, bool reducedMotion)
    {
        await using var context = await NewContextAsync(
            Browser,
            App,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = width > 900 ? 350 : 844 },
                ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
            });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        var links = page.Locator(".documentation-nav a[href^='#']");
        var count = await links.CountAsync();
        foreach (var index in new[] { count - 5, count - 1, 0 })
        {
            if (width <= 900)
                await page.Locator(".documentation-nav-toggle").ClickAsync();
            var link = links.Nth(index);
            var href = await link.GetAttributeAsync("href");
            await link.ClickAsync();

            var stableArrival = await page.EvaluateAsync<bool>(
                """
                async href => {
                    const heading = document.getElementById(decodeURIComponent(href.slice(1)));
                    const pane = document.querySelector('.documentation-nav');
                    const documentY = scrollY;
                    const paneY = pane.scrollTop;
                    for (let frame = 0; frame < 10; frame++) {
                        await new Promise(resolve => requestAnimationFrame(resolve));
                        const headingBox = heading.getBoundingClientRect();
                        if (headingBox.top < 69 || headingBox.bottom > innerHeight ||
                            Math.abs(scrollY - documentY) > 1 || Math.abs(pane.scrollTop - paneY) > 1 ||
                            document.querySelector('.documentation-nav a.is-active')?.getAttribute('href') !== href ||
                            document.activeElement !== heading || location.hash !== href) {
                            return false;
                        }
                    }
                    return true;
                }
                """, href);
            Assert.True(stableArrival,
                $"Selecting {href} moved through intermediate document or contents positions (width={width}, reducedMotion={reducedMotion}).");
        }
    }

    [Fact]
    public async Task MobileDocumentationNavigation_UsesProgressiveDisclosureAndLargeTargets()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 390, Height = 844 }
            });
        var page = await context.NewPageAsync();

        await NavigateAndSettleAsync(page, "/history");
        var toggle = page.Locator(".documentation-nav-toggle");
        var links = page.Locator(".documentation-nav-links");
        var heading = page.Locator(".documentation-document h1");

        Assert.True(await toggle.IsVisibleAsync());
        Assert.False(await links.IsVisibleAsync());
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        var toggleBox = await toggle.BoundingBoxAsync();
        var headingBox = await heading.BoundingBoxAsync();
        Assert.NotNull(toggleBox);
        Assert.NotNull(headingBox);
        Assert.True(toggleBox.Height >= 44, $"Documentation disclosure measured {toggleBox.Height}px high.");
        Assert.True(headingBox.Y < 260, $"Collapsed navigation delayed the History heading to y={headingBox.Y}px.");

        await toggle.ClickAsync();
        Assert.True(await links.IsVisibleAsync());
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));

        var targets = page.Locator(".documentation-nav-links a");
        var targetCount = await targets.CountAsync();
        Assert.True(targetCount > 8, "History should retain its detailed document navigation inside the disclosure.");
        for (var index = 0; index < targetCount; index++)
        {
            var box = await targets.Nth(index).BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Height >= 44, $"Documentation navigation target {index + 1} measured {box.Height}px high.");
        }

        var nestedMargin = await page.Locator(".documentation-nav-level-3").First.EvaluateAsync<double>(
            "element => parseFloat(getComputedStyle(element).marginLeft)");
        Assert.True(nestedMargin >= 8, $"Nested documentation hierarchy lost its indent ({nestedMargin}px).");
    }

    [Fact]
    public async Task MobileDocumentationNavigation_RemainsAvailableWithoutJavaScript()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                JavaScriptEnabled = false,
                ViewportSize = new ViewportSize { Width = 390, Height = 844 }
            });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/history", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        Assert.False(await page.Locator(".documentation-nav-toggle").IsVisibleAsync());
        Assert.True(await page.Locator(".documentation-nav-links").IsVisibleAsync());
        Assert.True(await page.Locator(".documentation-source-link").IsVisibleAsync());
        Assert.True(
            await page.Locator(".documentation-nav-links a").CountAsync() > 8,
            "History should expose its full document navigation when JavaScript is unavailable.");
    }

    [Fact]
    public async Task SharedInteractionStates_MeetComputedContrastAcrossLightDarkAndHigherContrastModes()
    {
        var app = App;
        var browser = Browser;

        foreach (var mode in new[] { "light", "dark", "higher-contrast" })
        {
            var options = new BrowserNewContextOptions
            {
                ColorScheme = mode == "dark" ? ColorScheme.Dark : ColorScheme.Light,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            };
            if (mode == "higher-contrast")
            {
                options.Contrast = Contrast.More;
            }

            await using var context = await NewContextAsync(browser, app, options);
            var page = await context.NewPageAsync();
            await NavigateAndSettleAsync(page, "/apply");

            var stateDiagnostics = await MeasureSharedInteractionStatesAsync(page);
            AssertInteractionStateContrast(mode, stateDiagnostics);

            if (mode != "light")
            {
                continue;
            }

            var placeholderContrast = await MeasurePlaceholderContrastAsync(page.Locator("#name"));
            Assert.True(placeholderContrast >= 4.5, $"Placeholder contrast was only {placeholderContrast:F2}:1.");

            await NavigateAndSettleAsync(page, "/leaderboard");
            var badge = page.Locator("a.badge-class, .badge-class.badge-clickable, .badge-class[data-clickable=\"true\"]").First;
            await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await badge.HoverAsync();
            var hoverMotion = await badge.EvaluateAsync<BadgeMotionDiagnostics>(
                """
                element => {
                    const style = getComputedStyle(element);
                    const durations = style.transitionDuration.split(',').map(value => {
                        const trimmed = value.trim();
                        return trimmed.endsWith('ms') ? parseFloat(trimmed) : parseFloat(trimmed) * 1000;
                    });
                    return {
                        AnimationName: style.animationName,
                        Transform: style.transform,
                        LongestTransitionMilliseconds: Math.max(0, ...durations)
                    };
                }
                """);

            Assert.Equal("none", hoverMotion.AnimationName);
            Assert.Equal("none", hoverMotion.Transform);
            Assert.True(
                hoverMotion.LongestTransitionMilliseconds <= 220,
                $"Badge hover transition lasted {hoverMotion.LongestTransitionMilliseconds}ms.");

            await badge.FocusAsync();
            var focus = await badge.EvaluateAsync<FocusStateDiagnostics>(
                """
                element => {
                    const style = getComputedStyle(element);
                    return {
                        AnimationName: style.animationName,
                        OutlineWidth: parseFloat(style.outlineWidth),
                        OutlineStyle: style.outlineStyle
                    };
                }
                """);
            Assert.Equal("none", focus.AnimationName);
            Assert.True(focus.OutlineWidth >= 3);
            Assert.NotEqual("none", focus.OutlineStyle);
        }
    }

    [Fact]
    public async Task SharedPrimaryActionsAndSemanticAccentCopy_MeetTextContrastInLightAndDarkThemes()
    {
        var app = App;
        var browser = Browser;

        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await NewContextAsync(
                browser,
                app,
                new BrowserNewContextOptions
                {
                    ColorScheme = scheme,
                    ViewportSize = new ViewportSize { Width = 390, Height = 844 }
                });
            var page = await context.NewPageAsync();

            await NavigateAndSettleAsync(page, "/");
            var actions = await BrowserContrast.MeasureVisibleTextAsync(
                page,
                ".join-game:not(.scrolled-button)",
                ".enhanced-subscribe-btn");
            Assert.Equal(2, actions.Length);
            BrowserContrast.AssertMinimum($"{scheme} shared action", actions);

            await NavigateAndSettleAsync(page, "/pheno-age");
            var accentCopy = await BrowserContrast.MeasureVisibleTextAsync(page, ".blood-sport-accent");
            Assert.Single(accentCopy);
            BrowserContrast.AssertMinimum($"{scheme} semantic danger copy", accentCopy);
        }
    }

    [Fact]
    public async Task SlowAndOfflineEventRequests_ShowLoadingAndRecoveryStates()
    {
        var app = App;
        var browser = Browser;

        await using (var slowContext = await NewContextAsync(
                         browser,
                         app,
                         new BrowserNewContextOptions
                         {
                             ViewportSize = new ViewportSize { Width = 390, Height = 844 }
                         }))
        {
            var eventRequestCount = 0;
            var eventRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var duplicateEventRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseEventRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await slowContext.RouteAsync("**/api/events", async route =>
            {
                var requestCount = Interlocked.Increment(ref eventRequestCount);
                eventRequestStarted.TrySetResult();
                if (requestCount >= 2)
                    duplicateEventRequestStarted.TrySetResult();

                await releaseEventRequest.Task;
                await route.ContinueAsync();
            });
            try
            {
                var slowPage = await slowContext.NewPageAsync();
                await slowPage.GotoAsync("/events", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await eventRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var loadingRoot = slowPage.Locator("#events-root[aria-busy=\"true\"]");
                await loadingRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                Assert.Equal("Loading events...", await slowPage.Locator("#eventsStatus").InnerTextAsync());
                var duplicateRequest = slowPage.EvaluateAsync<int>(
                    "async () => (await fetch('/api/events')).status");
                await duplicateEventRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(Volatile.Read(ref eventRequestCount) >= 2);

                releaseEventRequest.TrySetResult();
                Assert.Equal(200, await duplicateRequest);
                await slowPage.Locator("#events-root[aria-busy=\"false\"]").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            }
            finally
            {
                // A failed start waiter or assertion must release every routed
                // request before the context begins asynchronous disposal.
                releaseEventRequest.TrySetResult();
            }
        }

        await using (var offlineContext = await NewContextAsync(
                         browser,
                         app,
                         new BrowserNewContextOptions
                         {
                             ViewportSize = new ViewportSize { Width = 390, Height = 844 }
                         }))
        {
            await offlineContext.RouteAsync("**/api/events", route => route.AbortAsync("internetdisconnected"));
            var offlinePage = await offlineContext.NewPageAsync();
            await NavigateAndSettleAsync(offlinePage, "/events");
            var retry = offlinePage.Locator(".events-retry-button");
            await retry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            var retryBox = await retry.BoundingBoxAsync();
            Assert.NotNull(retryBox);
            Assert.True(retryBox.Height >= 44);
            Assert.Equal("alert", await offlinePage.Locator("#eventsStatus").GetAttributeAsync("role"));
            Assert.Contains("could not load", await offlinePage.Locator("#eventsStatus").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

}
