using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PointerLayerBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> ContextAsync(int width = 1440, bool dark = false, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new() {
            ViewportSize = new() { Width = width, Height = 900 },
            HasTouch = touch,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/*", route => route.Request.Method is "GET" or "HEAD"
            ? route.FallbackAsync() : route.FulfillAsync(new() { Status = 200, Body = "{}" }));
        return context;
    }

    private async Task<IPage> FixtureAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(App.BaseAddress, "/health").ToString());
        await page.SetContentAsync("""
            <style>body{margin:20px;font:16px Arial}button,a,input{margin:8px;padding:12px}
            #space{height:1400px}td{padding:12px}#clip{width:90px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
            </style>
            <button id="one" title="First tip" aria-describedby="help"><svg width="14" height="14"><path d="M0 0h14v14H0z"/></svg></button>
            <button id="two" title="Second tip">Second</button><button id="plain">Plain</button><span id="help">Help</span>
            <span id="clip">A long clipped caption that remains readable in a tooltip</span>
            <div role="toolbar"><input id="radio1" name="choice" type="radio" checked><input id="radio2" name="choice" type="radio">
            <input id="edit" value="Editable text"><div id="rich" contenteditable><span>Editable rich text</span></div>
            <div id="plainEdit" contenteditable="plaintext-only">Editable plain text</div><button id="last">Last</button></div>
            <table><tbody data-lwc-arrows="rows"><tr><td><a href="#" id="row1">First athlete</a></td></tr>
            <tr style="visibility:hidden"><td><a href="#" id="invisible">Hidden athlete</a></td></tr>
            <tr><td><a href="#" id="row2">Second athlete</a></td></tr></tbody></table>
            <details open><summary>More</summary><button id="detail">Inside</button></details>
            <dialog><button id="modal" title="Dialog action">Modal</button></dialog><div id="space"></div>
            """);
        await page.AddStyleTagAsync(new() { Url = new Uri(App.BaseAddress, "/css/pointer.css").ToString() });
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/pointer.js").ToString() });
        return page;
    }

    [Fact]
    public async Task Tooltip_NativeTextSelectionSurvivesDraggingOutsideAndReleasesOnOutsideClick()
    {
        await using var context = await ContextAsync(390);
        var page = await FixtureAsync(context);
        const string explanation = "Rank by bortz age (biological age). Click again to show default ranking.";
        await page.Locator("#two").EvaluateAsync("(e, text) => e.title = text", explanation);
        await page.Locator("#two").HoverAsync();
        var tip = page.Locator(".lwc-tip");
        await Expect(tip).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(200);
        var text = await page.Locator(".lwc-tip-text").BoundingBoxAsync();
        Assert.NotNull(text);
        await page.Mouse.MoveAsync(text.X + 10, text.Y + 8);
        await page.Mouse.DownAsync();
        try
        {
            await page.Mouse.MoveAsync(388, 200, new() { Steps = 5 });
            var selection = await page.EvaluateAsync<string>("window.getSelection().toString()");
            Assert.NotEmpty(selection);
            await page.WaitForTimeoutAsync(450);
            await Expect(tip).ToBeVisibleAsync();
            Assert.Equal(selection, await page.EvaluateAsync<string>("window.getSelection().toString()"));
        }
        finally { await page.Mouse.UpAsync(); }
        await page.WaitForTimeoutAsync(250);
        await Expect(tip).ToBeVisibleAsync();
        await page.Locator("#plain").ClickAsync();
        await Expect(tip).ToBeHiddenAsync();
        Assert.Equal(explanation, await page.Locator("#two").GetAttributeAsync("title"));
    }

    [Theory]
    [InlineData(390, false)]
    [InlineData(390, true)]
    [InlineData(320, false)]
    [InlineData(1440, false)]
    public async Task Tooltip_WiderContentAfterARightEdgeOwnerKeepsItsViewportGutter(int width, bool dark)
    {
        await using var context = await ContextAsync(width, dark);
        var page = await FixtureAsync(context);
        const string explanation = "Rank by bortz age (biological age). Click again to show default ranking.";
        await page.Locator("#one").EvaluateAsync("e => e.style.cssText = 'position:fixed;right:12px;top:20px'");
        await page.Locator("#two").EvaluateAsync("(e, text) => { e.title = text; e.style.cssText = 'position:fixed;left:20px;top:150px'; }", explanation);
        await page.Locator("#one").HoverAsync();
        await Expect(page.Locator(".lwc-tip")).ToHaveTextAsync("First tip");
        await page.Locator("#two").HoverAsync();
        await Expect(page.Locator(".lwc-tip")).ToHaveTextAsync(explanation);
        await page.WaitForTimeoutAsync(200);
        var geometry = await page.Locator(".lwc-tip").EvaluateAsync<double[]>("""
            e => { const r = e.getBoundingClientRect(); return [r.left, r.right, document.documentElement.clientWidth, e.scrollWidth - e.clientWidth, e.scrollHeight - e.clientHeight]; }
            """);
        Assert.True(geometry[0] >= 7.5 && geometry[1] <= geometry[2] - 7.5,
            $"The complete tooltip must retain its viewport gutter: {string.Join(", ", geometry)}");
        Assert.Equal(0, geometry[3]);
        Assert.Equal(0, geometry[4]);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("focus")]
    [InlineData("remove")]
    [InlineData("escape")]
    [InlineData("touch")]
    public async Task PendingTooltip_CancelsAndRestoresItsOwner(string ending)
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        if (ending == "focus") await page.Keyboard.PressAsync("Tab");
        else await page.Locator("#one svg").HoverAsync();
        await page.WaitForTimeoutAsync(70);
        if (ending == "focus") await page.Keyboard.PressAsync("Tab");
        else if (ending == "leave") await page.Mouse.MoveAsync(800, 600);
        else if (ending == "escape") await page.Keyboard.PressAsync("Escape");
        else if (ending == "touch") await page.Locator("#plain").DispatchEventAsync("pointerdown", new { pointerType = "touch" });
        else await page.EvaluateAsync("() => { window.removed = document.getElementById('one'); removed.remove(); }");
        await page.WaitForTimeoutAsync(750);
        Assert.Equal("First tip", await page.EvaluateAsync<string>("() => (window.removed || document.getElementById('one')).getAttribute('title')"));
        Assert.Null(await page.EvaluateAsync<string?>("() => (window.removed || document.getElementById('one')).getAttribute('data-lwc-tip-title')"));
        Assert.Equal("help", await page.EvaluateAsync<string>("() => (window.removed || document.getElementById('one')).getAttribute('aria-describedby')"));
        Assert.NotEqual("First tip", await page.EvaluateAsync<string?>("() => document.querySelector('.lwc-tip.is-open')?.textContent"));
    }

    [Fact]
    public async Task Tooltip_StaysHoverableAndPreservesPageTitleUpdates()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator("#one").HoverAsync();
        await Expect(page.Locator(".lwc-tip.is-open")).ToHaveTextAsync("First tip");
        await page.Locator(".lwc-tip").HoverAsync();
        await page.WaitForTimeoutAsync(240);
        await Expect(page.Locator(".lwc-tip.is-open")).ToBeVisibleAsync();
        await page.Locator("#one").EvaluateAsync("e => e.title = 'Updated tip'");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(".lwc-tip")).ToBeHiddenAsync();
        await Expect(page.Locator("#one")).ToHaveAttributeAsync("title", "Updated tip");
        await Expect(page.Locator("#one")).ToHaveAttributeAsync("aria-describedby", "help");
        Assert.Null(await page.Locator("#one").GetAttributeAsync("aria-label"));
    }

    [Theory]
    [InlineData("touch")]
    [InlineData("mouse")]
    public async Task LongTooltip_HybridTouchCanScrollWithoutDismissalOrReopening(string ending)
    {
        await using var context = await ContextAsync(390, false, true);
        var page = await FixtureAsync(context);
        await page.Locator("#two").EvaluateAsync("e => e.title = 'All words remain reachable in this explanation. '.repeat(150)");
        await page.Mouse.MoveAsync(2, 2);
        await page.Locator("#two").HoverAsync();
        var tip = page.Locator(".lwc-tip");
        await Expect(tip).ToBeVisibleAsync();
        Assert.True(await tip.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"));
        await page.EvaluateAsync("""
            () => {
                window.tipInterrupted = false;
                for (const type of ['gotpointercapture', 'pointercancel']) document.addEventListener(type, () => {
                    if (document.querySelector('.lwc-tip').hidden) window.tipInterrupted = true;
                }, true);
            }
            """);
        var box = await tip.BoundingBoxAsync();
        Assert.NotNull(box);
        var x = box.X + box.Width / 2;
        var y = Math.Min(700, box.Y + box.Height - 30);
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y, id = 1 } } });
        for (var step = 1; step <= 6; step++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x, y = y - step * 40, id = 1 } } });
            await page.WaitForTimeoutAsync(30);
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await cdp.DetachAsync();
        await page.WaitForTimeoutAsync(250);
        await Expect(tip).ToBeVisibleAsync();
        Assert.True(await tip.EvaluateAsync<bool>("e => e.scrollTop > 30"), "The native touch gesture must actually scroll the explanation.");
        Assert.False(await page.EvaluateAsync<bool>("window.tipInterrupted"), "Pointer capture or cancellation dismissed and reopened the tip during the gesture.");
        // Stay beyond Chromium's touch hit-test radius around the scrollable tip.
        if (ending == "touch") await page.Touchscreen.TapAsync(388, 885);
        else await page.Mouse.MoveAsync(388, 885);
        await page.WaitForTimeoutAsync(750);
        await Expect(tip).ToBeHiddenAsync();
        Assert.Equal(ending == "touch" ? "touch" : "pointer", await page.EvaluateAsync<string>("window.LwcPointer.modality()"));
        await page.Locator("#two").HoverAsync();
        await Expect(tip).ToBeVisibleAsync();
        Assert.Equal("pointer", await page.EvaluateAsync<string>("window.LwcPointer.modality()"));
    }

    [Fact]
    public async Task PointerDecorations_DoNotRemeasureUnrelatedFieldsAndLabelsKeepNativeFocus()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator("#edit").EvaluateAsync("field => field.insertAdjacentHTML('beforebegin', '<label id=editLabel for=edit>Edit</label>')");
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/form-controls.js").ToString() });
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await page.EvaluateAsync("""
            () => {
                window.unrelatedFieldReads = 0;
                const original = Element.prototype.getClientRects;
                Element.prototype.getClientRects = function(...args) {
                    if (this.id === 'edit') window.unrelatedFieldReads++;
                    return original.apply(this, args);
                };
            }
            """);
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator(".lwc-tip.is-open")).ToHaveTextAsync("First tip");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.unrelatedFieldReads"));
        await page.Locator("#edit").FocusAsync();
        await Expect(page.Locator("#editLabel")).ToHaveAttributeAsync("data-lwc-label", "focus block");
        await page.Locator("#plain").FocusAsync();
        await Expect(page.Locator("#editLabel")).ToHaveAttributeAsync("data-lwc-label", "block");
    }

    [Fact]
    public async Task PointerSweep_DoesNotMeasureLayoutUntilHoverIntent()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        var reads = await page.EvaluateAsync<int>("""
            () => {
                let reads = 0;
                const descriptor = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollWidth');
                Object.defineProperty(Element.prototype, 'scrollWidth', { configurable:true, get() { reads++; return descriptor.get.call(this); } });
                try {
                    for (const id of ['clip', 'plain', 'last', 'row1']) {
                        const e = document.getElementById(id);
                        e.dispatchEvent(new PointerEvent('pointerover', {bubbles:true,pointerType:'mouse'}));
                        e.dispatchEvent(new PointerEvent('pointerout', {bubbles:true,pointerType:'mouse',relatedTarget:document.body}));
                    }
                    return reads;
                } finally { Object.defineProperty(Element.prototype, 'scrollWidth', descriptor); }
            }
            """);
        Assert.Equal(0, reads);
        await page.WaitForTimeoutAsync(650);
        await Expect(page.Locator(".lwc-tip.is-open")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("pointercancel")]
    [InlineData("gotpointercapture")]
    [InlineData("pagehide")]
    public async Task InterruptedPointer_ReleasesTitlesAndCannotReopenFromAnOldFrame(string interruption)
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator("#one").HoverAsync();
        await Expect(page.Locator(".lwc-tip.is-open")).ToBeVisibleAsync();
        if (interruption == "pagehide") await page.EvaluateAsync("() => window.dispatchEvent(new Event('pagehide'))");
        else await page.Locator("#one").DispatchEventAsync(interruption);
        await page.WaitForTimeoutAsync(700);
        await Expect(page.Locator(".lwc-tip")).ToBeHiddenAsync();
        await Expect(page.Locator("#one")).ToHaveAttributeAsync("title", "First tip");
    }

    [Fact]
    public async Task HybridInput_CancelsTouchTipsAndRestoresMouseHoverWithoutAButtonPress()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator("#one").DispatchEventAsync("pointerdown", new { pointerType = "touch" });
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-lwc-input", "touch");
        await page.Locator("#one").DispatchEventAsync("pointerover", new { pointerType = "touch" });
        await page.WaitForTimeoutAsync(650);
        await Expect(page.Locator(".lwc-tip.is-open")).ToHaveCountAsync(0);
        await page.Locator("#two").HoverAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-lwc-input", "pointer");
        await Expect(page.Locator(".lwc-tip.is-open")).ToHaveTextAsync("Second tip");
    }

    [Theory]
    [InlineData("#radio1", "ArrowRight")]
    [InlineData("#rich", "End")]
    [InlineData("#plainEdit", "Home")]
    [InlineData("#edit", "ArrowRight")]
    public async Task NativeEditingAndRadioKeys_KeepTheirOwner(string selector, string key)
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator(selector).FocusAsync();
        await page.Keyboard.PressAsync(key);
        if (selector == "#radio1") await Expect(page.Locator("#radio2")).ToBeCheckedAsync();
        else await Expect(page.Locator(selector)).ToBeFocusedAsync();
    }

    [Fact]
    public async Task RowArrows_SkipInvisibleRowsAndRespectSelectionModifiersAndPageHandlers()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator("#row1").FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(page.Locator("#row2")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Shift+ArrowUp");
        await Expect(page.Locator("#row2")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => document.addEventListener('keydown', e => e.preventDefault(), {once:true})");
        await page.Keyboard.PressAsync("ArrowUp");
        await Expect(page.Locator("#row2")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => { const r = document.createRange(); r.selectNodeContents(document.getElementById('row2')); getSelection().addRange(r); }");
        await page.Keyboard.PressAsync("Home");
        await Expect(page.Locator("#row2")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Focus_RemainsVisibleImmediatelyAndAfterRuntimePreferenceChanges()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator(".lwc-focus-ring.is-on")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator("#two").EvaluateAsync<string>("e => getComputedStyle(e).outlineColor"));
        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.Active });
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator("#two").EvaluateAsync<string>("e => getComputedStyle(e).outlineColor"));
        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.None, ReducedMotion = ReducedMotion.Reduce });
        Assert.True(await page.Locator(".lwc-focus-ring").EvaluateAsync<bool>("e => e.getAnimations().every(a => ['finished','idle'].includes(a.playState))"));
        await page.EvaluateAsync("() => document.querySelector('dialog').showModal()");
        await Expect(page.Locator("#modal")).ToBeFocusedAsync();
        await Expect(page.Locator(".lwc-focus-ring.is-on")).ToHaveCountAsync(0);
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#two")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    [InlineData(320, false)]
    public async Task Tooltip_ShortContentFitsAndLongContentRemainsReachable(int width, bool dark)
    {
        await using var context = await ContextAsync(width, dark);
        var page = await FixtureAsync(context);
        await page.AddStyleTagAsync(new() { Content = "html{font-size:200%}" });
        await page.Locator("#one").HoverAsync();
        var tip = page.Locator(".lwc-tip.is-open");
        await Expect(tip).ToBeVisibleAsync();
        Assert.True(await tip.EvaluateAsync<bool>("e => e.scrollHeight <= e.clientHeight && e.scrollWidth <= e.clientWidth"));
        await page.Mouse.MoveAsync(width - 10, 850);
        await page.Locator("#two").EvaluateAsync("e => e.title = 'Long content with all words preserved. '.repeat(180)");
        await page.Locator("#two").HoverAsync();
        await Expect(tip).ToContainTextAsync("Long content");
        Assert.True(await tip.EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(); return r.top>=0 && r.bottom<=innerHeight && r.left>=0 && r.right<=innerWidth; }"));
        await page.Locator("#two").FocusAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => ['ctrlKey', 'metaKey', 'altKey', 'isComposing'].every(modifier => {
                const event = new KeyboardEvent('keydown', {key:'Tab', bubbles:true, cancelable:true, [modifier]:true});
                document.activeElement.dispatchEvent(event);
                return !event.defaultPrevented;
            })
            """));
        await tip.HoverAsync();
        await tip.FocusAsync();
        await page.Keyboard.PressAsync("Control+End");
        await Expect(tip).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.True(await tip.EvaluateAsync<bool>("e => e.scrollTop + e.clientHeight >= e.scrollHeight - 2"));
    }
}
