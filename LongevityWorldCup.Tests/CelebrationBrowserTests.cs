using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class CelebrationBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");
        await page.WaitForTimeoutAsync(900);
        await page.EvaluateAsync("() => LwcCelebrate.skip()");
        return page;
    }

    [Fact]
    public async Task ReviewArrivalUsesAcceptedSubmissionIdentityInsteadOfTheDay()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/review");
        await page.WaitForTimeoutAsync(800);
        Assert.Equal(0, await page.Locator(".lwc-celebration").CountAsync());
        foreach (var id in new[] { "first", "second" })
        {
            await page.EvaluateAsync("id => sessionStorage.setItem('reviewSubmission', JSON.stringify({kind:'result',id}))", id);
            await page.GotoAsync("/review?from=proof-upload");
            await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
            Assert.True(await page.EvaluateAsync<bool>("key => LwcCelebrate.seen(key)", $"review:result:{id}"));
            await page.ReloadAsync();
            await page.WaitForTimeoutAsync(800);
            Assert.Equal(0, await page.Locator(".lwc-celebration").CountAsync());
        }
    }

    [Fact]
    public async Task SuccessAlertRemainsInteractiveAndInputSuppressesItsDelayedFlourish()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenAsync(context);
        await page.EvaluateAsync("() => LwcCelebrate.play(document.querySelector('.application-review-seal'))");
        await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
        await page.EvaluateAsync("() => { void customAlert('Change request submitted!'); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(600);
        Assert.Equal(0, await page.Locator(".lwc-celebration").CountAsync());
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        await page.EvaluateAsync("() => { void customAlert('Change request submitted!'); }");
        await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
        Assert.True(await page.Locator("#custom-alert-close").EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(); return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)); }"));
        await page.Locator("#custom-alert-close").ClickAsync();
        await Assertions.Expect(page.Locator(".lwc-celebration")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("pointerdown")]
    [InlineData("keydown")]
    [InlineData("scroll")]
    public async Task InputCancelsPendingMomentsWithoutConsumingThem(string input)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            input => {
                LwcCelebrate.play(document.querySelector('.application-review-seal'), { once: 'pending', delay: 150 });
                window.dispatchEvent(new Event(input));
            }
            """, input);
        await page.WaitForTimeoutAsync(350);
        Assert.Equal(0, await page.Locator(".lwc-celebration").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("LwcCelebrate.seen('pending')"));
    }

    [Fact]
    public async Task InvisibleTargetsDoNotConsumeAMilestoneAndVisibleOnesPersist()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                const target = document.querySelector('.application-review-seal');
                target.style.visibility = 'hidden';
                LwcCelebrate.play(target, { once: 'visible' });
            }
            """);
        await page.WaitForTimeoutAsync(100);
        Assert.False(await page.EvaluateAsync<bool>("LwcCelebrate.seen('visible')"));
        Assert.Equal(0, await page.Locator(".lwc-celebration").CountAsync());
        await page.EvaluateAsync("""
            () => {
                const target = document.querySelector('.application-review-seal');
                target.style.visibility = '';
                LwcCelebrate.play(target, { once: 'visible' });
                LwcCelebrate.play(target, { once: 'visible' });
            }
            """);
        await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
        await page.ReloadAsync();
        Assert.False(await page.EvaluateAsync<bool>("LwcCelebrate.play(document.querySelector('.application-review-seal'), { once: 'visible' })"));
    }

    [Fact]
    public async Task PositioningAnotherScrollerDoesNotCancelAVisibleMilestone()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                LwcCelebrate.play(document.querySelector('.application-review-seal'), { once: 'board-update', delay: 100 });
                document.querySelector('.application-review-trail').dispatchEvent(new Event('scroll'));
            }
            """);
        await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
        Assert.True(await page.EvaluateAsync<bool>("LwcCelebrate.seen('board-update')"));
        await page.EvaluateAsync("document.querySelector('.application-review-panel').dispatchEvent(new Event('scroll'))");
        await Assertions.Expect(page.Locator(".lwc-celebration")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AnimationFailureFallsBackWithoutErrorsOrOrphanLayers()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenAsync(context);
        await page.WaitForTimeoutAsync(150);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.EvaluateAsync("""
            () => {
                Element.prototype.animate = () => { throw Error('Animation unavailable'); };
                LwcCelebrate.play(document.querySelector('.application-review-seal'), { level: 'burst' });
            }
            """);
        await page.WaitForTimeoutAsync(100);
        Assert.Empty(errors);
        await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".lwc-celebration")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task RemovingTheTargetAndChangingMotionPreferenceClearTheMoment()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenAsync(context);
        await page.WaitForTimeoutAsync(150);
        await page.EvaluateAsync("() => LwcCelebrate.play(document.querySelector('.application-review-seal'), { level: 'burst' })");
        await Assertions.Expect(page.Locator(".lwc-celebration")).ToHaveCountAsync(1);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator(".lwc-celebration")).ToHaveCountAsync(0, new() { Timeout = 500 });
        await page.EvaluateAsync("() => LwcCelebrate.play(document.querySelector('.application-review-seal'))");
        await Assertions.Expect(page.Locator(".lwc-celebration.is-static")).ToHaveCountAsync(1);
        await page.EvaluateAsync("document.querySelector('.application-review-seal').remove()");
        await Assertions.Expect(page.Locator(".lwc-celebration")).ToHaveCountAsync(0, new() { Timeout = 500 });
    }
}

