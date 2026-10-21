using System.Globalization;
using System.Text;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class ShareCardLayoutTests(TestWebApplicationFactory factory)
{
    [Fact]
    public void CjkFallback_HasABoldFaceInsteadOfTheUpstreamThinDefault()
    {
        var fonts = ShareCardCanvas.GetFonts(factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath);
        var cjk = Assert.Single(fonts.Fallbacks, family => family.Name.Contains("Noto Sans SC", StringComparison.Ordinal));
        Assert.Contains(FontStyle.Bold, cjk.GetAvailableStyles());
    }
    [Theory]
    [InlineData("A\u0301da E\u0301mile", "A\u0301E\u0301")]
    [InlineData("Александр Иванов", "АИ")]
    [InlineData("山田太郎 王小明", "山王")]
    [InlineData("🧬 👩🏽‍🚀", "🧬👩🏽‍🚀")]
    public void Monograms_PreserveWholeTextElements(string name, string initials)
        => Assert.Equal(initials, ShareCardCanvas.Monogram(name));

    [Theory]
    [InlineData("Alexandria Longname de Almeida International Longname")]
    [InlineData("WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW")]
    [InlineData("A\u0301da E\u0301mile A\u0301da E\u0301mile A\u0301da E\u0301mile")]
    [InlineData("Александр Иванов")]
    [InlineData("山田太郎 王小明")]
    [InlineData("🧬 Astronaut 👩🏽‍🚀")]
    [InlineData("आदित्य शर्मा")]
    public async Task AthleteNames_FitTheirColumnWithRealGlyphs(string name)
    {
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var fonts = ShareCardCanvas.GetFonts(environment.WebRootPath);
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.SpaceSeparator || rune.Value == 0xFE0F) continue;
            Assert.True(new[] { fonts.Bold }.Concat(fonts.Fallbacks).Any(family =>
                family.CreateFont(46, family.GetAvailableStyles().First()).TryGetGlyphs(new CodePoint(rune.Value), out _)), $"No bundled glyph for {rune} U+{rune.Value:X}.");
        }

        var (lines, font) = ShareCardCanvas.FitHeadline(fonts, name, 70, 54, 684, 2, 46);
        Assert.InRange(lines.Count, 1, 2);
        foreach (var line in lines)
            Assert.InRange(TextMeasurer.MeasureBounds(line, fonts.Text(font)).Width, 1, 685);

        var images = factory.Services.GetRequiredService<AthleteOgImageService>();
        Assert.True(images.TryGetCurrentPayload("michael-lustgarten", out var original));
        var payload = original with { Name = name, ProfilePicUrl = null, Signature = "unicode-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..10] };
        using var image = await Image.LoadAsync<Rgba32>((await images.EnsureRenderedImageAsync(payload))!);
        var ink = new List<(int X, int Y)>();
        for (var y = 110; y < 350; y++) for (var x = 440; x < image.Width; x++)
        {
            var p = image[x, y];
            if (p.R > 220 && p.G > 220 && p.B > 220) ink.Add((x, y));
        }
        Assert.NotEmpty(ink);
        Assert.InRange(ink.Min(p => p.X), 451, 1136);
        Assert.InRange(ink.Max(p => p.X), 452, 1137);
        var captures = Environment.GetEnvironmentVariable("LWC_SHARE_CARD_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(captures))
            await image.SaveAsPngAsync(Path.Combine(captures, "stress-" + payload.Signature + ".png"));
    }

    [Fact]
    public void Ellipsizing_NeverSplitsACombiningOrEmojiSequence()
    {
        var fonts = ShareCardCanvas.GetFonts(factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath);
        var font = fonts.Bold.CreateFont(42);
        const string text = "A\u0301👩🏽‍🚀A\u0301👩🏽‍🚀A\u0301👩🏽‍🚀A\u0301👩🏽‍🚀";
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        for (var width = 70; width < 320; width += 11)
        {
            var result = ImageTextLayout.EllipsizeToWidth(text, font, width, fonts.Fallbacks);
            if (result == text) continue;
            Assert.EndsWith("...", result);
            var prefix = result[..^3];
            Assert.StartsWith(prefix, text);
            Assert.Contains(prefix.Length, boundaries);
            Assert.True(TextMeasurer.MeasureSize(result, fonts.Text(font)).Width <= width + 1);
        }
    }

    [Theory]
    [InlineData("Pheno Age best improvement")]
    [InlineData("Bortz Age best improvement")]
    [InlineData("Chronological Age leaderboard")]
    public async Task ContextCaptions_ClearTheWordmark(string caption)
    {
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var fonts = ShareCardCanvas.GetFonts(environment.WebRootPath);
        using var image = new Image<Rgba32>(1200, 630);
        ShareCardCanvas.DrawBackground(image, Color.ParseHex("#FF4081"));
        await ShareCardCanvas.DrawMastheadAsync(image, Path.Combine(environment.WebRootPath, "assets", "HdLogo.png"), fonts, caption, Color.ParseHex("#FF4081"), NullLogger.Instance, default);
        var whiteRight = 0;
        var pinkLeft = 1200;
        for (var y = 20; y < 80; y++) for (var x = 125; x < 1200; x++)
        {
            var p = image[x, y];
            if (p.R > 220 && p.G > 220 && p.B > 220) whiteRight = Math.Max(whiteRight, x);
            if (p.R > 220 && p.G < 130 && p.B > 90) pinkLeft = Math.Min(pinkLeft, x);
        }
        Assert.True(whiteRight > 350 && pinkLeft < 1100);
        Assert.True(pinkLeft - whiteRight >= 28, $"Wordmark ends at {whiteRight}, context begins at {pinkLeft}.");
    }

    [Fact]
    public async Task LeagueSignature_IncludesDisplayNames()
    {
        await using var isolated = new TestWebApplicationFactory();
        var data = isolated.Services.GetRequiredService<AthleteDataService>();
        var images = isolated.Services.GetRequiredService<LeagueOgImageService>();
        Assert.True(images.TryGetCurrentPayload("ultimate", out var before));
        data.UpdateAthletesJsonInPlace(athlete => athlete["DisplayName"] = "Fixture name revision");
        Assert.True(images.TryGetCurrentPayload("ultimate", out var after));
        Assert.Equal(before.Top3Slugs, after.Top3Slugs);
        Assert.NotEqual(before.Top3Names, after.Top3Names);
        Assert.NotEqual(before.Signature, after.Signature);
    }

    [Fact]
    public void AthleteSignature_ExpiresWhenAMissingPortraitArrives()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LwcSharePortrait-" + Guid.NewGuid().ToString("N"));
        try
        {
            var images = new AthleteOgImageService(new ImageEnvironment(directory), factory.Services.GetRequiredService<AthleteDataService>(), NullLogger<AthleteOgImageService>.Instance);
            Assert.True(images.TryGetCurrentPayload("michael-lustgarten", out var before));
            Assert.False(string.IsNullOrEmpty(before.ProfilePicUrl));
            var imagePath = Path.Combine(directory, before.ProfilePicUrl.Split('?')[0].TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            using (var image = new Image<Rgba32>(20, 20)) image.SaveAsPng(imagePath);
            Assert.True(images.TryGetCurrentPayload("michael-lustgarten", out var after));
            Assert.Equal(before.MetricValue, after.MetricValue);
            Assert.Equal(before.Rank, after.Rank);
            Assert.NotEqual(before.Signature, after.Signature);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(directory));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class ImageEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ShareCardTests";
        public string EnvironmentName { get; set; } = "Testing";
    }
}
