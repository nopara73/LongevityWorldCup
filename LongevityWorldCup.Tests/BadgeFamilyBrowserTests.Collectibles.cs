using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class BadgeFamilyBrowserTests
{
    [Theory]
    [InlineData(1440, 900, false, 16)]
    [InlineData(1440, 900, true, 32)]
    [InlineData(768, 700, false, 24)]
    [InlineData(844, 390, true, 24)]
    [InlineData(390, 844, false, 16)]
    [InlineData(390, 844, true, 32)]
    [InlineData(320, 480, false, 32)]
    public async Task Collectible_EntranceAndSettledBoundsFitAtEveryEdge(int width, int height, bool dark, int fontSize)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"] });
        await using var context = await NewContextAsync(browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await OpenCollectibleFixtureAsync(context, fontSize);
        var badge = page.Locator("#collectible-fixture .badge-class").First;
        var tip = page.Locator("#badge-tip");
        foreach (var corner in new[] { "top-left", "top-right", "bottom-right", "bottom-left" })
        foreach (var card in new[] { false, true })
        {
            await badge.EvaluateAsync("""
                (badge, corner) => {
                    badge.blur();
                    const host=badge.parentElement;
                    host.style.top=corner.startsWith('top')?'8px':'auto';
                    host.style.bottom=corner.startsWith('bottom')?'8px':'auto';
                    host.style.left=corner.endsWith('left')?'8px':'auto';
                    host.style.right=corner.endsWith('right')?'8px':'auto';
                }
                """, corner);
            await badge.FocusAsync();
            if (card) await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(tip).ToBeVisibleAsync();
            var failures = await tip.EvaluateAsync<string[]>("""
                async tip => {
                    const failures=[];
                    const start=performance.now();
                    do {
                        await new Promise(requestAnimationFrame);
                        const r=tip.getBoundingClientRect();
                        // A sheet enters from below the screen; its horizontal
                        // bounds and its settled vertical bounds must still fit.
                        if(r.left < -0.6 || r.right > document.documentElement.clientWidth+0.6) failures.push('horizontal '+JSON.stringify(r));
                        if(tip.dataset.side !== 'sheet' && (r.top < -0.6 || r.bottom > innerHeight+0.6)) failures.push('vertical '+JSON.stringify(r));
                    } while(performance.now()-start<700);
                    const r=tip.getBoundingClientRect(), c=tip.querySelector('.badge-tip-content');
                    if(r.top<0 || r.bottom>innerHeight+0.6) failures.push('settled vertical');
                    if(c.scrollHeight!==c.clientHeight || c.scrollWidth!==c.clientWidth) failures.push('short content scrolls');
                    if(c.offsetWidth!==c.clientWidth) failures.push('native gutter');
                    const cr=c.getBoundingClientRect();
                    for(const el of c.querySelectorAll('.badge-tip-title,.badge-tip-detail')) {
                        const range=document.createRange();range.selectNodeContents(el);
                        for(const t of range.getClientRects()) if(t.left<cr.left || t.right>cr.right || t.top<cr.top || t.bottom>cr.bottom) failures.push('clipped glyphs');
                    }
                    return failures;
                }
                """);
            Assert.True(failures.Length == 0, $"{corner}, card={card}: {string.Join(';', failures)}");
            if (card)
            {
                await Assertions.Expect(tip).ToHaveAttributeAsync("role", "dialog");
                await Assertions.Expect(tip).ToHaveAttributeAsync("aria-hidden", "false");
                await Assertions.Expect(tip.Locator(".badge-tip-content")).ToBeFocusedAsync();
            }
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(tip).ToBeHiddenAsync();
            await Assertions.Expect(badge).ToBeFocusedAsync();
        }
    }

    [Fact]
    public async Task Collectible_SheetContainsFocusSurvivesHeightResizeAndRestoresItsOwner()
    {
        await using var context = await NewBadgeContextAsync(390, 844, true);
        var page = await OpenCollectibleFixtureAsync(context);
        var badge = page.Locator("#collectible-fixture .badge-class").First;
        var tip = page.Locator("#badge-tip");
        await badge.TapAsync();
        await Assertions.Expect(tip).ToHaveAttributeAsync("aria-modal", "true");
        Assert.True(await tip.EvaluateAsync<bool>("el=>el.matches(':modal')"));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(tip.Locator("button")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(tip.Locator(".badge-tip-content")).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(390, 650);
        await Assertions.Expect(tip).ToBeVisibleAsync();
        Assert.Equal("hidden", await page.EvaluateAsync<string>("document.body.style.overflow"));
        await page.Mouse.ClickAsync(10, 10); // Native dialog backdrop.
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await Assertions.Expect(badge).ToBeFocusedAsync();
        Assert.Equal("", await page.EvaluateAsync<string>("document.body.style.overflow"));
        await badge.TapAsync();
        await badge.EvaluateAsync("el=>el.remove()");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        Assert.Equal("", await page.EvaluateAsync<string>("document.body.style.overflow"));

        await page.EvaluateAsync("""
            () => window.setBadges({Badges:Array.from({length:35},()=>({BadgeLabel:'Best domain – inflammation',Place:1}))},document.querySelector('#collectible-fixture'))
            """);
        var chip = page.Locator("#collectible-fixture .badge-overflow-count");
        await chip.TapAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-mini")).ToHaveCountAsync(32);
        await page.Keyboard.PressAsync("Control+End");
        await Assertions.Expect(tip.Locator(".badge-tip-row").Last).ToBeInViewportAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(chip).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Collectible_RapidUpgradeSwitchExitAndRuntimeReducedMotionStayConsistent()
    {
        await using var context = await NewBadgeContextAsync(1440, 900, false);
        var page = await OpenCollectibleFixtureAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.EvaluateAsync("""
            () => window.setBadges({Badges:[{BadgeLabel:'≥2 submissions'},{BadgeLabel:'Best domain – vitamin D',Place:1}]},document.querySelector('#collectible-fixture'))
            """);
        var badges = page.Locator("#collectible-fixture .badge-class");
        var tip = page.Locator("#badge-tip");
        await badges.First.HoverAsync();
        await badges.First.ClickAsync();
        await Assertions.Expect(tip).ToHaveAttributeAsync("role", "dialog");
        await badges.Nth(1).ClickAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-title")).ToHaveTextAsync("Sun God");
        Assert.True(await page.Locator(".badge-tip-exit").EvaluateAllAsync<bool>("els=>els.every(el=>el.inert && el.getAttribute('aria-hidden')==='true' && !el.querySelector('[id]'))"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator(".badge-tip-exit")).ToHaveCountAsync(0);
        Assert.False(await tip.EvaluateAsync<bool>("el=>el.getAnimations({subtree:true}).some(a=>a.playState==='running')"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await Assertions.Expect(badges.Nth(1)).ToBeFocusedAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await badges.First.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".badge-tip-exit")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[1,null,\"previous\"]")]
    [InlineData("unavailable")]
    public async Task Collectible_CelebratesOnlyGainedArrivalsOnceEvenWithoutStorage(string saved)
    {
        await using var context = await NewBadgeContextAsync(1440, 900, false);
        await context.AddInitScriptAsync($$"""
            (() => {
                const value={{System.Text.Json.JsonSerializer.Serialize(saved)}};
                if(value==='unavailable') { Storage.prototype.getItem=()=>{throw new Error('blocked')};Storage.prototype.setItem=()=>{throw new Error('blocked')}; }
                else sessionStorage.setItem('lwcBadgeCelebrated:v1',value);
            })()
            """);
        var page = await OpenCollectibleFixtureAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        var result = await page.EvaluateAsync<bool[]>("""
            () => {
                const row=document.createElement('tr');row.dataset.eventId='award';row.dataset.day='2099-1-1';
                const cell=document.createElement('td');cell.className='event-message-cell badge-section';row.append(cell);document.querySelector('table').append(row);
                window.setBadges({Badges:[{BadgeLabel:'≥2 submissions'}]},cell);
                const badge=()=>cell.querySelector('.badge-class');
                window.LwcBadges.celebrate(row);
                const unrelated=!badge().classList.contains('badge-earned');
                row.dataset.badgeEarned='false';window.LwcBadges.celebrate(row);
                const lost=!badge().classList.contains('badge-earned');
                row.dataset.badgeEarned='true';window.LwcBadges.celebrate(row);
                const gained=badge().classList.contains('badge-earned');
                window.setBadges({Badges:[{BadgeLabel:'≥2 submissions'}]},cell);window.LwcBadges.celebrate(row);
                const duplicate=!badge().classList.contains('badge-earned');
                return [unrelated,lost,gained,duplicate];
            }
            """);
        Assert.All(result, Assert.True);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator(".badge-earned")).ToHaveCountAsync(0);
    }

    private static async Task<IPage> OpenCollectibleFixtureAsync(IBrowserContext context, int fontSize = 16)
    {
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await page.WaitForFunctionAsync("()=>typeof window.setBadges==='function'");
        await page.EvaluateAsync("""
            size=>{
                document.documentElement.style.fontSize=size+'px';
                const host=document.createElement('div');host.id='collectible-fixture';host.className='badge-section';
                host.style.cssText='position:fixed;left:8px;top:40%;z-index:10000';document.body.append(host);
                window.setBadges({Badges:[{BadgeLabel:'≥2 submissions'}]},host);
            }
            """, fontSize);
        await page.EvaluateAsync("()=>document.fonts.ready");
        return page;
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task Collectible_ClosesInsideTheAthleteDialogWithoutClosingItsParent(int width)
    {
        await using var context = await NewBadgeContextAsync(width, 844, false);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        await page.WaitForFunctionAsync("()=>document.querySelectorAll('#modalBadgeStrip .modal-badge-item').length>=7");
        // Same shared renderer in the enclosing profile's non-inert portal.
        await page.EvaluateAsync("""
            () => {
                const host=document.createElement('div');host.id='nested-collectible';host.className='badge-section';
                document.querySelector('#detailsModal .modal-content').prepend(host);
                window.setBadges({Badges:[{BadgeLabel:'≥2 submissions'}]},host);
            }
            """);
        var badge = page.Locator("#nested-collectible .badge-class");
        await badge.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var tip = page.Locator("#badge-tip");
        await Assertions.Expect(tip).ToHaveAttributeAsync("role", "dialog");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(tip.Locator("button")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await Assertions.Expect(badge).ToBeFocusedAsync();
        // A second Escape belongs to the profile again.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }
}
