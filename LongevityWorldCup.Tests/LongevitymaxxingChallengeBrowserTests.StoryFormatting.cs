using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(1280)]
    public async Task AdviceStory_LongMultilineDraftPublishesReloadsAndGrowsDuringEditing(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        var state = AdvicePromptBrowserState("case-22", 22);
        var story = "First line.\nSecond line.\n\n  Indented paragraph.\n" +
            string.Concat(Enumerable.Repeat("More context about my day. ", 500)) + "\nhttps://example.com/story\n<b>literal text</b>";
        string? published = null;
        string? edited = null;
        void SetStory(string value)
        {
            foreach (var collection in new[] { state["notes"]!, state["public"]!["notes"]! })
                collection[0]!["advice"]!["story"] = value;
        }
        await context.RouteAsync("**/api/longevitymaxxing/advice/publish", async route =>
        {
            published = JsonNode.Parse(route.Request.PostData!)!["story"]!.GetValue<string>();
            state = AdviceBrowserState(0);
            SetStory(published);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.RouteAsync("**/api/longevitymaxxing/advice/edit", async route =>
        {
            edited = JsonNode.Parse(route.Request.PostData!)!["story"]!.GetValue<string>();
            SetStory(edited);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var dialog = page.Locator("#lmxAdviceStoryDialog");
        var textarea = dialog.Locator("textarea");
        await textarea.FillAsync(story);
        await textarea.PressAsync("End");
        await textarea.PressAsync("Enter");
        await textarea.PressSequentiallyAsync("Last line.");
        story += "\nLast line.";
        await Assertions.Expect(textarea).ToHaveValueAsync(story);
        await Assertions.Expect(dialog.Locator("#lmxAdviceStoryCount")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog.Locator("button[type=submit]")).ToBeEnabledAsync();
        await CaptureAdviceAsync(page, $"unlimited-story-{width}.png");
        await page.ReloadAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync(story);
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        Assert.Equal(story, published);
        var body = page.Locator("#lmxNotes .lmx-advice-story > p");
        await Assertions.Expect(body).ToHaveCSSAsync("white-space", "pre-wrap");
        Assert.Equal(story, await body.InnerTextAsync());
        await Assertions.Expect(body.Locator("b")).ToHaveCountAsync(0);
        await Assertions.Expect(body.Locator("a[href='https://example.com/story']")).ToHaveCountAsync(1);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        await page.Locator("#lmxNotes [data-advice-edit]").ClickAsync();
        var editor = page.Locator("#lmxNotes .lmx-advice-story-editor");
        Assert.Null(await editor.Locator("textarea").GetAttributeAsync("maxlength"));
        var longerStory = story + "\n\n" + new string('x', 5000);
        await editor.Locator("textarea").FillAsync(longerStory);
        await editor.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(editor).ToHaveCountAsync(0);
        Assert.Equal(longerStory, edited);
        await page.ReloadAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await Assertions.Expect(body).ToBeVisibleAsync();
        Assert.Equal(longerStory, await body.InnerTextAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(320, 100)]
    [InlineData(1280, 100)]
    [InlineData(640, 200)]
    public async Task AdviceFormatting_PreservesParagraphsAndKeepsCounterBelowTheScrolledInput(int width, int textSize)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        if (textSize != 100)
            await context.AddInitScriptAsync($"document.addEventListener('DOMContentLoaded',()=>document.documentElement.style.fontSize='{textSize}%')");
        var state = AdviceBrowserState(1, advisor: true);
        const string lines = "First line.\nSecond line.\n\n  Indented paragraph.";
        foreach (var collection in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            collection[0]!["note"] = lines;
            collection[0]!["advice"]!["story"] = lines;
            collection[0]!["advice"]!["replies"]![0]!["body"] = lines;
            collection[0]!["replies"]![0]!["body"] = lines;
        }
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await post.Locator(".lmx-advice-context > summary").ClickAsync();
        foreach (var selector in new[] { ".lmx-advice-story > p", ".lmx-advice-context > p", ".is-active > .lmx-discussion-reply-item [data-discussion-reply-body]" })
        {
            var body = post.Locator(selector);
            await Assertions.Expect(body).ToHaveCSSAsync("white-space", "pre-wrap");
            Assert.Equal(lines, await body.InnerTextAsync());
        }
        await CaptureAdviceAsync(page, $"formatted-story-{width}-{textSize}.png", post);
        await post.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        var composer = post.Locator("[data-discussion-draft]");
        await composer.Locator("textarea").FillAsync(string.Join('\n', Enumerable.Repeat("A line of advice.", 8)));
        await composer.Locator("textarea").EvaluateAsync("e => e.scrollTop = e.scrollHeight");
        await Assertions.Expect(composer.Locator("[data-reply-count]")).ToBeVisibleAsync();
        var inputBounds = (await composer.Locator("textarea").BoundingBoxAsync())!;
        var countBounds = (await composer.Locator("[data-reply-count]").BoundingBoxAsync())!;
        Assert.True(countBounds.Y >= inputBounds.Y + inputBounds.Height, "The counter must remain outside the editable text area.");
        await CaptureAdviceAsync(page, $"advice-counter-{width}-{textSize}.png", composer);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }
}
