using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class MediaKitBrowserTests
{
    [Fact]
    public async Task DownloadFeedback_ConfirmsOnlySuccessfulExtraction_AndClearsAfterRetry()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(context, failDownloadOnce: true);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Download README.txt", Exact = true });
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("data-download-failed", "");
        Assert.Null(await button.GetAttributeAsync("data-downloaded"));
        await Assertions.Expect(page.Locator("#downloadStatus")).ToContainTextAsync("Download failed");
        await page.Clock.InstallAsync();
        await page.Clock.PauseAtAsync(DateTime.UtcNow.AddHours(1));
        var download = await page.RunAndWaitForDownloadAsync(() => button.EvaluateAsync("e => e.click()"));
        Assert.Null(await download.FailureAsync());
        Assert.Equal(Convert.FromBase64String(KitFiles.Value["MediaKit/README.txt"]),
            await File.ReadAllBytesAsync((await download.PathAsync())!));
        await Assertions.Expect(button).ToHaveAttributeAsync("data-downloaded", "");
        Assert.Null(await button.GetAttributeAsync("data-download-failed"));
        await Assertions.Expect(page.Locator("#downloadStatus")).ToBeEmptyAsync();
        await page.Clock.RunForAsync(1601);
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("data-downloaded", "", new() { Timeout = 4000 });
    }
}
