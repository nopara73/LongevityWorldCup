using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PagerPaintBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task Paging_DoesNotForceDecorativeLayoutInsideTheInputTask(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Locator(".leaderboard tbody .athlete-name").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("""
            () => {
                window.pagerLayoutReads = [];
                const selector = '.age-board-figure, #ageRadarCenterValue, #ageRadarCenterBio, #modalProfilePic, input, select, textarea, [role="combobox"]';
                for (const method of ['getClientRects', 'checkVisibility']) {
                    const original = Element.prototype[method];
                    Element.prototype[method] = function (...args) {
                        if (window.insidePagerTask && this.matches(selector))
                            window.pagerLayoutReads.push({ method, id: this.id });
                        return Reflect.apply(original, this, args);
                    };
                }
                const computedStyle = window.getComputedStyle;
                window.getComputedStyle = function (element, ...args) {
                    if (window.insidePagerTask && element === document.documentElement)
                        window.pagerLayoutReads.push({ method: 'getComputedStyle', id: 'root' });
                    return computedStyle.call(this, element, ...args);
                };
                const focus = HTMLElement.prototype.focus;
                HTMLElement.prototype.focus = function (...args) {
                    if (window.insidePagerTask && this.matches('.athlete-step')
                        && document.querySelector('#detailsModal .modal-content').classList.contains('is-loading'))
                        window.pagerLayoutReads.push({ method: 'focusDuringHydration', id: this.dataset.step });
                    return focus.apply(this, args);
                };
                document.addEventListener('click', event => {
                    if (!event.target.closest('.athlete-step')) return;
                    window.insidePagerTask = true;
                    requestAnimationFrame(() => { window.insidePagerTask = false; });
                }, true);
            }
            """);
        var initial = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug");
        foreach (var direction in new[] { "next", "previous", "next", "previous" })
        {
            var previous = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug");
            await page.Locator($".modal-content > .athlete-steps [data-step={direction}]").ClickAsync();
            await page.WaitForFunctionAsync("slug => { const card = document.querySelector('#detailsModal .modal-content'); return card.dataset.athleteSlug !== slug && !card.classList.contains('is-loading'); }", previous);
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        }
        Assert.Equal(initial, await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug"));
        await Assertions.Expect(page.Locator(".modal-content > .athlete-steps [data-step=next]")).ToBeFocusedAsync();
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.pagerLayoutReads.map(read => JSON.stringify(read))"));
        await Assertions.Expect(page.Locator("#athleteName")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Counting_KeepsSemanticColourAndReadableForcedColourText(bool forcedColors)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ColorScheme = ColorScheme.Dark,
            ForcedColors = forcedColors ? ForcedColors.Active : ForcedColors.None
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Locator(".leaderboard tbody .athlete-name").First.ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("""
            () => {
                const host = document.createElement('div');
                host.innerHTML = '<span id="countColourReference" style="color:var(--lwc-success)">42</span><span id="countPaintProbe" style="color:var(--lwc-success)" data-lwc-count="40">42</span>';
                document.querySelector('#detailsModal .modal-content').prepend(host);
            }
            """);
        var probe = page.Locator("#countPaintProbe");
        await Assertions.Expect(probe).ToHaveTextAsync("42");
        Assert.True(await probe.EvaluateAsync<bool>("e => getComputedStyle(e).color === getComputedStyle(document.getElementById('countColourReference')).color"));
        if (forcedColors)
        {
            Assert.True(await probe.EvaluateAsync<bool>("e => getComputedStyle(e).webkitTextFillColor === getComputedStyle(e).color"));
            Assert.Equal("none", await probe.EvaluateAsync<string>("e => getComputedStyle(e, '::after').content"));
        }
        else
        {
            Assert.Equal("rgba(0, 0, 0, 0)", await probe.EvaluateAsync<string>("e => getComputedStyle(e).webkitTextFillColor"));
            Assert.True(await probe.EvaluateAsync<bool>("e => getComputedStyle(e, '::after').webkitTextFillColor === getComputedStyle(e).color"));
        }
    }
}
