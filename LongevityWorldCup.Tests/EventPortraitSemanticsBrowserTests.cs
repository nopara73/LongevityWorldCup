using System.Text.Json;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class EventPortraitSemanticsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 1440, "light")]
    [InlineData("/", 1440, "dark")]
    [InlineData("/", 390, "light")]
    [InlineData("/", 390, "dark")]
    [InlineData("/events", 1440, "light")]
    [InlineData("/events", 1440, "dark")]
    [InlineData("/events", 390, "light")]
    [InlineData("/events", 390, "dark")]
    public async Task MixedFeed_PreservesBrandPixelsAndAthleteIdentityAcrossEnhancementAndRerenders(
        string path, int width, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = width < 600 ? 844 : 900 },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light
        });
        await context.AddInitScriptAsync($"localStorage.setItem('theme',{JsonSerializer.Serialize(theme)});localStorage.setItem('gmaSkipAll','true');");
        var generation = 0;
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new[]
            {
                new { Id=$"brand-{generation}", Type=6, Text="LWC announcements are now on [Instagram](https://www.instagram.com/longevityworldcup/)", OccurredAt=DateTime.UtcNow, Relevance=15 },
                new { Id=$"photo-{generation}", Type=1, Text="slug[real_athlete]", OccurredAt=DateTime.UtcNow.AddHours(-1), Relevance=5 },
                new { Id=$"missing-{generation}", Type=1, Text="slug[missing_athlete]", OccurredAt=DateTime.UtcNow.AddHours(-2), Relevance=5 },
                new { Id=$"donation-{generation}", Type=3, Text="tx[test] sats[8455]", OccurredAt=DateTime.UtcNow.AddHours(-3), Relevance=5 },
                new { Id=$"milestone-{generation}", Type=4, Text="count[200]", OccurredAt=DateTime.UtcNow.AddHours(-4), Relevance=5 }
            })
        }));
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [{"AthleteSlug":"real_athlete","Name":"Real Athlete","CurrentPlacement":1,"ProfilePicThumb":"/semantic-athlete.svg"},
                 {"AthleteSlug":"missing_athlete","Name":"Missing Athlete","CurrentPlacement":2,"ProfilePicThumb":"/assets/HdLogo_thumb_sm.png"}]
                """
        }));
        await context.RouteAsync("**/semantic-athlete.svg", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml",
            Body = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><rect width='96' height='96' fill='teal'/></svg>"
        }));
        // Capture the approved artwork and orange frame before this layer is
        // initialized. The test then loads the real component on the real page.
        await context.RouteAsync("**/js/portrait-system.js*", route =>
            route.Request.Url.Contains("semantic-probe=1", StringComparison.Ordinal)
                ? route.ContinueAsync()
                : route.FulfillAsync(new() { ContentType = "application/javascript", Body = "" }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link=>link.sheet)");
        await page.EvaluateAsync("() => document.fonts.ready");
        var logo = page.Locator($"tr[data-event-id='brand-{generation}'] img.custom-event-avatar");
        await Assertions.Expect(logo).ToHaveCountAsync(1);
        await logo.EvaluateAsync("image=>image.decode()");
        var originalSource = await logo.GetAttributeAsync("src");
        Assert.Contains("/assets/HdLogo_thumb_sm.png", originalSource);
        var chrome = await LogoChromeAsync(logo);
        Assert.NotEqual("none", chrome[0]);
        var approvedPixels = await CaptureSettledLogoAsync(logo);
        var approvedGeometry = await logo.EvaluateAsync<string>("image=>JSON.stringify({rect:image.getBoundingClientRect().toJSON(),scrollY,dpr:devicePixelRatio})");
        await page.AddScriptTagAsync(new() { Url = "/js/portrait-system.js?semantic-probe=1" });
        await page.WaitForFunctionAsync("() => !!window.LwcPortrait");

        for (var pass = 0; pass < 3; pass++)
        {
            if (pass > 0)
            {
                generation++;
                await page.EvaluateAsync("""
                    home => {
                        const root=home?document.getElementById('events-root-index'):document;
                        const reload=()=>window.loadEventsTable(home?6:null,true,null,true,{useSharedEvents:false});
                        return home&&typeof window.callScoped==='function'?window.callScoped(root,reload):reload();
                    }
                    """, path == "/");
                logo = page.Locator($"tr[data-event-id='brand-{generation}'] img.custom-event-avatar");
                await Assertions.Expect(logo).ToHaveCountAsync(1);
            }

            await Assertions.Expect(logo).ToHaveAttributeAsync("src", originalSource!);
            await logo.EvaluateAsync("image=>image.decode()");
            Assert.Null(await logo.GetAttributeAsync("data-lwc-portrait"));
            Assert.Null(await logo.GetAttributeAsync("data-lwc-portrait-state"));
            Assert.Equal("Longevity World Cup", await logo.GetAttributeAsync("alt"));
            Assert.Equal(chrome, await LogoChromeAsync(logo));
            var actualPixels = await CaptureSettledLogoAsync(logo);
            try { AssertLogoPixels(approvedPixels, actualPixels); }
            catch
            {
                if (Environment.GetEnvironmentVariable("LWC_LOGO_CAPTURE_DIR") is { Length: > 0 } output)
                {
                    Directory.CreateDirectory(output);
                    var label = $"{(path == "/" ? "home" : "events")}-{width}-{theme}-{pass}";
                    await File.WriteAllBytesAsync(Path.Combine(output, label + "-approved.png"), approvedPixels);
                    await File.WriteAllBytesAsync(Path.Combine(output, label + "-actual.png"), actualPixels);
                    var actualGeometry = await logo.EvaluateAsync<string>("image=>JSON.stringify({rect:image.getBoundingClientRect().toJSON(),scrollY,dpr:devicePixelRatio})");
                    await File.WriteAllTextAsync(Path.Combine(output, label + "-geometry.json"), $"{{\"approved\":{approvedGeometry},\"actual\":{actualGeometry}}}");
                }
                throw;
            }

            var athlete = page.Locator($"tr[data-event-id='photo-{generation}'] .event-avatar-frame img");
            await Assertions.Expect(athlete).ToHaveAttributeAsync("data-lwc-portrait-state", "ready");
            await Assertions.Expect(athlete).ToHaveAttributeAsync("src", "/semantic-athlete.svg");
            await Assertions.Expect(athlete).ToHaveAttributeAsync("alt", "Real Athlete");
            Assert.True(await athlete.EvaluateAsync<bool>("image=>image.complete&&image.naturalWidth>0"));
            var missing = page.Locator($"tr[data-event-id='missing-{generation}'] .event-avatar-frame img");
            await PortraitSystemBrowserTests.AssertApprovedArtworkAsync(missing, "/assets/HdLogo_thumb_sm.png");
            await Assertions.Expect(missing).ToHaveAttributeAsync("alt", "Missing Athlete");
            Assert.Equal(1, await page.Locator($"tr[data-event-id='donation-{generation}'] .donation-gift-avatar .fa-gift").CountAsync());
            Assert.Equal(1, await page.Locator($"tr[data-event-id='milestone-{generation}'] .milestone-people-avatar .fa-people-group").CountAsync());
            Assert.Equal(0, await page.Locator(".donation-gift-avatar[data-lwc-portrait], .milestone-people-avatar[data-lwc-portrait]").CountAsync());
        }
        Assert.Empty(errors);
    }

    internal static async Task<byte[]> CaptureSettledLogoAsync(ILocator logo)
    {
        await logo.ScrollIntoViewIfNeededAsync();
        // Rerendering creates exit copies and can trigger native scroll
        // anchoring. Capture actual settled pixels rather than fast-forwarding
        // animations while the screenshot's clip is being measured.
        await logo.Page.WaitForFunctionAsync("""
            image => !document.querySelector('.events-exit-ghost') && document.getAnimations().every(animation => {
                const target=animation.effect?.target;
                return !(target instanceof Element) || !target.contains(image)
                    || !(animation.timeline instanceof DocumentTimeline)
                    || animation.playState!=='running' || animation.effect.getComputedTiming().iterations===Infinity;
            })
            """, await logo.ElementHandleAsync());
        await logo.EvaluateAsync("""
            async image => {
                let previous='', stable=0;
                const deadline=performance.now()+5000;
                while(stable<3) {
                    await new Promise(requestAnimationFrame);
                    const current=JSON.stringify([image.getBoundingClientRect().toJSON(),scrollX,scrollY]);
                    stable=current===previous?stable+1:0;
                    previous=current;
                    if(performance.now()>deadline)throw Error('Logo capture geometry did not settle');
                }
            }
            """);
        // Locator screenshots scroll again after measuring their clip. Native
        // scroll anchoring can then offset the crop even though the document
        // image did not move. Capture its document coordinates directly.
        var clip = await logo.EvaluateAsync<JsonElement>("""
            image => {const r=image.getBoundingClientRect();return {
                x:r.left+scrollX,y:r.top+scrollY,width:r.width,height:r.height,scale:1
            }}
            """);
        var cdp = await logo.Page.Context.NewCDPSessionAsync(logo.Page);
        try
        {
            var result = await cdp.SendAsync("Page.captureScreenshot", new Dictionary<string, object>
            {
                ["format"] = "png", ["fromSurface"] = true,
                ["captureBeyondViewport"] = true, ["clip"] = clip
            });
            return Convert.FromBase64String(result!.Value.GetProperty("data").GetString()!);
        }
        finally { await cdp.DetachAsync(); }
    }

    private static Task<string[]> LogoChromeAsync(ILocator image) => image.EvaluateAsync<string[]>("""
        image=>{const s=getComputedStyle(image);return [s.boxShadow,s.borderColor,s.borderWidth,s.borderRadius,
            s.outlineStyle,s.objectFit,s.objectPosition,s.content,s.filter]}
        """);

    internal static void AssertLogoPixels(byte[] approved, byte[] actual)
    {
        using var before = Image.Load<Rgba32>(approved);
        using var after = Image.Load<Rgba32>(actual);
        Assert.Equal(before.Size, after.Size);
        // Compare rendered artwork inside the round frame; the orange frame
        // and crop are asserted separately. A monogram or blank content fails.
        for (var y = before.Height / 4; y < before.Height * 3 / 4; y++)
        for (var x = before.Width / 4; x < before.Width * 3 / 4; x++)
            Assert.Equal(before[x, y], after[x, y]);
    }
}
