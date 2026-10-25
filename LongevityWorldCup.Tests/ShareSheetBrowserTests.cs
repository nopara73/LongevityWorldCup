using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class ShareSheetBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("bortz")]
    [InlineData("pheno")]
    [InlineData("improvement")]
    [InlineData("bortz-improvement")]
    [InlineData("crowd")]
    public async Task RankingViews_UseTheSignedPageCardOnDirectRoutesAndTheCurrentViewAfterHistoryChanges(string view)
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/league/" + view);
        await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
        await ExpectPreviewAsync(page);
        var source = await page.Locator(".lwc-share__preview img").GetAttributeAsync("src");
        Assert.StartsWith($"/og/page/view-{view}.png?v=", source);
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync("() => history.replaceState({}, '', '/league/pheno')");
        await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
        Assert.StartsWith("/og/page/view-pheno.png", await page.Locator(".lwc-share__preview img").GetAttributeAsync("src"));
        Assert.Equal("https://longevityworldcup.com/league/pheno", await SharedUrlAsync(page));
    }

    [Fact]
    public async Task LiveLeaderboardControls_ShareCurrentLeagueFlagViewAndSearchWithoutStaleCanonicalMetadata()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        foreach (var selection in new[] { "league", "flag", "view", "search" })
        {
            await page.EvaluateAsync("""
                selection => {
                    for (const input of document.querySelectorAll('.filter-section input[type=checkbox]')) input.checked = false;
                    if (selection === 'league') [...document.querySelectorAll('input[name=division]')].find(e => e.value.toLowerCase().startsWith('women')).checked = true;
                    if (selection === 'flag') document.querySelector('input[name=flag][value=Hungary]').checked = true;
                    const radio = document.querySelector('input[name=leaderboardView][value=' + (selection === 'view' ? 'pheno' : 'ultimate') + ']');
                    radio.checked = true;
                    document.getElementById('athleteSearch').value = selection === 'search' ? 'Michael & PhD' : '';
                    radio.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """, selection);
            await page.WaitForTimeoutAsync(400);
            var path = await page.EvaluateAsync<string>("() => location.pathname + location.search");
            Assert.NotEqual("/leaderboard", path);
            await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
            Assert.Equal("https://longevityworldcup.com" + path, await SharedUrlAsync(page));
            var title = await page.TitleAsync();
            Assert.Equal(title, await page.Locator(".lwc-share__destination[data-network=email]").EvaluateAsync<string>("a => new URL(a.href).searchParams.get('subject')"));
            await page.Keyboard.PressAsync("Escape");
        }
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task ExplicitCalculatorLinks_ExcludePrivateInputsAndEncodeDestinationPayloads(string path)
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        await page.EvaluateAsync("""
            path => {
                history.replaceState({}, '', path + '?AlbGL=45&age=44');
                const trigger = document.querySelector('[data-lwc-share]');
                trigger.dataset.shareUrl = path;
                trigger.dataset.shareTitle = 'Age & health? #1';
                trigger.dataset.shareText = 'Measured + shared\nwith care';
            }
            """, path);
        await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
        Assert.Equal("https://longevityworldcup.com" + path, await SharedUrlAsync(page));
        Assert.Equal("Measured + shared\nwith care", await page.Locator(".lwc-share__destination[data-network=x]").EvaluateAsync<string>("a => new URL(a.href).searchParams.get('text')"));
        await page.Locator(".lwc-share__copy").ClickAsync();
        Assert.Equal(new[] { "https://longevityworldcup.com" + path }, await page.EvaluateAsync<string[]>("() => window.__copies"));
        Assert.Contains("AlbGL=45", page.Url);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task CloseReopenAndLateCopy_DoNotCloseOrChangeTheNewSheet(int width)
    {
        await using var context = await ContextAsync(width, reduced: false);
        await context.AddInitScriptAsync("navigator.clipboard.writeText = value => { window.__copies.push(value); return new Promise(resolve => window.__finishCopy = resolve); }");
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        var trigger = page.Locator("#leaderboardTitle [data-lwc-share]");
        await trigger.ClickAsync();
        await page.Locator(".lwc-share__copy").ClickAsync();
        await page.Locator(".lwc-share__copy").PressAsync("Enter");
        Assert.Single(await page.EvaluateAsync<string[]>("() => window.__copies"));
        await page.EvaluateAsync("() => { LwcShare.close(true); history.replaceState({}, '', '/league/bortz'); LwcShare.open(document.querySelector('[data-lwc-share]')); window.__finishCopy(); }");
        await page.WaitForTimeoutAsync(1400);
        await Assertions.Expect(page.Locator(".lwc-share")).ToBeVisibleAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.DoesNotContain("is-closing", await page.Locator(".lwc-share-scrim").GetAttributeAsync("class"));
        Assert.Equal("https://longevityworldcup.com/league/bortz", await SharedUrlAsync(page));
        Assert.DoesNotContain("is-copied", await page.Locator(".lwc-share").GetAttributeAsync("class"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task RepeatedCopyFeedback_KeepsTheNewestTriggerStateUntilItsOwnDeadline()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await OpenPageAsync(page, "/leaderboard");
        await page.Clock.PauseAtAsync(DateTime.UtcNow.AddSeconds(1));
        var trigger = page.Locator("#leaderboardTitle [data-lwc-share]");
        await trigger.ClickAsync();
        await page.Locator(".lwc-share__copy").ClickAsync();
        await Assertions.Expect(trigger).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-copied"));
        await page.Keyboard.PressAsync("Escape");
        await page.Clock.FastForwardAsync(1200);
        await trigger.ClickAsync();
        await page.Locator(".lwc-share__copy").ClickAsync();
        await page.Clock.FastForwardAsync(900);
        await Assertions.Expect(trigger).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-copied"));
        Assert.Equal(2, (await page.EvaluateAsync<string[]>("window.__copies")).Length);
        await page.Clock.FastForwardAsync(1150);
        await Assertions.Expect(trigger).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-copied"));
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task SharingAnActualCalculatedResult_PreservesInputsAndResults(string path)
    {
        await using var context = await ContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await page.BringToFrontAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, path);
        await FlowActionDockBrowserTests.FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        if (path == "/pheno-age") await FlowActionDockBrowserTests.FillPhenoBiomarkersAsync(page);
        else await FlowActionDockBrowserTests.FillBortzBiomarkersAsync(page);
        await page.Locator("#calculateBioageButton").ClickAsync();
        await Assertions.Expect(page.Locator(".bioage-result-share")).ToBeVisibleAsync();
        var values = await page.Locator("form input, form select").EvaluateAllAsync<string[]>("fields => fields.map(e => e.id + '=' + e.value)");
        var result = await page.Locator(".bio-age-number-container").InnerTextAsync();
        await page.Locator(".bioage-result-share").ClickAsync();
        Assert.Equal("https://longevityworldcup.com" + path, await SharedUrlAsync(page));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal(values, await page.Locator("form input, form select").EvaluateAllAsync<string[]>("fields => fields.map(e => e.id + '=' + e.value)"));
        Assert.Equal(result, await page.Locator(".bio-age-number-container").InnerTextAsync());
        await Assertions.Expect(page.Locator(".bioage-result-share")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task NativeCancellationAndLateFailure_DoNotOpenAStaleFallback()
    {
        await using var context = await ContextAsync(390);
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'share', {configurable:true,value:() => { window.__nativeCalls = (window.__nativeCalls || 0) + 1; return new Promise((resolve,reject) => window.__rejectNative = reject); }});");
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        await page.EvaluateAsync("() => { const t=document.querySelector('[data-lwc-share]'); LwcShare.open(t); LwcShare.open(t); }");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__nativeCalls"));
        await page.EvaluateAsync("() => { LwcShare.close(); window.__rejectNative(new Error('late failure')); }");
        await Assertions.Expect(page.Locator(".lwc-share")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("[data-lwc-share]")).Not.ToHaveAttributeAsync("aria-busy", "true");
        await page.EvaluateAsync("() => { LwcShare.open(document.querySelector('[data-lwc-share]')); window.__rejectNative(new DOMException('cancelled','AbortError')); }");
        await Assertions.Expect(page.Locator(".lwc-share")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440, 900, 16)]
    [InlineData(390, 844, 16)]
    [InlineData(320, 844, 16)]
    [InlineData(768, 1024, 16)]
    [InlineData(844, 390, 16)]
    [InlineData(1440, 900, 32)]
    [InlineData(390, 844, 32)]
    [InlineData(320, 844, 32)]
    [InlineData(600, 844, 32)]
    public async Task OrdinaryShareContent_FitsWithoutInternalScrollbars(int width, int height, int textSize)
    {
        await using var nativeBrowser = await Browser.BrowserType.LaunchAsync(new() { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var context = await ContextAsync(width, height, browser: nativeBrowser);
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", textSize);
        // Exercise visible, space-consuming native scrollbars instead of hiding overflow.
        await page.AddStyleTagAsync(new() { Content = "::-webkit-scrollbar { width:15px; height:15px; }" });
        await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
        await ExpectPreviewAsync(page);
        await AssertFitAsync(page.Locator(".lwc-share"));
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator(".lwc-share__destination[data-network=email]")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".lwc-share__copy")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#leaderboardTitle [data-lwc-share]")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task FailedPreviewAndCopy_LeaveAReachableSelectedLinkAndOutsideDismissal()
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/og/**/*.png*", route => route.AbortAsync());
        await context.AddInitScriptAsync("navigator.clipboard.writeText = async () => { throw new Error('denied'); }");
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
        await Assertions.Expect(page.Locator(".lwc-share__preview")).ToBeHiddenAsync();
        await page.Locator(".lwc-share__copy").ClickAsync();
        await Assertions.Expect(page.Locator(".lwc-share__manual")).ToBeFocusedAsync();
        Assert.True(await page.Locator(".lwc-share__manual").EvaluateAsync<bool>("e => e.selectionStart === 0 && e.selectionEnd === e.value.length"));
        await AssertFitAsync(page.Locator(".lwc-share"));
        await page.Mouse.ClickAsync(10, 100);
        await Assertions.Expect(page.Locator(".lwc-share")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#leaderboardTitle [data-lwc-share]")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(320, 844, 32, false)]
    [InlineData(320, 844, 32, true)]
    [InlineData(600, 844, 32, false)]
    [InlineData(600, 844, 32, true)]
    [InlineData(711, 402, 16, false)]
    [InlineData(711, 402, 16, true)]
    public async Task AthleteShare_ShortContentAtEnlargedTextFitsWithoutAScrollbar(int width, int height, int textSize, bool dark)
    {
        await using var nativeBrowser = await Browser.BrowserType.LaunchAsync(new() { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var context = await ContextAsync(width, height, browser: nativeBrowser);
        var page = await context.NewPageAsync();
        await page.BringToFrontAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/michael-lustgarten");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize=size+'px'", textSize);
        await page.AddStyleTagAsync(new() { Content = "::-webkit-scrollbar { width:15px; height:15px; }" });
        await page.Locator("#shareAthleteProfile").ClickAsync();
        if (height > 560) await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview.is-loaded img')?.naturalWidth === 1200", null, new() { Timeout = 30000 });
        await AssertFitAsync(page.Locator("#athleteShareMenu"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AthleteShare_ScrolledEnlargedProfileFitsBelowItsStickyHeader(bool dark)
    {
        await using var nativeBrowser = await Browser.BrowserType.LaunchAsync(new() { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var context = await ContextAsync(browser: nativeBrowser);
        var page = await context.NewPageAsync();
        await page.BringToFrontAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await OpenPageAsync(page, "/leaderboard");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize='32px'");
        await page.AddStyleTagAsync(new() { Content = "::-webkit-scrollbar { width:15px; height:15px; }" });
        await page.Locator("#athleteLeaderboard .athlete-name").Filter(new() { HasText = "Joe V" }).ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("e => e.scrollTo({top:1200,behavior:'instant'})");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        await page.Locator("#shareAthleteProfile").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview.is-loaded img')?.naturalWidth === 1200");
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("e => e.scrollBy({top:600,behavior:'instant'})");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(resolve))))");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        await AssertFitAsync(page.Locator("#athleteShareMenu"));
        await AssertAthleteMenuInsideCardAsync(page);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task AthleteShare_ParentScrollingKeepsControlsVisibleAndCloseCancelsLayoutWork(int width, int height)
    {
        await using var context = await ContextAsync(width, height);
        var page = await context.NewPageAsync();
        await page.BringToFrontAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/michael-lustgarten");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.Locator("#shareAthleteProfile").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview.is-loaded img')?.naturalWidth === 1200");
        await page.EvaluateAsync("() => document.querySelector('#detailsModal .modal-content').scrollBy({top:300,behavior:'instant'})");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(resolve))))");
        await page.EvaluateAsync("() => document.querySelector('#detailsModal .modal-content').scrollBy({top:300,behavior:'instant'})");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(resolve))))");
        await AssertAthleteMenuInsideCardAsync(page);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => document.querySelector('#detailsModal .modal-content').scrollBy({top:100,behavior:'instant'})");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeHiddenAsync();
        Assert.Equal("", await page.Locator("#athleteShareMenu").EvaluateAsync<string>("e => e.style.getPropertyValue('--athlete-share-offset')"));
    }

    private static async Task AssertAthleteMenuInsideCardAsync(IPage page)
    {
        var fit = await page.EvaluateAsync<JsonElement>("""
            () => {
                const menu=document.getElementById('athleteShareMenu'), card=menu.closest('.modal-content');
                const rect=menu.getBoundingClientRect(), frame=card.getBoundingClientRect();
                const header=card.querySelector('.modal-sticky-header.visible')?.getBoundingClientRect();
                const top=Math.max(0,frame.top,header?.bottom||0), bottom=Math.min(innerHeight,frame.bottom);
                const controls=[...menu.querySelectorAll('button,a')].filter(e=>e.getClientRects().length).map(e=>{
                    const r=e.getBoundingClientRect(), hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);
                    return {top:r.top,bottom:r.bottom,hit:!!hit&&(hit===e||e.contains(hit))};
                });
                return {top,bottom,menuTop:rect.top,menuBottom:rect.bottom,controls};
            }
            """);
        Assert.True(fit.GetProperty("menuTop").GetDouble() >= fit.GetProperty("top").GetDouble(), fit.ToString());
        Assert.True(fit.GetProperty("menuBottom").GetDouble() <= fit.GetProperty("bottom").GetDouble(), fit.ToString());
        foreach (var control in fit.GetProperty("controls").EnumerateArray())
        {
            Assert.True(control.GetProperty("top").GetDouble() >= fit.GetProperty("top").GetDouble(), fit.ToString());
            Assert.True(control.GetProperty("bottom").GetDouble() <= fit.GetProperty("bottom").GetDouble(), fit.ToString());
            Assert.True(control.GetProperty("hit").GetBoolean(), fit.ToString());
        }
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(320, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task AthleteShare_EnlargedPhoneHeaderKeepsAShortNameProBadgeAndControlsVisible(int width, bool dark)
    {
        await using var nativeBrowser = await Browser.BrowserType.LaunchAsync(new() { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var context = await ContextAsync(width, 844, browser: nativeBrowser);
        var page = await context.NewPageAsync();
        await page.BringToFrontAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/joe-v");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.WaitForFunctionAsync("() => document.getElementById('stickyAthleteName').textContent === 'Joe V' && document.querySelector('#detailsModal .modal-content').dataset.athleteSlug === 'joe-v'");
        await page.AddStyleTagAsync(new() { Content = "html { font-size:32px; } ::-webkit-scrollbar { width:15px; height:15px; }" });
        var fontSize = await page.Locator("#stickyAthleteName").EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        // Isolate the earned PRO header state from calendar-dependent score
        // fixtures. The matching rendered gallery uses Joe V's real data.
        await page.EvaluateAsync("() => { document.getElementById('modalStickyHeader').classList.add('is-pro'); document.querySelector('#detailsModal .modal-content').scrollTo({top:1200,behavior:'instant'}); }");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        var fit = await page.Locator("#modalStickyHeader").EvaluateAsync<JsonElement>("""
            header => {
                const name=header.querySelector('.sticky-athlete-name'), badge=header.querySelector('.sticky-pro-badge');
                const n=name.getBoundingClientRect(), b=badge.getBoundingClientRect(), h=header.getBoundingClientRect();
                const controls=[...header.querySelectorAll('button')].map(e=>{const r=e.getBoundingClientRect(), hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);return {width:r.width,height:r.height,hit:!!hit&&(e===hit||e.contains(hit))};});
                return {text:name.textContent,sh:name.scrollHeight,ch:name.clientHeight,sw:name.scrollWidth,cw:name.clientWidth,font:parseFloat(getComputedStyle(name).fontSize),badgeFits:badge.scrollWidth<=badge.clientWidth&&b.top>=h.top&&b.bottom<=h.bottom,nameFits:n.top>=h.top&&n.bottom<=h.bottom,controls};
            }
            """);
        Assert.Equal("Joe V", fit.GetProperty("text").GetString());
        Assert.Equal(fontSize, fit.GetProperty("font").GetDouble(), 2);
        Assert.True(fit.GetProperty("sw").GetInt32() <= fit.GetProperty("cw").GetInt32(), fit.ToString());
        Assert.True(fit.GetProperty("sh").GetInt32() <= fit.GetProperty("ch").GetInt32(), fit.ToString());
        Assert.True(fit.GetProperty("badgeFits").GetBoolean() && fit.GetProperty("nameFits").GetBoolean(), fit.ToString());
        Assert.Equal(3, fit.GetProperty("controls").GetArrayLength());
        foreach (var control in fit.GetProperty("controls").EnumerateArray())
        {
            Assert.True(control.GetProperty("width").GetDouble() >= 44 && control.GetProperty("height").GetDouble() >= 44, fit.ToString());
            Assert.True(control.GetProperty("hit").GetBoolean(), fit.ToString());
        }
    }

    [Fact]
    public async Task ConstrainedSheet_ScrollsContentWhileOnlyTheHandleDrags()
    {
        await using var context = await ContextAsync(390, 400);
        var page = await context.NewPageAsync();
        await OpenPageAsync(page, "/leaderboard");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize='32px'");
        await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
        await ExpectPreviewAsync(page);
        var sheet = page.Locator(".lwc-share");
        Assert.True(await sheet.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"));
        var touch = await context.NewCDPSessionAsync(page);
        for (var step = 0; step <= 10; step++)
            await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = step == 0 ? "touchStart" : "touchMove", ["touchPoints"] = new[] { new { x = 190, y = 300 - step * 20, id = 1 } } });
        // Release a resting finger so the content-scroll check cannot fling the
        // handle away while the independent dismissal gesture targets it.
        await page.WaitForTimeoutAsync(200);
        await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await page.WaitForFunctionAsync("() => document.querySelector('.lwc-share').scrollTop > 50");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        Assert.Equal("", await sheet.EvaluateAsync<string>("e => e.style.getPropertyValue('--lwc-share-drag')"));
        await sheet.EvaluateAsync("e => e.scrollTop=0");
        var handle = (await page.Locator(".lwc-share__handle").BoundingBoxAsync())!;
        for (var step = 0; step <= 10; step++)
        {
            await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = step == 0 ? "touchStart" : "touchMove",
                ["touchPoints"] = new[] { new { x = handle.X + handle.Width / 2, y = handle.Y + handle.Height / 2 + step * 24, id = 1 } }
            });
            if (step == 0) await Assertions.Expect(sheet).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-dragging\b"));
            await page.WaitForTimeoutAsync(20);
        }
        await touch.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        Assert.False(await page.Locator("main").EvaluateAsync<bool>("e => e.inert"));
        await page.Locator("#leaderboardTitle [data-lwc-share]").TapAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
    }

    private async Task<IBrowserContext> ContextAsync(int width = 1440, int height = 900, bool reduced = true, IBrowser? browser = null)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(browser ?? Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, HasTouch = width < 600,
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await context.RouteAsync("**/*", route => route.Request.Method is "GET" or "HEAD" ? route.FallbackAsync() : route.AbortAsync());
        await context.AddInitScriptAsync("""
            localStorage.setItem('gmaSkipAll','true');
            window.__copies=[];
            Object.defineProperty(navigator,'share',{configurable:true,value:undefined});
            Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:async value => { window.__copies.push(value); }}});
            document.execCommand=()=>false;
            window.open=()=>null;
            document.addEventListener('click',e=>{if(e.target.closest('a[href^="mailto:"],a[target="_blank"],a[download]'))e.preventDefault();},true);
            """);
        return context;
    }

    private static async Task OpenPageAsync(IPage page, string path)
    {
        await page.BringToFrontAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
    }

    private static Task ExpectPreviewAsync(IPage page) => page.WaitForFunctionAsync(
        "() => { const img=document.querySelector('.lwc-share__preview.is-loaded img'); return img?.complete && img.naturalWidth === 1200; }", null, new() { Timeout = 30000 });

    private static Task<string> SharedUrlAsync(IPage page) => page.Locator(".lwc-share__destination[data-network=facebook]")
        .EvaluateAsync<string>("a => new URL(a.href).searchParams.get('u')");

    private static async Task AssertFitAsync(ILocator surface)
    {
        var fit = await surface.EvaluateAsync<JsonElement>("""
            e => { const r=e.getBoundingClientRect(); return {sh:e.scrollHeight,ch:e.clientHeight,sw:e.scrollWidth,cw:e.clientWidth,top:r.top,bottom:r.bottom,left:r.left,right:r.right,width:innerWidth,height:innerHeight}; }
            """);
        Assert.True(fit.GetProperty("sh").GetInt32() <= fit.GetProperty("ch").GetInt32() + 1, fit.ToString());
        Assert.True(fit.GetProperty("sw").GetInt32() <= fit.GetProperty("cw").GetInt32() + 1, fit.ToString());
        Assert.InRange(fit.GetProperty("top").GetDouble(), 0, fit.GetProperty("height").GetDouble());
        Assert.InRange(fit.GetProperty("bottom").GetDouble(), 0, fit.GetProperty("height").GetDouble() + 1);
        Assert.InRange(fit.GetProperty("left").GetDouble(), 0, fit.GetProperty("width").GetDouble());
        Assert.InRange(fit.GetProperty("right").GetDouble(), 0, fit.GetProperty("width").GetDouble() + 1);
    }
}
