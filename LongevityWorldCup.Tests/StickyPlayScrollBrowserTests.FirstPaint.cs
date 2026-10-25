using System.Collections.Concurrent;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class StickyPlayScrollBrowserTests
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task FirstNativeWheel_PaintsPlayInTheFirstScrolledFrame(int width, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about", new() { WaitUntil = WaitUntilState.Load });
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame); }");
        var cdp = await context.NewCDPSessionAsync(page);
        var paints = new ConcurrentQueue<(double Y, byte[] Pixels)>();
        var initialPaint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scrolledPaint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cdp.Event("Page.screencastFrame").OnEvent += (_, value) =>
        {
            var frame = value!.Value;
            var y = frame.GetProperty("metadata").GetProperty("scrollOffsetY").GetDouble();
            paints.Enqueue((y, Convert.FromBase64String(frame.GetProperty("data").GetString()!)));
            _ = cdp.SendAsync("Page.screencastFrameAck", new() { ["sessionId"] = frame.GetProperty("sessionId").GetInt32() });
            initialPaint.TrySetResult();
            if (y >= 79) scrolledPaint.TrySetResult();
        };
        byte[] settledPixels;
        try
        {
            await cdp.SendAsync("Page.startScreencast", new() { ["format"] = "png", ["everyNthFrame"] = 1 });
            await initialPaint.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // This is the first input. DOM reads after a scroll event miss the
            // compositor frame that can scroll the banner away ahead of script.
            await page.Mouse.WheelAsync(0, 80);
            await scrolledPaint.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await page.WaitForFunctionAsync("() => scrollY >= 79 && document.querySelector('#site-sticky-header').classList.contains('visible')");
            settledPixels = await page.ScreenshotAsync();
        }
        finally
        {
            await cdp.SendAsync("Page.stopScreencast");
            await cdp.DetachAsync();
        }
        var play = (await page.Locator("#site-sticky-header .scrolled-button").BoundingBoxAsync())!;
        int GreenPixels(byte[] bytes)
        {
            using var image = Image.Load<Rgba32>(bytes);
            var count = 0;
            for (var y = (int)play.Y + 4; y < Math.Min(image.Height, play.Y + play.Height - 4); y++)
            for (var x = (int)play.X + 4; x < Math.Min(image.Width, play.X + play.Width - 4); x++)
            {
                var pixel = image[x, y];
                if (pixel.G > 85 && pixel.G > pixel.R * 1.35 && pixel.G > pixel.B * 1.2) count++;
            }
            return count;
        }
        var reference = GreenPixels(settledPixels);
        Assert.True(reference > 500, "The settled PLAY reference must contain its approved green action surface.");
        var scrolled = paints.Where(frame => frame.Y >= 79).ToArray();
        Assert.NotEmpty(scrolled);
        Assert.All(scrolled, frame => Assert.True(GreenPixels(frame.Pixels) >= reference * .95,
            $"PLAY disappeared in a native scroll paint at y={frame.Y}; reference contains {reference} green pixels."));
        Assert.True((await page.EvaluateAsync<ScrollState>(Snapshot)).AnyPlay);
    }
}
