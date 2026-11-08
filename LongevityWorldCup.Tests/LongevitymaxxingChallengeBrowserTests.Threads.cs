using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(ColorScheme.Light, false)]
    [InlineData(ColorScheme.Dark, false)]
    [InlineData(ColorScheme.Light, true)]
    [InlineData(ColorScheme.Dark, true)]
    public async Task Threads_SpecialPostSurfacesSurviveHoverAndPermalinkWash(ColorScheme theme, bool hover)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ColorScheme = theme });
        var state = AdviceBrowserState(0);
        state["public"]!["systemDiscussionPosts"] = new JsonArray(new JsonObject
        {
            ["id"] = DiscussionAnnouncementId, ["kind"] = "announcement", ["participantId"] = "p2",
            ["displayName"] = "Ari", ["date"] = "2026-07-01", ["occurredAtUtc"] = "2026-07-01T09:00:00Z",
            ["lastActivityAtUtc"] = "2026-07-01T09:00:00Z", ["replyCount"] = 0, ["replies"] = new JsonArray(),
            ["announcement"] = new JsonObject { ["eventId"] = DiscussionAnnouncementId, ["title"] = "A new round", ["content"] = "The next round is open." }
        });
        var page = await OpenDiscussionPolishAsync(context, state);
        foreach (var selector in new[] { ".lmx-needs-advice", ".lmx-announcement" })
        {
            var post = page.Locator("#lmxNotes > " + selector);
            await post.ScrollIntoViewIfNeededAsync();
            await page.Mouse.MoveAsync(0, 0);
            await WaitForFiniteAnimationsAsync(page);
            var baseline = await post.EvaluateAsync<string[]>("e => [getComputedStyle(e).backgroundColor, getComputedStyle(e).boxShadow]");
            if (hover) await post.HoverAsync();
            else await post.EvaluateAsync("e => e.classList.add('lmx-discussion-target')");
            await WaitForFiniteAnimationsAsync(page);
            Assert.Equal(baseline, await post.EvaluateAsync<string[]>("e => [getComputedStyle(e).backgroundColor, getComputedStyle(e).boxShadow]"));
        }
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, false)]
    [InlineData(1440, true)]
    [InlineData(390, true)]
    public async Task Threads_RailGeometryDoesNotJumpWhenArrivalsFinish(int width, bool newPost)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        var page = await OpenDiscussionPolishAsync(context);
        var thread = DiscussionThread(page, "p7", 5);
        await page.EvaluateAsync("""
            () => {
                window.heldArrivals = [];
                const animate = Element.prototype.animate;
                Element.prototype.animate = function(...args) {
                    const animation = animate.apply(this,args);
                    if (this.matches('.lmx-note, .lmx-discussion-reply-item')) {
                        animation.pause(); animation.currentTime = 0; window.heldArrivals.push(animation);
                    }
                    return animation;
                };
            }
            """);
        await thread.EvaluateAsync("""
            (article, newPost) => {
                const incoming = (newPost ? article : article.querySelector('.lmx-discussion-reply-item')).cloneNode(true);
                incoming.querySelector('.lmx-discussion-permalink').href = '#unique-arrival';
                incoming.dataset.arrivalFixture = '';
                (newPost ? article.parentElement : article.querySelector('.lmx-discussion-reply-list')).append(incoming);
            }
            """, newPost);
        var incoming = page.Locator("[data-arrival-fixture]");
        await Assertions.Expect(incoming).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-arriving"));
        const string readGeometry = """
            e => {
                const post=e.matches('.lmx-note')?e:e.closest('.lmx-note');
                const replies=post.querySelector('[data-lmx-thread]');
                const reply=e.matches('.lmx-note')?e.querySelector('.lmx-discussion-reply-item'):e;
                return [replies.style.getPropertyValue('--lmx-rail-tail'), replies.style.getPropertyValue('--lmx-rail-reach'),
                    reply.style.getPropertyValue('--lmx-elbow-x'), reply.style.getPropertyValue('--lmx-elbow-y'), reply.style.getPropertyValue('--lmx-elbow-w')].map(parseFloat);
            }
            """;
        await page.EvaluateAsync("() => { window.LwcLmxDiscussion.measure(); return new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        var during = await incoming.EvaluateAsync<double[]>(readGeometry);
        await page.EvaluateAsync("() => window.heldArrivals.forEach(animation => animation.finish())");
        await Assertions.Expect(incoming).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-arriving"));
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var after = await incoming.EvaluateAsync<double[]>(readGeometry);
        for (var i = 0; i < during.Length; i++) Assert.InRange(during[i] - after[i], -.5, .5);
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Threads_FocusedTimestampKeepsItsTooltipWhenTheViewportChanges(ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenDiscussionPolishAsync(context);
        var timestamp = DiscussionThread(page, "p7", 5).Locator(".lmx-discussion-post-author .lmx-discussion-permalink");
        await timestamp.FocusAsync();
        var tooltip = page.Locator("#lmxDiscussionTimeTooltip");
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(timestamp).ToBeFocusedAsync();
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await Assertions.Expect(timestamp).ToHaveAttributeAsync("aria-describedby", "lmxDiscussionTimeTooltip");
        Assert.True(await tooltip.EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(); return r.left>=0 && r.right<=innerWidth && e.scrollWidth<=e.clientWidth && e.scrollHeight<=e.clientHeight; }"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tooltip).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Threads_RestingActionsRemainLegible(ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenDiscussionPolishAsync(context);
        await page.Mouse.MoveAsync(0, 0);
        await WaitForFiniteAnimationsAsync(page);
        var contrasts = await page.Locator("#lmxNotes .lmx-discussion-quiet-action:visible:not(:disabled)").EvaluateAllAsync<double[]>("""
            buttons => buttons.map(button => {
                const rgb = css => css.match(/[\d.]+/g).slice(0,3).map(Number);
                const style = getComputedStyle(button), opacity = Number(style.opacity);
                const probe = document.createElement('span');
                probe.style.background = 'var(--lmx-surface)'; button.append(probe);
                const bg = rgb(getComputedStyle(probe).backgroundColor); probe.remove();
                const fg = rgb(style.color).map((n,i) => n * opacity + bg[i] * (1 - opacity));
                const luminance = color => color.map(n => n / 255).map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4)
                    .reduce((sum,n,i) => sum + n * [.2126,.7152,.0722][i],0);
                const [high, low] = [luminance(bg), luminance(fg)].sort((a,b)=>b-a);
                const minimum = button.textContent.trim() ? 4.5 : 3;
                return (high + .05) / (low + .05) - minimum;
            })
            """);
        Assert.NotEmpty(contrasts);
        Assert.All(contrasts, margin => Assert.True(margin >= 0, $"{theme}: action contrast missed its threshold by {-margin:F2}:1."));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Threads_HistoryControlsStayHiddenAndRailFollowsVisibleReplies(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", r => FulfillJsonAsync(r, EarlierPolishReplies()));
        var page = await OpenDiscussionPolishAsync(context, ConversationWorkspaceState());
        var thread = DiscussionThread(page, "p7", 5);
        await Assertions.Expect(thread.Locator("[data-lmx-thread]")).ToHaveCountAsync(1);
        await Assertions.Expect(thread.Locator("[data-discussion-replies-expand]")).ToBeHiddenAsync();
        await thread.Locator("[data-discussion-replies-page]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-replies-expand]")).ToBeHiddenAsync();
        await AssertThreadRailAsync(thread);
        await thread.Locator("[data-discussion-replies-collapse]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-replies-expand]")).ToBeFocusedAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-replies-collapse]")).ToBeHiddenAsync();
        await AssertThreadRailAsync(thread);
        await thread.Locator("[data-discussion-replies-expand]").PressAsync("Enter");
        await Assertions.Expect(thread.Locator("[data-discussion-replies-expand]")).ToBeHiddenAsync();
        await AssertThreadRailAsync(thread);
    }

    private static async Task AssertThreadRailAsync(ILocator thread)
    {
        await Assertions.Expect(thread.Locator(".lmx-discussion-reply-item:visible").First).ToBeVisibleAsync();
        await thread.Page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var geometry = await thread.EvaluateAsync<double[]>("""
            article => {
                const replies = article.querySelector('[data-lmx-thread]');
                const items = [...replies.querySelectorAll('.lmx-discussion-reply-list > .lmx-discussion-reply-item')].filter(e => e.getClientRects().length);
                const last = items.at(-1), rail = getComputedStyle(replies, '::before'), elbow = getComputedStyle(last, '::before');
                const railEnd = replies.getBoundingClientRect().top + replies.clientTop + parseFloat(rail.top) + parseFloat(rail.height);
                const elbowTop = last.getBoundingClientRect().top + parseFloat(elbow.top);
                return [railEnd - elbowTop, ...items.map(e => {
                    const face = e.querySelector('.lmx-discussion-author-avatar').getBoundingClientRect();
                    return e.getBoundingClientRect().top + parseFloat(e.style.getPropertyValue('--lmx-elbow-y')) - face.top - face.height / 2;
                })];
            }
            """);
        Assert.All(geometry, delta => Assert.InRange(delta, -3, 3));
    }

    [Fact]
    public async Task Threads_AdviceKeepsItsNestedBranchesAndArchiveGeometry()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionPolishAsync(context, AdviceBrowserState(2));
        var advice = page.Locator("#lmxNotes .lmx-advice-replies");
        await Assertions.Expect(advice.Locator(".lmx-advice-archive")).ToHaveCountAsync(1);
        await Assertions.Expect(advice.Locator(".lmx-advice-archive summary")).ToBeVisibleAsync();
        await advice.Locator(".lmx-advice-archive summary").ClickAsync();
        await Assertions.Expect(advice.Locator("[data-discussion-reply-id='old-comment']")).ToBeVisibleAsync();
        Assert.Null(await advice.GetAttributeAsync("data-lmx-thread"));
        Assert.All(await advice.Locator(".lmx-discussion-reply-item").EvaluateAllAsync<string[]>("items => items.map(e => getComputedStyle(e, '::before').content)"), value => Assert.Equal("none", value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Threads_SlowInitialHydrationAndMovedDraftsDoNotReplayArrivals(bool delayed)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("""
            localStorage.setItem('lmxAccessToken', 'browser-token');
            window.threadArrivals = [];
            const animate = Element.prototype.animate;
            Element.prototype.animate = function (...args) {
                if (this.matches('.lmx-note, .lmx-discussion-reply-composer')) window.threadArrivals.push(this.className);
                return animate.apply(this, args);
            };
            """);
        var state = ConversationWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/participant", async route => { await ready.Task; await FulfillJsonAsync(route, state.ToJsonString()); });
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state["public"]!.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync(new() { Time = "2026-07-01T10:00:00Z" });
        await page.GotoAsync("/longevitymaxxing");
        if (delayed) await page.Clock.RunForAsync(1600);
        ready.SetResult();
        var thread = DiscussionThread(page, "p7", 5);
        await Assertions.Expect(thread).ToBeVisibleAsync();
        await page.Clock.RunForAsync(1600);
        Assert.Empty(await page.EvaluateAsync<string[]>("window.threadArrivals"));
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("A draft that must stay still.");
        await page.Clock.RunForAsync(800);
        await thread.EvaluateAsync("""
            article => {
                window.threadArrivals = [];
                const composer = article.querySelector('[data-discussion-draft]');
                const textarea = composer.querySelector('textarea');
                window.threadDraft = textarea;
                textarea.setSelectionRange(2, 7);
                const replacement = article.cloneNode(true);
                replacement.querySelector('[data-discussion-draft]').remove();
                article.replaceWith(replacement);
                replacement.querySelector('.lmx-discussion-reply-slot').append(composer);
            }
            """);
        await page.Clock.RunForAsync(20);
        Assert.Empty(await page.EvaluateAsync<string[]>("window.threadArrivals"));
        Assert.True(await thread.Locator("textarea").EvaluateAsync<bool>("e => e === window.threadDraft && e.selectionStart === 2 && e.selectionEnd === 7"));
        // A genuinely new reply still arrives; changing the motion preference
        // during that arrival must immediately release the animated element.
        await thread.EvaluateAsync("""
            article => {
                const reply = article.querySelector('.lmx-discussion-reply-item').cloneNode(true);
                reply.dataset.discussionReplyId = 'motion-fixture';
                reply.querySelector('.lmx-discussion-permalink').href = '#motion-fixture';
                article.querySelector('.lmx-discussion-reply-list').append(reply);
            }
            """);
        var arriving = thread.Locator("[data-discussion-reply-id='motion-fixture']");
        await Assertions.Expect(arriving).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-arriving"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(arriving).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-arriving"));
    }
}
