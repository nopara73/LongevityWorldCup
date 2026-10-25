using Microsoft.Playwright;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The header, its sticky bar, the footer and the outage cards form one chrome
/// system: one dark band, one content column and one lockup on every page.
/// </summary>
[Collection(BrowserTestCollections.WorkloadD)]
public sealed class SiteChromeSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 1440, 900, false)]
    [InlineData("/leaderboard", 1440, 900, true)]
    [InlineData("/about", 1440, 900, false)]
    [InlineData("/about", 1280, 720, true)]
    [InlineData("/ruleset", 1024, 768, false)]
    public async Task Header_AlignsTheLockupAndPlayToTheContentColumn(string path, int width, int height, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, height, dark);
        var page = await OpenAsync(context, path);
        var gutter = Math.Max(24, (width - 1152) / 2.0);
        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const brand = document.querySelector('header[role=banner] .header-link').getBoundingClientRect();
                const logo = document.querySelector('header[role=banner] .main-logo-image').getBoundingClientRect();
                const play = document.querySelector('header[role=banner] .join-game:not(.scrolled-button)').getBoundingClientRect();
                return [brand.left, logo.left, document.querySelector('header .site-header-actions').getBoundingClientRect().right, document.documentElement.clientWidth, play.top + play.height / 2, logo.top + logo.height / 2];
            }
            """);
        Assert.InRange(box[1], gutter - 2, gutter + 2);
        Assert.InRange(box[3] - box[2], gutter - 2, gutter + 2);
        Assert.InRange(Math.Abs(box[4] - box[5]), 0, 2);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/about", 1440, 900, false)]
    [InlineData("/leaderboard", 1280, 720, true)]
    [InlineData("/leaderboard", 768, 1024, false)]
    [InlineData("/ruleset", 844, 390, true)]
    public async Task Header_StacksTheUnchangedTaglineUnderTheWordmarkBesideAHairline(string path, int width, int height, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, height, dark);
        var page = await OpenAsync(context, path);
        var lockup = await page.EvaluateAsync<LockupDiagnostics>(
            """
            () => {
                const link = document.querySelector('header[role=banner] .header-link');
                const word = link.querySelector('.bannertext').getBoundingClientRect();
                const tagline = link.querySelector('.tagline');
                const tag = tagline.getBoundingClientRect();
                const logo = link.querySelector('.main-logo-image').getBoundingClientRect();
                const rule = getComputedStyle(link, '::before');
                return {
                    TaglineText: tagline.textContent.trim(),
                    TaglineVisible: tag.width > 0 && getComputedStyle(tagline).display !== 'none',
                    TaglineBelow: tag.top >= word.bottom - 1,
                    LeftAligned: Math.abs(tag.left - word.left) <= 1,
                    Transform: getComputedStyle(tagline).textTransform,
                    RuleWidth: parseFloat(rule.width),
                    RuleBetween: rule.content !== 'none' && logo.right < word.left - 8,
                    PlayText: document.querySelector('header[role=banner] .join-game:not(.scrolled-button)').innerText.replace(/\s+/g, ' ').trim()
                };
            }
            """);
        Assert.Equal("longevity leaderboards", lockup.TaglineText);
        Assert.True(lockup.TaglineVisible, $"{path} at {width}px hid the tagline.");
        Assert.True(lockup.TaglineBelow && lockup.LeftAligned, $"{path} at {width}px did not stack the tagline under the wordmark.");
        Assert.Equal("uppercase", lockup.Transform);
        Assert.Equal(1, lockup.RuleWidth);
        Assert.True(lockup.RuleBetween);
        Assert.Equal("PLAY THE GAME", lockup.PlayText);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chrome_SharesOneSurfaceAndHairlineAcrossHeaderStickyBarAndFooter(bool dark)
    {
        string? surface = null;
        foreach (var path in new[] { "/", "/leaderboard", "/about", "/pheno-age" })
        {
            await using var context = await NewChromeContextAsync(1280, 720, dark);
            var page = await OpenAsync(context, path);
            var header = await page.Locator("header[role=banner]").EvaluateAsync<string[]>(
                "h => { const s = getComputedStyle(h); return [s.backgroundImage, s.borderBottomWidth, s.borderBottomStyle]; }");
            Assert.Contains("linear-gradient", header[0]);
            Assert.Equal("1px", header[1]);
            Assert.Equal("solid", header[2]);
            surface ??= header[0];
            Assert.Equal(surface, header[0]);
            if (path == "/pheno-age") continue;
            var footer = await page.Locator(".footer").EvaluateAsync<string[]>(
                "f => { const s = getComputedStyle(f); return [s.backgroundImage, s.borderTopWidth]; }");
            Assert.Equal(surface, footer[0]);
            Assert.Equal("1px", footer[1]);
        }
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(390, 844, false)]
    public async Task StickyBar_KeepsNavigationAndPlayTogetherOnTheContentColumn(int width, int height, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, height, dark);
        var page = await OpenAsync(context, "/about");
        await page.Mouse.WheelAsync(0, 1600);
        await page.WaitForFunctionAsync("() => document.getElementById('site-sticky-header').getAttribute('aria-hidden') === 'false'");
        await page.EvaluateAsync("async () => { for (let i = 0; i < 4; i++) await new Promise(requestAnimationFrame); }");
        var gutter = width <= 640 ? 16 : Math.Max(24, (width - 1152) / 2.0);
        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const logo = document.querySelector('.site-sticky-header-logo').getBoundingClientRect();
                const title = document.querySelector('.site-sticky-header-title');
                const play = document.querySelector('.scrolled-button').getBoundingClientRect();
                const bar = document.getElementById('site-sticky-header').getBoundingClientRect();
                return [logo.left, document.documentElement.clientWidth - document.querySelector('.site-sticky-menu-toggle').getBoundingClientRect().right, parseFloat(getComputedStyle(title).borderLeftWidth),
                    play.top - bar.top, bar.bottom - play.bottom, parseFloat(getComputedStyle(document.getElementById('site-sticky-header')).borderBottomWidth)];
            }
            """);
        Assert.InRange(box[0], gutter - 2, gutter + 2);
        if (width > 640) Assert.InRange(box[1], gutter - 2, gutter + 2);
        Assert.Equal(1, box[2]);
        Assert.InRange(box[3], 0, 100);
        Assert.InRange(box[4], 0, 100);
        Assert.Equal(1, box[5]);
        await StickyPlayScrollBrowserTests.AssertControlsAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(320, 640, true)]
    [InlineData(560, 844, false)]
    public async Task HomepagePhoneHeader_CentersTheLockupOverAFullWidthInvitation(int width, int height, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, height, dark, mobile: true);
        var page = await OpenAsync(context, "/");
        var box = await page.EvaluateAsync<double[]>(
            """
            () => {
                const header = document.querySelector('header[role=banner]').getBoundingClientRect();
                const brand = document.querySelector('header[role=banner] .header-link').getBoundingClientRect();
                const play = document.querySelector('header[role=banner] .join-game:not(.scrolled-button)').getBoundingClientRect();
                return [header.width, brand.left + brand.width / 2, document.querySelector('header .site-header-actions').getBoundingClientRect().width, play.top - brand.bottom, document.querySelector('header .site-header-actions').getBoundingClientRect().left + document.querySelector('header .site-header-actions').getBoundingClientRect().width / 2];
            }
            """);
        Assert.InRange(Math.Abs(box[1] - box[0] / 2), 0, 2);
        Assert.InRange(Math.Abs(box[4] - box[0] / 2), 0, 2);
        Assert.True(box[2] >= Math.Min(26 * 16, box[0] - 32) - 1, $"Play spans {box[2]:F0}px of a {box[0]:F0}px header.");
        Assert.True(box[3] >= 8, "Play should sit below the lockup.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/about", 1440, 900, false)]
    [InlineData("/leaderboard", 1024, 768, true)]
    public async Task Footer_SetsThePagesInOneRuledRailUnderThePhoenixCrest(string path, int width, int height, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, height, dark);
        var page = await OpenAsync(context, path);
        await page.Locator(".footer").ScrollIntoViewIfNeededAsync();
        var rail = await page.EvaluateAsync<RailDiagnostics>(
            """
            () => {
                const footer = document.querySelector('.footer').getBoundingClientRect();
                const column = document.querySelector('.footer-column--explore');
                const style = getComputedStyle(column);
                const links = [...column.querySelectorAll('.footer-link')];
                const boxes = links.map(l => l.getBoundingClientRect());
                const crest = document.querySelector('.footer-crest');
                const crestBox = crest.getBoundingClientRect();
                const mark = crest.querySelector('img');
                return {
                    RailBorder: parseFloat(style.borderTopWidth),
                    RailRadius: parseFloat(style.borderTopLeftRadius),
                    Rows: new Set(boxes.map(b => Math.round(b.top))).size,
                    Dividers: links.slice(1).filter(l => parseFloat(getComputedStyle(l).borderLeftWidth) === 1).length,
                    EqualHeights: boxes.every(b => Math.abs(b.height - boxes[0].height) < 1),
                    CrestCentered: Math.abs(crestBox.left + crestBox.width / 2 - (footer.left + footer.width / 2)) < 1.5,
                    CrestOnEdge: Math.abs(crestBox.top + crestBox.height / 2 - footer.top) < 1.5,
                    CrestNamed: crest.getAttribute('aria-label') === 'Back to top' && crest.tagName === 'A' && mark.alt === ''
                };
            }
            """);
        Assert.Equal(1, rail.RailBorder);
        Assert.True(rail.RailRadius >= 8);
        Assert.Equal(1, rail.Rows);
        Assert.Equal(5, rail.Dividers);
        Assert.True(rail.EqualHeights);
        Assert.True(rail.CrestCentered && rail.CrestOnEdge, "The crest should sit centred on the footer's top edge.");
        Assert.True(rail.CrestNamed, "The back-to-top crest must have an accessible name and decorative image.");
        await page.WaitForFunctionAsync("() => { const i = document.querySelector('.footer-crest img'); return i.complete && i.naturalWidth > 0; }");
    }

    [Theory]
    [InlineData(768, 3, false)]
    [InlineData(390, 2, true)]
    [InlineData(320, 2, false)]
    public async Task Footer_RailBecomesARuledTableOnSmallerScreens(int width, int columns, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, 844, dark, mobile: width < 600);
        var page = await OpenAsync(context, "/ruleset");
        await page.Locator(".footer").ScrollIntoViewIfNeededAsync();
        var cells = await page.Locator(".footer-column--explore .footer-link").EvaluateAllAsync<double[][]>(
            """
            links => links.map(l => {
                const b = l.getBoundingClientRect(), s = getComputedStyle(l);
                return [b.left, b.top, b.width, parseFloat(s.borderLeftWidth), parseFloat(s.borderTopWidth)];
            })
            """);
        var rows = cells.GroupBy(c => Math.Round(c[1])).ToArray();
        Assert.Equal(6 / columns, rows.Length);
        Assert.All(rows, row => Assert.Equal(columns, row.Count()));
        Assert.All(cells, c => Assert.InRange(c[2], cells[0][2] - 1, cells[0][2] + 1));
        for (var i = 0; i < cells.Length; i++)
        {
            Assert.Equal(i % columns == 0 ? 0 : 1, cells[i][3]);
            Assert.Equal(i < columns ? 0 : 1, cells[i][4]);
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/error/502.html", 1440, 900, false)]
    [InlineData("/error/503.html", 390, 844, true)]
    [InlineData("/error/504.html", 768, 1024, false)]
    public async Task OutageCards_OpenWithTheChromeBandAndAQuietCodeChip(string path, int width, int height, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, height, dark);
        var page = await OpenAsync(context, path, settle: false);
        await page.Locator(".mark img").EvaluateAsync("i => i.decode()");
        var band = await page.EvaluateAsync<string[]>(
            """
            () => {
                const bar = getComputedStyle(document.querySelector('.card-bar'));
                const brand = getComputedStyle(document.querySelector('.brand'));
                const mark = getComputedStyle(document.querySelector('.mark img'));
                const code = getComputedStyle(document.querySelector('.code'));
                const visual = getComputedStyle(document.querySelector('.visual'));
                return [bar.backgroundImage, brand.color, mark.filter, code.borderTopLeftRadius, code.textTransform, visual.boxShadow, getComputedStyle(document.querySelector('h1')).fontWeight];
            }
            """);
        Assert.Contains("linear-gradient", band[0]);
        Assert.Equal("rgb(255, 255, 255)", band[1]);
        Assert.Equal("invert(1)", band[2]);
        Assert.Equal("999px", band[3]);
        Assert.Equal("uppercase", band[4]);
        Assert.Equal("none", band[5]);
        Assert.Equal("700", band[6]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(1440)]
    public async Task StickyArrival_KeepsPlayInsideTheStableNavigationBar(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await OpenAsync(context, "/history");
        var offsets = await page.EvaluateAsync<double[][]>(
            """
            async () => {
                scrollTo(0, 600);
                const samples = [];
                for (let i = 0; i < 20; i++) {
                    await new Promise(requestAnimationFrame);
                    const bar = document.getElementById('site-sticky-header');
                    if (bar.getAttribute('aria-hidden') !== 'false') continue;
                    const b = bar.getBoundingClientRect();
                    const a = document.querySelector('.scrolled-button').getBoundingClientRect();
                    samples.push([b.top, a.top - b.top, b.bottom - a.bottom]);
                }
                return samples;
            }
            """);
        Assert.NotEmpty(offsets);
        Assert.All(offsets, sample =>
        {
            Assert.InRange(Math.Abs(sample[0]), 0, 0.5);
            Assert.InRange(sample[1], 0, 100);
            Assert.InRange(sample[2], 0, 100);
        });
    }

    [Theory]
    [InlineData(".site-sticky-header-link", "header[role=banner] .header-link")]
    [InlineData(".scrolled-button", ".join-game:not(.scrolled-button)")]
    public async Task StickyLinks_ReturnFocusToTheirVisibleBannerCounterpart(string stickySelector, string bannerSelector)
    {
        await using var context = await NewChromeContextAsync(1440, 900, dark: false);
        var page = await OpenAsync(context, "/history");
        await page.EvaluateAsync("scrollTo(0, 600)");
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "false");
        await page.Locator(stickySelector).FocusAsync();
        await page.EvaluateAsync("scrollTo(0, 0)");
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "true");
        await Assertions.Expect(page.Locator(bannerSelector)).ToBeFocusedAsync();
        Assert.True(await page.Locator("#site-sticky-header").EvaluateAsync<bool>("e => e.inert"));
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(390, true)]
    public async Task Footer_KeyboardNamesAndEscapeAlsoWorkWithATouchPrimaryPointer(int width, bool dark)
    {
        await using var context = await NewChromeContextAsync(width, 844, dark, mobile: true);
        var page = await OpenAsync(context, "/ruleset");
        Assert.True(await page.EvaluateAsync<bool>("matchMedia('(hover: none)').matches"));
        await page.Keyboard.PressAsync("Tab");
        var platform = page.Locator(".footer-follow-links--primary .footer-link").First;
        await platform.FocusAsync();
        var label = platform.Locator(".footer-link-label");
        Assert.Equal("none", await label.EvaluateAsync<string>("e => getComputedStyle(e).clipPath"));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("inset(50%)", await label.EvaluateAsync<string>("e => getComputedStyle(e).clipPath"));
        await Assertions.Expect(platform).ToBeFocusedAsync();
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(195)]
    [InlineData(320)]
    public async Task Footer_NarrowLayoutsKeepEveryRealLinkTargetInsideTheViewport(int width)
    {
        await using var context = await NewChromeContextAsync(width, 844, dark: true, mobile: true);
        var page = await OpenAsync(context, "/ruleset");
        var clipped = await page.Locator(".footer .footer-link").EvaluateAllAsync<string[]>(
            """
            links => links.filter(link => {
                const b = link.getBoundingClientRect();
                return b.left < -0.5 || b.right > innerWidth + 0.5 || b.width < 44 || b.height < 44;
            }).map(link => link.getAttribute('aria-label'))
            """);
        Assert.Empty(clipped);
        var platforms = await page.Locator(".footer-follow-links--primary .footer-link").EvaluateAllAsync<string[]>(
            "links => links.map(link => link.textContent.trim())");
        Assert.Equal(["Instagram", "YouTube", "X"], platforms);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task Footer_FeaturedLinksWrapWhenEnlargedIconsNeedMoreThanTheirColumn()
    {
        await using var context = await NewChromeContextAsync(1440, 900, dark: true);
        var page = await OpenAsync(context, "/ruleset");
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.Locator(".footer-column--follow").EvaluateAsync(
            "e => { e.style.inlineSize = '230px'; e.style.maxInlineSize = 'none'; }");
        var layout = await page.Locator(".footer-column--follow").EvaluateAsync<double[][]>(
            """
            column => {
                const bounds = column.getBoundingClientRect();
                return [...column.querySelectorAll('.footer-follow-links--primary .footer-link')].map(link => {
                    const b = link.getBoundingClientRect();
                    return [b.left - bounds.left, bounds.right - b.right, b.top, b.width, b.height];
                });
            }
            """);
        Assert.Equal(3, layout.Length);
        Assert.All(layout, link =>
        {
            Assert.True(link[0] >= -0.5 && link[1] >= -0.5, "A featured link left its column.");
            Assert.True(link[3] >= 44 && link[4] >= 44);
        });
        Assert.True(layout[2][2] > layout[0][2], "The last featured link needs another row.");
    }

    [Theory]
    [InlineData(320, 640, 200)]
    [InlineData(844, 390, 200)]
    [InlineData(195, 422, 100)]
    public async Task Footer_KeyboardFocusKeepsOneReadableNameWhenThePointerRestsOnANeighbor(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context, "/ruleset");
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.Keyboard.PressAsync("Tab");
        var platforms = page.Locator(".footer-follow-links--primary .footer-link");
        await platforms.Nth(0).FocusAsync();
        await platforms.Nth(1).HoverAsync();
        var visible = await page.Locator(".footer-column--follow .footer-link-label").EvaluateAllAsync<string[]>(
            "labels => labels.filter(e => getComputedStyle(e).clipPath === 'none').map(e => e.textContent.trim())");
        Assert.Equal(["Instagram"], visible);
        await Assertions.Expect(platforms.Nth(0)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await page.Locator(".footer-column--follow .footer-link-label").EvaluateAllAsync<bool>(
            "labels => labels.every(e => getComputedStyle(e).clipPath === 'inset(50%)')"));
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task OutageCards_LoadTheirCompleteDesignUsingOnlyNginxFallbackAliases(int code)
    {
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var snippet = await File.ReadAllTextAsync(Path.Combine(webRoot, "..", "Scripts", "lwc-error-page.conf"));
        var aliases = Regex.Matches(snippet, @"location = (\S+) \{\s*(?:internal;\s*)?alias /var/www/longevityworldcup-errors/(\S+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var requested = new List<string>();
        await using var context = await NewChromeContextAsync(390, 844, dark: true);
        await context.RouteAsync("**/*", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            requested.Add(path);
            if (path == "/") path = $"/{code}.html";
            if (!aliases.TryGetValue(path, out var name))
            {
                await route.FulfillAsync(new() { Status = 502, Body = "Application unavailable" });
                return;
            }
            var source = name switch
            {
                "error-system.css" => "css/error-system.css",
                "favicon-128x128.png" => "assets/favicon-128x128.png",
                "Roboto-Regular.woff2" or "Roboto-Bold.woff2" => $"assets/fonts/{name}",
                _ => $"error/{name}"
            };
            var contentType = Path.GetExtension(name) switch
            {
                ".css" => "text/css",
                ".png" => "image/png",
                ".woff2" => "font/woff2",
                _ => "text/html"
            };
            await route.FulfillAsync(new() { Status = 200, ContentType = contentType, BodyBytes = await File.ReadAllBytesAsync(Path.Combine(webRoot, source)) });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("http://outage.test/");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await document.fonts.load('400 16px Roboto'); await document.fonts.load('700 32px Roboto'); }");
        Assert.Contains("linear-gradient", await page.Locator(".card-bar").EvaluateAsync<string>("e => getComputedStyle(e).backgroundImage"));
        Assert.Equal("invert(1)", await page.Locator(".mark img").EvaluateAsync<string>("e => getComputedStyle(e).filter"));
        Assert.Contains("/css/error-system.css", requested);
        Assert.Contains("/assets/fonts/Roboto-Regular.woff2", requested);
        Assert.Contains("/assets/fonts/Roboto-Bold.woff2", requested);
        Assert.All(requested.Where(p => p != "/"), p => Assert.True(aliases.ContainsKey(p), $"{p} fell through to the unavailable application."));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(502, 320, 200)]
    [InlineData(503, 390, 200)]
    [InlineData(504, 195, 100)]
    [InlineData(503, 1440, 200)]
    public async Task OutageBand_KeepsTheBrandAndStatusWholeWhenTextOrZoomNeedsMoreRows(int code, int width, int textPercent)
    {
        await using var context = await NewChromeContextAsync(width, 844, dark: true);
        var page = await OpenAsync(context, $"/error/{code}.html", settle: false);
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const bar = document.querySelector('.card-bar').getBoundingClientRect();
                const brand = document.querySelector('.brand').getBoundingClientRect();
                const code = document.querySelector('.code').getBoundingClientRect();
                if (brand.left < code.right && code.left < brand.right && brand.top < code.bottom && code.top < brand.bottom)
                    problems.push('Brand and status overlap');
                for (const element of document.querySelectorAll('.brand, .code')) {
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    let text;
                    while (text = walker.nextNode()) {
                        if (!text.textContent.trim()) continue;
                        const range = document.createRange(); range.selectNodeContents(text);
                        for (const r of range.getClientRects()) {
                            if (r.width && (r.left < bar.left || r.right > bar.right || r.top < bar.top || r.bottom > bar.bottom))
                                problems.push(text.textContent.trim() + ' clipped by band');
                        }
                        for (const match of text.textContent.matchAll(/\S+/g)) {
                            const word = document.createRange();
                            word.setStart(text, match.index); word.setEnd(text, match.index + match[0].length);
                            if (word.getClientRects().length > 1) problems.push(match[0] + ' split across lines');
                        }
                    }
                }
                return problems;
            }
            """);
        Assert.Empty(problems);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/", 160)]
    [InlineData("/", 195)]
    [InlineData("/about", 160)]
    [InlineData("/about", 195)]
    public async Task ZoomNarrowHeader_KeepsEveryBrandWordWhole(string path, int width)
    {
        await using var context = await NewChromeContextAsync(width, 844, dark: false);
        var page = await OpenAsync(context, path);
        var problems = await page.Locator("header[role=banner] .bannertext").EvaluateAsync<string[]>(
            """
            element => {
                const band = element.closest('header').getBoundingClientRect();
                const text = element.firstChild;
                const problems = [];
                for (const match of text.textContent.matchAll(/\S+/g)) {
                    const word = document.createRange();
                    word.setStart(text, match.index); word.setEnd(text, match.index + match[0].length);
                    const lines = [...word.getClientRects()];
                    if (lines.length !== 1) problems.push(match[0] + ' split across lines');
                    if (lines.some(r => r.left < band.left || r.right > band.right || r.top < band.top || r.bottom > band.bottom))
                        problems.push(match[0] + ' clipped by banner');
                }
                return problems;
            }
            """);
        Assert.Empty(problems);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/", 160)]
    [InlineData("/", 195)]
    [InlineData("/about", 160)]
    [InlineData("/about", 195)]
    public async Task ZoomNarrowHeader_StillContainsAllGlyphsWithAdditionalTextEnlargement(string path, int width)
    {
        await using var context = await NewChromeContextAsync(width, 844, dark: true);
        var page = await OpenAsync(context, path);
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        var clipped = await page.Locator("header[role=banner] .bannertext").EvaluateAsync<bool>(
            """
            element => {
                const band = element.closest('header').getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(element);
                return [...range.getClientRects()].some(r => r.width > 0 &&
                    (r.left < band.left || r.right > band.right || r.top < band.top || r.bottom > band.bottom));
            }
            """);
        Assert.False(clipped, $"{path} at {width}px clipped its enlarged brand.");
        var headerOverflow = await page.Locator("header[role=banner]").EvaluateAsync<double>(
            "e => e.scrollWidth - e.clientWidth");
        Assert.InRange(headerOverflow, 0, 1);
    }

    private Task<IBrowserContext> NewChromeContextAsync(int width, int height, bool dark, bool? mobile = null)
    {
        var touch = mobile ?? width < 600;
        return NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = touch,
            HasTouch = touch
        });
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string path, bool settle = true)
    {
        var page = await context.NewPageAsync();
        if (settle) await NavigateAndSettleAsync(page, path);
        else await page.GotoAsync(path);
        await page.EvaluateAsync("async () => { await document.fonts.ready; for (let i = 0; i < 3; i++) await new Promise(requestAnimationFrame); }");
        return page;
    }

    public sealed class LockupDiagnostics
    {
        public string TaglineText { get; set; } = "";
        public bool TaglineVisible { get; set; }
        public bool TaglineBelow { get; set; }
        public bool LeftAligned { get; set; }
        public string Transform { get; set; } = "";
        public double RuleWidth { get; set; }
        public bool RuleBetween { get; set; }
        public string PlayText { get; set; } = "";
    }

    public sealed class RailDiagnostics
    {
        public double RailBorder { get; set; }
        public double RailRadius { get; set; }
        public int Rows { get; set; }
        public int Dividers { get; set; }
        public bool EqualHeights { get; set; }
        public bool CrestCentered { get; set; }
        public bool CrestOnEdge { get; set; }
        public bool CrestNamed { get; set; }
    }
}
