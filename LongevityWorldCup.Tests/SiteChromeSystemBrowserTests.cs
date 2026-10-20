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
[Collection(BrowserTestCollections.WorkloadB)]
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
                return [brand.left, logo.left, play.right, document.documentElement.clientWidth, play.top + play.height / 2, logo.top + logo.height / 2];
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
    public async Task StickyBar_KeepsTheLockupAndPlayOnTheContentColumn(int width, int height, bool dark)
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
                return [logo.left, document.documentElement.clientWidth - play.right, parseFloat(getComputedStyle(title).borderLeftWidth),
                    play.top - bar.top, bar.bottom - play.bottom, parseFloat(getComputedStyle(document.getElementById('site-sticky-header')).borderBottomWidth)];
            }
            """);
        Assert.InRange(box[0], gutter - 2, gutter + 2);
        if (width > 640) Assert.InRange(box[1], gutter - 2, gutter + 2);
        Assert.Equal(1, box[2]);
        Assert.InRange(Math.Abs(box[3] - box[4]), 0, 2);
        Assert.Equal(1, box[5]);
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
                return [header.width, brand.left + brand.width / 2, play.width, play.top - brand.bottom, play.left + play.width / 2];
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
                    CrestHidden: crest.getAttribute('aria-hidden') === 'true' && mark.alt === ''
                };
            }
            """);
        Assert.Equal(1, rail.RailBorder);
        Assert.True(rail.RailRadius >= 8);
        Assert.Equal(1, rail.Rows);
        Assert.Equal(5, rail.Dividers);
        Assert.True(rail.EqualHeights);
        Assert.True(rail.CrestCentered && rail.CrestOnEdge, "The crest should sit centred on the footer's top edge.");
        Assert.True(rail.CrestHidden, "The decorative crest should stay out of the accessibility tree.");
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
    public async Task StickyArrival_KeepsItsMeasuredEdgeAndPlayAlignedDuringMotion(int width)
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
                    samples.push([b.top, Math.abs(a.top + a.height / 2 - b.top - b.height / 2)]);
                }
                return samples;
            }
            """);
        Assert.NotEmpty(offsets);
        Assert.All(offsets, sample =>
        {
            Assert.InRange(Math.Abs(sample[0]), 0, 0.5);
            Assert.InRange(sample[1], 0, 1);
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
        public bool CrestHidden { get; set; }
    }
}
