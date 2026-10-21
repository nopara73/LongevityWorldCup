using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

// Athlete, league and page link previews are one card family: the same masthead,
// the same closing accent bar, and no empty portrait slot when an athlete has no photo.
[Collection(HttpTestCollections.ReadOnly)]
public sealed class ShareCardFamilyTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task AthleteCardWithoutAPhoto_ShowsTheirInitialsInARingedDisc()
    {
        var images = sharedFactory.Services.GetRequiredService<AthleteOgImageService>();
        Assert.True(images.TryGetCurrentPayload("ron-lugbill", out var payload));
        var photoless = payload with { ProfilePicUrl = null, Signature = "nophoto" + payload.Signature[..5] };

        using var image = await LoadAsync(await images.EnsureRenderedImageAsync(photoless), "athlete-no-photo");

        // The portrait slot sits in the left third of the body, centred vertically.
        var ring = 0;
        var initials = 0;
        for (var y = 200; y < 520; y += 2)
        {
            for (var x = 60; x < 400; x += 2)
            {
                var pixel = image[x, y];
                if (pixel.R > 200 && pixel.G < 120 && pixel.B > 80)
                    ring++;
                if (pixel.R > 200 && pixel.G > 200 && pixel.B > 200)
                    initials++;
            }
        }

        Assert.True(ring > 400, $"Expected an accent ring around the portrait slot, found {ring} accent pixels.");
        Assert.True(initials > 300, $"Expected the athlete's initials inside the slot, found {initials} light pixels.");
    }

    [Fact]
    public async Task AthleteLeagueAndPageCards_OpenWithTheSameMasthead()
    {
        var bounds = new List<Rectangle>();
        foreach (var (name, image) in await RenderFamilyAsync())
        {
            using (image)
            {
                var logo = BrightBounds(image, 0, 0, 120, 96);
                var wordmark = BrightBounds(image, 120, 0, 520, 96);
                Assert.True(logo.Width > 20, $"{name}: no logo mark in the masthead.");
                Assert.True(wordmark.Width > 250, $"{name}: the wordmark must run on one line beside the logo.");
                Assert.True(wordmark.Height < 30, $"{name}: the wordmark must be a single line, not stacked.");
                bounds.Add(Rectangle.Union(logo, wordmark));
            }
        }

        foreach (var other in bounds.Skip(1))
        {
            Assert.InRange(Math.Abs(other.X - bounds[0].X), 0, 2);
            Assert.InRange(Math.Abs(other.Y - bounds[0].Y), 0, 2);
            Assert.InRange(Math.Abs(other.Width - bounds[0].Width), 0, 3);
            Assert.InRange(Math.Abs(other.Height - bounds[0].Height), 0, 3);
        }
    }

    [Fact]
    public async Task Cards_CloseWithTheirAccentBar()
    {
        foreach (var (name, image) in await RenderFamilyAsync())
        {
            using (image)
            {
                var left = image[100, image.Height - 4];
                var right = image[image.Width - 100, image.Height - 4];
                Assert.True(right.G > 180 && right.R < 160, $"{name}: the bar must end in the reduction green, got {right}.");
                if (name == "athlete")
                    Assert.True(left.R > 200 && left.G < 120, $"{name}: the bar must open in the rank pink, got {left}.");
                else
                    Assert.True(left.G > 180 && left.R < 160, $"{name}: the bar must run in the card's accent, got {left}.");

                var aboveBar = image[image.Width / 2, image.Height - 16];
                Assert.True(aboveBar.R < 90 && aboveBar.G < 90 && aboveBar.B < 90, $"{name}: the bar must be a crisp band, got {aboveBar} above it.");
            }
        }
    }

    [Fact]
    public async Task AthleteCard_SetsRankAndMetricAsDisplayNumerals()
    {
        var images = sharedFactory.Services.GetRequiredService<AthleteOgImageService>();
        Assert.True(images.TryGetCurrentPayload("andressa-lohana-de-almeida", out var payload));
        using var image = await LoadAsync(await images.EnsureRenderedImageAsync(payload), "athlete-long-name");

        // Rank pink and reduction green each fill a tall numeral, not a single text line.
        var pink = BoundsWhere(image, 490, 120, 1150, 600, p => p.R > 220 && p.G < 110 && p.B > 90);
        var green = BoundsWhere(image, 490, 120, 1150, 600, p => p.G > 190 && p.R < 150 && p.B < 110);
        Assert.True(pink.Height >= 44, $"Rank numerals are only {pink.Height}px tall.");
        Assert.True(green.Height >= 44, $"Metric numerals are only {green.Height}px tall.");
        Assert.InRange(Math.Abs(pink.Bottom - green.Bottom), 0, 6);
        Assert.True(green.Left - pink.Left > 250, "Rank and metric sit side by side as two tiles.");
    }

    private async Task<IReadOnlyList<(string Name, Image<Rgba32> Image)>> RenderFamilyAsync()
    {
        var athletes = sharedFactory.Services.GetRequiredService<AthleteOgImageService>();
        var leagues = sharedFactory.Services.GetRequiredService<LeagueOgImageService>();
        var pages = sharedFactory.Services.GetRequiredService<PageOgImageService>();
        Assert.True(athletes.TryGetCurrentPayload("ron-lugbill", out var athlete));
        Assert.True(leagues.TryGetCurrentPayload("ultimate", out var league));
        Assert.True(pages.TryGetCurrentPayload("home", out var page));

        return
        [
            ("athlete", await LoadAsync(await athletes.EnsureRenderedImageAsync(athlete), "athlete")),
            ("league", await LoadAsync(await leagues.EnsureRenderedImageAsync(league), "league")),
            ("page", await LoadAsync(await pages.EnsureRenderedImageAsync(page), "page"))
        ];
    }

    private static async Task<Image<Rgba32>> LoadAsync(string? path, string captureName)
    {
        Assert.False(string.IsNullOrWhiteSpace(path));
        var image = await Image.LoadAsync<Rgba32>(path!);
        Assert.Equal(1200, image.Width);
        Assert.Equal(630, image.Height);

        var captures = Environment.GetEnvironmentVariable("LWC_SHARE_CARD_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            File.Copy(path!, Path.Combine(captures, captureName + ".png"), overwrite: true);
        }

        return image;
    }

    private static Rectangle BrightBounds(Image<Rgba32> image, int x0, int y0, int x1, int y1)
        => BoundsWhere(image, x0, y0, x1, y1, p => p.R > 200 && p.G > 200 && p.B > 200);

    private static Rectangle BoundsWhere(Image<Rgba32> image, int x0, int y0, int x1, int y1, Func<Rgba32, bool> match)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (!match(image[x, y]))
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
