using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class IconSystemBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task RecycledControls_KeepTheirHandlersFocusNamesAndProtectedArtwork()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        // Deliberately synthetic lifecycle probe, separate from real-page fit checks.
        await page.EvaluateAsync("""
            () => {
                const button = document.createElement('button'); button.id='iconProbe'; button.ariaLabel='Probe action';
                button.innerHTML='<i class="fa fa-link" aria-hidden="true"></i>';
                button.addEventListener('click', () => window.iconProbeClicks=(window.iconProbeClicks||0)+1);
                document.body.prepend(button); window.iconProbeHost=button.firstElementChild;
            }
            """);
        var button = page.Locator("#iconProbe");
        var host = button.Locator("i");
        await Assertions.Expect(host).ToHaveAttributeAsync("data-lwc-icon", "link");
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await host.EvaluateAsync("e => e.className='fa fa-check'");
        await Assertions.Expect(host).ToHaveAttributeAsync("data-lwc-icon", "check");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(button).ToHaveAccessibleNameAsync("Probe action");
        Assert.True(await host.EvaluateAsync<bool>("e => e === window.iconProbeHost"));
        Assert.Equal(1, await page.EvaluateAsync<int>("window.iconProbeClicks"));
        await Assertions.Expect(host.Locator("svg")).Not.ToHaveAttributeAsync("data-lwc-icon-enter", "draw");
        await host.EvaluateAsync("e => e.replaceChildren()");
        await Assertions.Expect(host.Locator("svg path")).ToHaveCountAsync(1);
        await button.EvaluateAsync("e => e.setAttribute('data-lwc-icon-keep','')");
        await Assertions.Expect(host.Locator("svg")).ToHaveCountAsync(0);
        await Assertions.Expect(host).Not.ToHaveAttributeAsync("data-lwc-icon", "check");
        await button.EvaluateAsync("e => e.removeAttribute('data-lwc-icon-keep')");
        await Assertions.Expect(host).ToHaveAttributeAsync("data-lwc-icon", "check");
        await button.EvaluateAsync("e => e.classList.add('badge-class')");
        await Assertions.Expect(host.Locator("svg")).ToHaveCountAsync(0);
        await button.EvaluateAsync("e => e.classList.remove('badge-class')");
        await Assertions.Expect(host.Locator("svg")).ToHaveCountAsync(1);
        await host.EvaluateAsync("e => e.className='fa-brands fa-github'");
        await Assertions.Expect(host.Locator("svg")).ToHaveCountAsync(0);
        await button.ClickAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("window.iconProbeClicks"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Feedback_RespectsStartupAndRuntimeReducedMotionWithoutReplayingOldStates(bool startReduced)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = startReduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.EvaluateAsync("""
            () => {
                const button=document.createElement('button'); button.id='feedbackProbe'; button.ariaLabel='Working probe';
                button.innerHTML='<i class="fa fa-link" aria-hidden="true"></i>';
                button.onclick=()=>button.firstElementChild.className='fa fa-spinner fa-spin'; document.body.prepend(button);
            }
            """);
        var button = page.Locator("#feedbackProbe");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "link");
        await button.ClickAsync();
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "spinner");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(button.Locator("svg")).ToHaveCSSAsync("animation-duration", "2.4s");
        await page.WaitForFunctionAsync("() => !document.querySelector('#feedbackProbe svg').hasAttribute('data-lwc-icon-enter')");
        await button.Locator("i").EvaluateAsync("e => e.className='fa fa-check'");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "check");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.Equal(0, await button.Locator("svg").EvaluateAsync<int>("e => e.getAnimations({subtree:true}).filter(a=>a instanceof CSSAnimation).length"));
        for (var i = 0; i < 3; i++)
        {
            await button.Locator("i").EvaluateAsync("e => e.className='fa fa-link'");
            await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "link");
            await button.Locator("i").EvaluateAsync("e => e.className='fa fa-check'");
            await Assertions.Expect(button.Locator("svg")).ToHaveAttributeAsync("data-lwc-icon-enter", "draw");
            await page.WaitForFunctionAsync("() => !document.querySelector('#feedbackProbe svg').hasAttribute('data-lwc-icon-enter')");
        }
    }

    [Fact]
    public async Task BatchedGlyphChanges_DoNotReadGeometryOrChurnAfterSettling()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.Keyboard.PressAsync("Shift");
        await page.EvaluateAsync("""
            () => {
                const root=document.createElement('div'); root.id='batchProbe';
                root.innerHTML=Array.from({length:100},()=>'<button aria-label="Synthetic batch"><i class="fa fa-link"></i></button>').join('');
                document.body.append(root);
            }
            """);
        await Assertions.Expect(page.Locator("#batchProbe i[data-lwc-icon=link]")).ToHaveCountAsync(100);
        await page.EvaluateAsync("""
            () => {
                const read=Element.prototype.getBoundingClientRect;
                window.iconGeometryReads=0; window.iconMutations=0;
                Element.prototype.getBoundingClientRect=function(...args) { if(this.matches('.lwc-icon')) window.iconGeometryReads++; return read.apply(this,args); };
                new MutationObserver(records=>window.iconMutations+=records.length).observe(document.getElementById('batchProbe'),{childList:true,subtree:true,attributes:true});
                document.querySelectorAll('#batchProbe i').forEach(e=>e.className='fa fa-check');
            }
            """);
        await Assertions.Expect(page.Locator("#batchProbe i[data-lwc-icon=check]")).ToHaveCountAsync(100);
        await page.WaitForFunctionAsync("() => !document.querySelector('#batchProbe [data-lwc-icon-enter]')");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.iconGeometryReads"));
        var mutations = await page.EvaluateAsync<int>("window.iconMutations");
        await page.EvaluateAsync("() => new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))");
        Assert.Equal(mutations, await page.EvaluateAsync<int>("window.iconMutations"));
        Assert.InRange(mutations, 100, 1500);
    }

    [Fact]
    public async Task TypedGlyphsAndOpticalTiers_FollowReusedControlsAndActualTextSize()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.EvaluateAsync("""
            () => {
                const button=document.createElement('button'); button.id='typedProbe'; button.ariaLabel='Close probe';
                button.textContent='?'; document.body.prepend(button); button.firstChild.data='×';
            }
            """);
        var button = page.Locator("#typedProbe");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "close");
        await button.FocusAsync();
        await button.EvaluateAsync("e => e.firstElementChild.style.fontSize='10px'");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon-tier", "xs");
        await button.EvaluateAsync("e => e.firstElementChild.style.fontSize='32px'");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon-tier", "l");
        await button.EvaluateAsync("e => { window.detachedIconProbe=e; e.remove(); }");
        await page.EvaluateAsync("() => new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))");
        await page.EvaluateAsync("() => { const e=window.detachedIconProbe; e.firstElementChild.style.fontSize='10px'; document.body.prepend(e); }");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon-tier", "xs");
        await button.FocusAsync();
        await button.EvaluateAsync("e => e.setAttribute('data-lwc-icon-keep','')");
        await Assertions.Expect(button).ToHaveTextAsync("×");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(button).ToHaveAccessibleNameAsync("Close probe");
        Assert.Equal("", await page.EvaluateAsync<string>("window.LwcIcons.svg('constructor')"));
    }

    [Fact]
    public async Task HiddenControlUpdates_ClearFeedbackThatNeverStartedBeforeRevealingTheControl()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.Keyboard.PressAsync("Shift");
        await page.EvaluateAsync("""
            () => { const button=document.createElement('button');button.id='hiddenIconProbe';button.ariaLabel='Hidden feedback probe';button.innerHTML='<i class="fa fa-link"></i>';document.body.prepend(button); }
            """);
        var button = page.Locator("#hiddenIconProbe");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "link");
        await button.EvaluateAsync("e => {e.hidden=true;e.firstElementChild.className='fa fa-check';}");
        await Assertions.Expect(button.Locator("i")).ToHaveAttributeAsync("data-lwc-icon", "check");
        await Assertions.Expect(button.Locator("svg")).Not.ToHaveAttributeAsync("data-lwc-icon-enter", "draw");
        await button.EvaluateAsync("e => e.hidden=false");
        await Assertions.Expect(button).ToBeVisibleAsync();
        Assert.Equal(0, await button.Locator("svg").EvaluateAsync<int>("e=>e.getAnimations({subtree:true}).filter(a=>a instanceof CSSAnimation).length"));
    }
}
