using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class LeagueFilterSheetBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task PointerAndKeyboard_FocusAVisibleControlWithTheMatchingIndicator(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        if (width > 768)
        {
            await page.Locator(".sidebar-rail").ClickAsync();
            await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
            Assert.False(await page.Locator(".sidebar-close").EvaluateAsync<bool>("e => e.matches(':focus-visible')"));
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement.matches('.sidebar input')"));
        }
        else
        {
            await page.Locator(".sidebar-toggle").ClickAsync();
            await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
            Assert.False(await page.Locator(".sidebar-close").EvaluateAsync<bool>("e => e.matches(':focus-visible')"));
        }
        await page.Locator(".sidebar-close").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        Assert.False(await page.Locator(".sidebar-toggle").EvaluateAsync<bool>("e => e.matches(':focus-visible') || e.classList.contains('is-keyboard-focus')"));
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        Assert.True(await page.Locator(".sidebar-toggle").EvaluateAsync<bool>("e => e.matches(':focus-visible') && e.classList.contains('is-keyboard-focus')"));
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("input[name=leagueTrack][value=Amateur i]").CheckAsync();
        await page.Locator("#clearSidebarFiltersBtn").ClickAsync();
        var afterClear = page.Locator(width > 768 ? ".sidebar-toggle" : ".sidebar-close");
        await Assertions.Expect(afterClear).ToBeFocusedAsync();
        Assert.False(await afterClear.EvaluateAsync<bool>("e => e.matches(':focus-visible') || e.classList.contains('is-keyboard-focus')"));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task Sheet_ConfinesFocusAndOnlyDismissesADownwardHeadingDrag(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, HasTouch = true, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        // Exercise the obsolete page-level touch listener independently of the
        // site's intentional pointer-based Back gesture.
        await page.EvaluateAsync("""
            () => {
                const target=document.querySelector('h1');
                const start=new Touch({identifier:1,target,clientX:40,clientY:100});
                const end=new Touch({identifier:1,target,clientX:170,clientY:100});
                target.dispatchEvent(new TouchEvent('touchstart',{bubbles:true,touches:[start],changedTouches:[start]}));
                target.dispatchEvent(new TouchEvent('touchend',{bubbles:true,touches:[],changedTouches:[end]}));
            }
            """);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Locator(".sidebar-panel")).ToHaveAttributeAsync("role", "region");
        await page.Locator(".sidebar-toggle").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator(".sidebar-panel")).ToHaveAttributeAsync("aria-modal", "true");
        Assert.True(await page.Locator("#showLeaderboardResults").EvaluateAsync<bool>("""
            button => {
                const r=button.getBoundingClientRect(),sheet=button.closest('.sidebar').getBoundingClientRect();
                return r.top>=sheet.top && r.bottom<=innerHeight && r.bottom<=sheet.bottom
                    && button.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));
            }
            """));
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator("#showLeaderboardResults")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
        var sheet = (await page.Locator(".sidebar").BoundingBoxAsync())!;
        Assert.InRange(Math.Abs(sheet.Y + sheet.Height - height), 0, 1);
        var list = (await page.Locator(".sidebar-filter-list").BoundingBoxAsync())!;
        await DragAsync(page, (int)list.X + 50, (int)list.Y + 30, 0, 60);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        var heading = (await page.Locator(".sidebar-heading").BoundingBoxAsync())!;
        await DragAsync(page, (int)heading.X + 60, (int)heading.Y + 10, 90, 0);
        await Assertions.Expect(page.Locator("#view-ultimate")).ToBeCheckedAsync();
        await DragAsync(page, (int)heading.X + 60, (int)heading.Y + 10, 0, 75);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.body.classList.contains('sidebar-drawer-open')"));
        await page.Keyboard.PressAsync("Enter");
        var grab = (await page.Locator(".sidebar-grab").BoundingBoxAsync())!;
        await DragAsync(page, width / 2, (int)grab.Y + 7, 0, 75);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        await page.Keyboard.PressAsync("Enter");
        await page.Mouse.ClickAsync(5, Math.Max(1, sheet.Y / 2));
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Locator(".sidebar-panel")).ToHaveAttributeAsync("role", "region");
    }

    [Theory]
    [InlineData("/leaderboard")]
    [InlineData("/")]
    public async Task SearchAndFilters_KeepTheDeckOpenAndUpdateEveryShare(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.Locator("input[name=leagueTrack][value=Amateur i]").CheckAsync();
        await page.Locator("#athleteSearch").FillAsync("Michael");
        await page.WaitForFunctionAsync("() => new URL(location.href).searchParams.get('search') === 'michael'");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('.filter-section')].every(section => {
                const labels=[...section.querySelectorAll('li > label')];
                const counts=labels.map(e=>parseInt(e.querySelector('.filter-count').textContent,10)||0);
                const max=Math.max(1,...counts);
                return labels.every((e,i)=>Math.abs(parseFloat(e.style.getPropertyValue('--tile-share'))-counts[i]/max)<.0001);
            })
            """));
        var ranks = await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible").AllTextContentsAsync();
        Assert.NotEmpty(ranks);
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal(ranks, await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible").AllTextContentsAsync());
        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(page.Locator(".sidebar-panel")).ToHaveAttributeAsync("aria-modal", "true");
        await page.SetViewportSizeAsync(1440, 900);
        // The viewport command can return before the browser dispatches resize.
        await Assertions.Expect(page.Locator("body")).Not.ToHaveClassAsync(new Regex(@"\bsidebar-drawer-open\b"));
        await Assertions.Expect(page.Locator(".sidebar-panel")).ToHaveAttributeAsync("role", "region");
    }

    private static async Task DragAsync(IPage page, int x, int y, int dx, int dy)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y, id = 1 } } });
            for (var i = 1; i <= 8; i++)
                await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = x + dx * i / 8, y = y + dy * i / 8, id = 1 } } });
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        }
        finally { await cdp.DetachAsync(); }
    }
}
