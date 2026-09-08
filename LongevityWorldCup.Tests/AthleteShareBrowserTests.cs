using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class AthleteShareBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string FirstUrl = "https://longevityworldcup.com/athlete/michael-lustgarten";
    private const string SecondUrl = "https://longevityworldcup.com/athlete/michael-egorov";

    [Fact]
    public async Task Copy_SerializesRequestsAndKeepsFocusAfterSuccess()
    {
        await using var context = await CreateContextAsync(copy: "hold");
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        var copy = page.Locator("#copyAthleteProfileLink");
        await copy.ClickAsync();
        await Assertions.Expect(copy).ToHaveTextAsync("Copying…");
        await Assertions.Expect(copy).ToBeFocusedAsync();
        await copy.PressAsync("Enter");
        await copy.PressAsync("Space");
        Assert.Equal([FirstUrl], await CopyCallsAsync(page));
        await CompleteAsync(page, "copy");
        await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Copied");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveAttributeAsync("aria-disabled", "false");
        await Assertions.Expect(page.Locator("#athleteShareStatus")).ToHaveTextAsync("Link copied.");
        // Sharing is immediately available again during the brief confirmation.
        await page.Locator("#shareAthleteProfile").PressAsync("Enter");
        await Assertions.Expect(copy).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Share");
    }

    [Fact]
    public async Task CopyConfirmation_DoesNotResetANewNativeShare()
    {
        await using var context = await CreateContextAsync(native: "error");
        var page = await OpenProfileAsync(context);
        await page.Clock.InstallAsync();
        var share = page.Locator("#shareAthleteProfile");
        await share.ClickAsync();
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await Assertions.Expect(share).ToHaveTextAsync("Copied");
        await page.EvaluateAsync("() => { Object.defineProperty(navigator,'share',{configurable:true,value:()=>new Promise(resolve=>window.__finishNative=resolve)}); }");
        await share.PressAsync("Enter");
        await Assertions.Expect(share).ToHaveTextAsync("Sharing…");
        await page.Clock.FastForwardAsync(1601);
        await Assertions.Expect(share).ToHaveTextAsync("Sharing…");
        await Assertions.Expect(share).ToHaveAttributeAsync("aria-busy", "true");
        await page.EvaluateAsync("() => window.__finishNative()");
        await Assertions.Expect(share).ToHaveTextAsync("Share");
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("legacy-failed")]
    public async Task FailedCopy_OffersSelectedLinkAndKeyboardRetry(string copyMode)
    {
        await using var context = await CreateContextAsync(copy: copyMode);
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        var input = page.Locator("#athleteShareLink");
        await Assertions.Expect(input).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync(FirstUrl);
        Assert.True(await input.EvaluateAsync<bool>("el => el.readOnly && el.selectionStart === 0 && el.selectionEnd === el.value.length"));
        await Assertions.Expect(page.Locator("#copyAthleteProfileLink")).ToHaveTextAsync("Retry copy");
        await input.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator("#copyAthleteProfileLink")).ToBeFocusedAsync();
        await page.Locator("#copyAthleteProfileLink").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Copied");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        Assert.Equal([FirstUrl, FirstUrl], await CopyCallsAsync(page));
    }

    [Fact]
    public async Task LegacyCopy_RestoresFocusAfterRemovingItsTemporaryField()
    {
        await using var context = await CreateContextAsync(copy: "legacy-success");
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Copied");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        Assert.Equal([FirstUrl], await CopyCallsAsync(page));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCopy_DoesNotAffectAnotherProfileOrItsOpenSharing(bool fail)
    {
        await using var context = await CreateContextAsync(copy: "hold");
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await OpenSecondProfileAsync(page);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await CompleteAsync(page, "copy", fail);
        await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Share");
        await Assertions.Expect(page.Locator("#copyAthleteProfileLink")).ToHaveTextAsync("Copy link");
        await Assertions.Expect(page.Locator("#copyAthleteProfileLink")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#athleteShareRecovery")).ToBeHiddenAsync();
        await page.Locator("#copyAthleteProfileLink").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Copied");
        Assert.Equal([FirstUrl, SecondUrl], await CopyCallsAsync(page));
    }

    [Fact]
    public async Task ReopeningSharing_KeepsAPendingCopySingleWithoutAStaleClose()
    {
        await using var context = await CreateContextAsync(copy: "hold");
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        var copy = page.Locator("#copyAthleteProfileLink");
        await copy.ClickAsync();
        await copy.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await page.Locator("#shareAthleteProfile").PressAsync("Enter");
        await Assertions.Expect(copy).ToHaveTextAsync("Copying…");
        await copy.PressAsync("Enter");
        Assert.Single(await CopyCallsAsync(page));
        await CompleteAsync(page, "copy");
        await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeVisibleAsync();
        await Assertions.Expect(copy).ToHaveTextAsync("Copy link");
        await Assertions.Expect(copy).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Share");
    }

    [Theory]
    [InlineData("success", false)]
    [InlineData("cancel", false)]
    [InlineData("error", true)]
    [InlineData("unsupported", true)]
    public async Task NativeSharing_PreservesSuccessCancellationAndFallback(string nativeMode, bool fallback)
    {
        await using var context = await CreateContextAsync(native: nativeMode);
        var page = await OpenProfileAsync(context);
        var share = page.Locator("#shareAthleteProfile");
        var title = await share.GetAttributeAsync("data-share-title");
        var text = await share.GetAttributeAsync("data-share-text");
        await share.ClickAsync();
        await Assertions.Expect(share).ToHaveTextAsync("Share");
        await Assertions.Expect(share).ToHaveAttributeAsync("aria-disabled", "false");
        if (fallback)
        {
            await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#copyAthleteProfileLink")).ToBeFocusedAsync();
        }
        else
        {
            await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeHiddenAsync();
            await Assertions.Expect(share).ToBeFocusedAsync();
        }
        var calls = (await page.EvaluateAsync<JsonElement>("() => window.__nativeCalls")).EnumerateArray().ToArray();
        if (nativeMode == "unsupported") Assert.Empty(calls);
        else
        {
            var payload = Assert.Single(calls);
            Assert.Equal(FirstUrl, payload.GetProperty("url").GetString());
            Assert.Equal(title, payload.GetProperty("title").GetString());
            Assert.Equal(text, payload.GetProperty("text").GetString());
        }
        Assert.Empty(await CopyCallsAsync(page));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSharing_SerializesAndIgnoresResultsAfterProfileNavigation(bool fail)
    {
        await using var context = await CreateContextAsync(native: "hold");
        var page = await OpenProfileAsync(context);
        var share = page.Locator("#shareAthleteProfile");
        await share.ClickAsync();
        await Assertions.Expect(share).ToHaveTextAsync("Sharing…");
        await Assertions.Expect(share).ToBeFocusedAsync();
        await share.PressAsync("Enter");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__nativeCalls.length"));
        await OpenSecondProfileAsync(page);
        var focus = await page.EvaluateAsync<string>("() => document.activeElement?.id || document.activeElement?.tagName");
        await CompleteAsync(page, "native", fail);
        await Assertions.Expect(share).ToHaveTextAsync("Share");
        await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeHiddenAsync();
        Assert.Equal(focus, await page.EvaluateAsync<string>("() => document.activeElement?.id || document.activeElement?.tagName"));
        Assert.Empty(await CopyCallsAsync(page));
    }

    [Fact]
    public async Task Recovery_KeepsTheExistingShareDestinations()
    {
        await using var context = await CreateContextAsync(copy: "denied");
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        var ids = new[] { "shareAthleteProfileX", "shareAthleteProfileFacebook", "shareAthleteProfileLinkedIn", "shareAthleteProfileEmail" };
        var destinations = await Task.WhenAll(ids.Select(id => page.Locator("#" + id).GetAttributeAsync("href")));
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteShareLink")).ToBeFocusedAsync();
        for (var i = 0; i < ids.Length; i++)
        {
            Assert.Equal(destinations[i], await page.Locator("#" + ids[i]).GetAttributeAsync("href"));
            Assert.Contains(FirstUrl, Uri.UnescapeDataString(destinations[i]!));
        }
        await page.Locator("#athleteShareLink").PressAsync("Tab");
        await Assertions.Expect(page.Locator("#shareAthleteProfileX")).ToBeFocusedAsync();
        await page.Locator("#shareAthleteProfileX").PressAsync("Escape");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(320, 568, false)]
    [InlineData(390, 844, true)]
    [InlineData(1280, 760, false)]
    [InlineData(844, 390, true)]
    public async Task Recovery_FitsTheViewportAndKeepsTheLinkUsable(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(copy: "denied", width: width, height: height, dark: dark);
        var page = await OpenProfileAsync(context);
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteShareLink")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#athleteShareMenu").EvaluateAsync<bool>("el => { const box=el.getBoundingClientRect(); const input=document.querySelector('#athleteShareLink').getBoundingClientRect(); return box.left>=0 && box.right<=innerWidth+1 && box.top>=0 && box.bottom<=innerHeight+1 && el.scrollWidth<=el.clientWidth && input.width<=box.width && input.height>=44; }"));
        BrowserContrast.AssertMinimum("Sharing recovery", await BrowserContrast.MeasureVisibleTextAsync(page, "#athleteShareRecovery p", "#athleteShareRecovery label"));
        await page.Locator("#athleteShareLink").PressAsync("Escape");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
    }

    private async Task<IBrowserContext> CreateContextAsync(string copy = "success", string native = "", int width = 390, int height = 844, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height }, IsMobile = native.Length > 0, HasTouch = native.Length > 0, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/*", route => route.Request.Method is "GET" or "HEAD" or "OPTIONS" ? route.FallbackAsync() : route.AbortAsync());
        await context.AddInitScriptAsync($$$"""
            localStorage.setItem('gmaSkipAll','true');
            const options = {{{JsonSerializer.Serialize(new { copy, native })}}};
            window.__copyCalls=[]; window.__nativeCalls=[]; window.__gates={};
            const held = kind => new Promise((resolve,reject) => { window.__gates[kind] = fail => fail ? reject(new DOMException('Unavailable','NotAllowedError')) : resolve(); });
            const write = text => {
                window.__copyCalls.push(text);
                if(options.copy==='hold' && window.__copyCalls.length===1) return held('copy');
                if(options.copy==='denied' && window.__copyCalls.length===1) return Promise.reject(new DOMException('Blocked','NotAllowedError'));
                return Promise.resolve();
            };
            Object.defineProperty(navigator,'clipboard',{configurable:true,value:options.copy.startsWith('legacy') ? undefined : {writeText:write}});
            document.execCommand = command => {
                if(command!=='copy') throw new Error('Unexpected command');
                window.__copyCalls.push(document.activeElement.value);
                return options.copy==='legacy-success' || window.__copyCalls.length>1;
            };
            if(options.native) {
                Object.defineProperty(navigator,'canShare',{configurable:true,value:()=>options.native!=='unsupported'});
                Object.defineProperty(navigator,'share',{configurable:true,value:payload=>{
                    window.__nativeCalls.push(payload);
                    if(options.native==='hold') return held('native');
                    if(options.native==='cancel' || options.native==='error') return Promise.reject(new DOMException('Share unavailable',options.native==='cancel'?'AbortError':'NotAllowedError'));
                    return Promise.resolve();
                }});
            }
            """);
        return context;
    }

    private static async Task<IPage> OpenProfileAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?search=michael");
        await page.WaitForFunctionAsync("() => document.querySelector('#leaderboardStatus')?.textContent==='Leaderboard loaded.'");
        var rows = page.Locator(".leaderboard tbody .athlete-name:visible");
        await Assertions.Expect(rows).ToHaveCountAsync(2);
        await rows.First.ClickAsync();
        await WaitForProfileAsync(page, "michael-lustgarten");
        return page;
    }

    private static async Task OpenSecondProfileAsync(IPage page)
    {
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.Locator(".leaderboard tbody .athlete-name:visible").Nth(1).ClickAsync();
        await WaitForProfileAsync(page, "michael-egorov");
    }

    private static Task WaitForProfileAsync(IPage page, string slug) => page.WaitForFunctionAsync("slug => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug===slug && !document.querySelector('#detailsModal .modal-content').matches('.is-loading,.has-load-error')", slug);
    private static Task<string[]> CopyCallsAsync(IPage page) => page.EvaluateAsync<string[]>("() => window.__copyCalls");
    private static Task CompleteAsync(IPage page, string kind, bool fail = false) => page.EvaluateAsync("async ({kind,fail}) => { window.__gates[kind](fail); await new Promise(requestAnimationFrame); }", new { kind, fail });
}
