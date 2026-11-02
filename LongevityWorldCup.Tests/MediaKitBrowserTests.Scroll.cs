using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class MediaKitBrowserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateFolderHeadings_StickBelowTheSiteHeaderAndRemainReadable(bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        await page.EvaluateAsync("() => document.fonts.ready");
        var group = page.Locator(".media-kit-group").Filter(new() { Has = page.Locator(".media-kit-group-head") }).First;
        await group.EvaluateAsync("e => scrollTo({top:e.getBoundingClientRect().top + scrollY + 100,behavior:'instant'})");
        var head = group.Locator(".media-kit-group-head");
        await Assertions.Expect(head).ToHaveAttributeAsync("data-lwc-stuck", "");
        Assert.True(await head.EvaluateAsync<bool>("e => Math.abs(e.getBoundingClientRect().top - parseFloat(getComputedStyle(e).top)) < 2"));
        Assert.Equal("1", await head.EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
        Assert.DoesNotContain("rgba", await head.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        await page.EvaluateAsync("scrollTo({top:0,behavior:'instant'})");
        await Assertions.Expect(head).Not.ToHaveAttributeAsync("data-lwc-stuck", "");
    }
}
