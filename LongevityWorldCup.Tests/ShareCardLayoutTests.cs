using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
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
    public async Task LeagueRender_KeepsThePortraitsCapturedWithItsSignature()
    {
        using var workspace = CreateLeagueWorkspace();
        await using var isolated = new TestWebApplicationFactory(builder => builder.UseWebRoot(workspace.Root));
        var data = isolated.Services.GetRequiredService<AthleteDataService>();
        var images = isolated.Services.GetRequiredService<LeagueOgImageService>();
        Assert.True(images.TryGetCurrentPayload("ultimate", out var payload));
        Assert.Equal(3, payload.Top3Slugs.Count);
        Assert.All(payload.ProfilePaths!, path => Assert.NotNull(path));
        var control = payload with { Signature = "snapshot-before-" + payload.Signature };
        var before = await File.ReadAllBytesAsync((await images.EnsureRenderedImageAsync(control))!);
        await WaitForLeagueChangeAsync(data, images, workspace.ChangePortraits,
            current => current.ProfilePaths!.Zip(payload.ProfilePaths!).All(pair => pair.First != pair.Second));
        var queued = payload with { Signature = "snapshot-after-" + payload.Signature };
        var after = await File.ReadAllBytesAsync((await images.EnsureRenderedImageAsync(queued))!);
        Assert.Equal(before, after);
        Assert.True(images.TryGetCurrentPayload("ultimate", out var current));
        Assert.NotEqual(payload.Signature, current.Signature);
    }

    [Fact]
    public async Task LeagueSignature_IncludesDisplayNames()
    {
        using var workspace = CreateLeagueWorkspace();
        await using var isolated = new TestWebApplicationFactory(builder => builder.UseWebRoot(workspace.Root));
        var data = isolated.Services.GetRequiredService<AthleteDataService>();
        var images = isolated.Services.GetRequiredService<LeagueOgImageService>();
        Assert.True(images.TryGetCurrentPayload("ultimate", out var before));
        var after = await WaitForLeagueChangeAsync(data, images, workspace.ChangeDisplayNames,
            current => current.Top3Names.Count == 3 && current.Top3Names.All(name => name.StartsWith("Revised fixture ", StringComparison.Ordinal)));
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

    private LeagueWorkspace CreateLeagueWorkspace()
    {
        var source = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var slugs = factory.Services.GetRequiredService<AthleteDataService>().GetTop3SlugsForLeague("ultimate");
        return new LeagueWorkspace(source, slugs);
    }

    private static async Task<LeagueOgImageService.LeagueOgPayload> WaitForLeagueChangeAsync(
        AthleteDataService data, LeagueOgImageService images, Action change,
        Func<LeagueOgImageService.LeagueOgPayload, bool> changed)
    {
        var completion = new TaskCompletionSource<LeagueOgImageService.LeagueOgPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe()
        {
            if (images.TryGetCurrentPayload("ultimate", out var current) && changed(current))
                completion.TrySetResult(current);
        }
        data.AthletesChanged += Observe;
        try
        {
            change();
            Observe();
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { data.AthletesChanged -= Observe; }
    }

    private sealed class LeagueWorkspace : IDisposable
    {
        private readonly List<string> _portraitPaths = [];
        private readonly List<string> _athletePaths = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "LwcLeagueSnapshot-" + Guid.NewGuid().ToString("N"));

        public LeagueWorkspace(string source, IReadOnlyList<string> slugs)
        {
            Assert.Equal(3, slugs.Count);
            var fonts = Path.Combine(source, "assets", "fonts");
            foreach (var file in Directory.EnumerateFiles(fonts, "*", SearchOption.AllDirectories)
                .Append(Path.Combine(source, "assets", "HdLogo.png")))
            {
                var target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            for (var index = 0; index < slugs.Count; index++)
            {
                var slug = "fixture-athlete-" + index;
                var directory = Path.Combine(Root, "athletes", slug);
                Directory.CreateDirectory(directory);
                var athlete = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "athletes", slugs[index], "athlete.json")))!;
                athlete["Name"] = $"Fixture athlete {index}";
                athlete["DisplayName"] = $"Fixture athlete {index}";
                var jsonPath = Path.Combine(directory, "athlete.json");
                File.WriteAllText(jsonPath, athlete.ToJsonString());
                _athletePaths.Add(jsonPath);
                _portraitPaths.Add(Path.Combine(directory, slug + ".png"));
            }
            WritePortraits(false);
        }

        public void ChangePortraits() => WritePortraits(true);

        private void WritePortraits(bool changed)
        {
            for (var index = 0; index < _portraitPaths.Count; index++)
            {
                var color = changed ? new Rgba32(220, (byte)(50 + index * 60), 30) : new Rgba32(30, (byte)(50 + index * 60), 190);
                using var image = new Image<Rgba32>(32, 32, color);
                var temporary = _portraitPaths[index] + ".next";
                image.SaveAsPng(temporary);
                File.Move(temporary, _portraitPaths[index], overwrite: true);
            }
        }

        public void ChangeDisplayNames()
        {
            for (var index = 0; index < _athletePaths.Count; index++)
            {
                var athlete = JsonNode.Parse(File.ReadAllText(_athletePaths[index]))!;
                athlete["DisplayName"] = $"Revised fixture {index}";
                var temporary = _athletePaths[index] + ".next";
                File.WriteAllText(temporary, athlete.ToJsonString());
                File.Move(temporary, _athletePaths[index], overwrite: true);
            }
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(Root));
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
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
