using System.Numerics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LongevityWorldCup.Website.Tools;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using DrawingPath = SixLabors.ImageSharp.Drawing.Path;
using IOPath = System.IO.Path;

namespace LongevityWorldCup.Website.Business;

// Instagram gets a portrait composition, not a small landscape text fallback.
public sealed class InstagramAnnouncementImageService(
    IWebHostEnvironment environment, AthleteDataService athletes, IHttpClientFactory http,
    ILogger<InstagramAnnouncementImageService> log)
{
    private const int Width = 1080;
    private const int Height = 1350;
    private static readonly Color Ink = Color.ParseHex("11151b");
    private static readonly Color Teal = Color.ParseHex("2dd4bf");
    private static readonly Color Muted = Color.ParseHex("c9d1da");
    private readonly FontCollection _fonts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FontFamily? _bold;
    private FontFamily? _regular;

    public bool IsConfigured => File.Exists(Asset("fonts", "Poppins-Regular.ttf"))
        && File.Exists(Asset("fonts", "Poppins-Bold.ttf")) && File.Exists(Asset("HdLogo.png"));

    internal async Task<MemoryStream> RenderAsync(InstagramVisual visual, CancellationToken ct = default, string? artworkPath = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (visual.Artwork?.InstagramAssetPath is { } portraitAsset)
            {
                using var portrait = await ImageInput.LoadAsync<Rgba32>(IOPath.Combine(environment.WebRootPath, portraitAsset), ct);
                if (portrait.Width != Width || portrait.Height != Height)
                    throw new InvalidOperationException("Prepared Instagram artwork must be 1080 by 1350 pixels.");
                return await EncodePngAsync(portrait, ct);
            }
            if (visual.Placement is { } placement)
                return await RenderPlacementAsync(placement, visual.Headline, visual.AthleteSlugs[0], ct);
            artworkPath ??= visual.Artwork?.FullPath(environment.WebRootPath);
            _bold ??= _fonts.Add(Asset("fonts", "Poppins-Bold.ttf"));
            _regular ??= _fonts.Add(Asset("fonts", "Poppins-Regular.ttf"));
            using var image = new Image<Rgba32>(Width, Height, Ink);
            image.Mutate(x =>
            {
                x.Fill(Color.ParseHex("19232c"), new RectangularPolygon(0, 0, Width, 140));
                x.Fill(Teal, new RectangularPolygon(80, 164, 104, 6));
            });
            using var logo = await ImageLogo.LoadMarkAsync(Asset("HdLogo.png"), ct);
            logo.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(72, 72), Mode = ResizeMode.Max }));
            image.Mutate(x =>
            {
                x.DrawImage(logo, new Point(80, 34), 1f);
                x.DrawText("LONGEVITY\nWORLD CUP", _bold.Value.CreateFont(25, FontStyle.Bold), Color.White, new PointF(172, 31));
            });

            var y = 240f;
            var hasHero = false;
            if (visual.Headline.Length <= 240)
            {
                if (artworkPath is not null)
                {
                    using var artwork = await ImageInput.LoadAsync<Rgba32>(artworkPath, ct);
                    artwork.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(760, 390), Mode = ResizeMode.Max }));
                    image.Mutate(x => x.DrawImage(artwork, new Point((Width - artwork.Width) / 2, 230), 1f));
                    hasHero = true;
                }
                else if (visual.Platform is not null)
                    hasHero = DrawPlatform(image, visual.Platform);
                else if (visual.VideoId is not null)
                    hasHero = await DrawVideoAsync(image, visual.VideoId, ct);
                else
                    hasHero = await DrawAthletesAsync(image, visual.AthleteSlugs, ct);
            }
            if (hasHero) y = 650f;

            // The headline has priority. Supporting detail can continue in the caption.
            var headline = Fit(visual.Headline, _bold.Value, 82f, 46f, Height - y - 220f, truncate: false);
            if (!hasHero)
            {
                var detailHeight = string.IsNullOrWhiteSpace(visual.Details) ? 0
                    : Fit(visual.Details, _regular.Value, 40, 36, 360, truncate: true).Height + 44;
                y = Math.Max(y, (Height - headline.Height - detailHeight) / 2f);
            }
            image.Mutate(x => x.DrawText(Options(headline.Font, y), headline.Text, Color.White));
            y += headline.Height + 44f;
            if (!string.IsNullOrWhiteSpace(visual.Details) && y < Height - 250)
            {
                var details = Fit(visual.Details, _regular.Value, 40f, 36f, Height - y - 180f, truncate: true);
                image.Mutate(x => x.DrawText(Options(details.Font, y), details.Text, Muted));
            }
            if (!string.IsNullOrWhiteSpace(visual.Address))
            {
                var font = _regular.Value.CreateFont(28);
                var address = ImageTextLayout.EllipsizeToWidth(visual.Address, font, 920);
                image.Mutate(x => x.DrawText(address, font, Teal, new PointF(80, Height - 140)));
            }
            image.Mutate(x => x.DrawText("longevityworldcup.com", _regular.Value.CreateFont(26), Muted, new PointF(80, Height - 70)));
            if (visual.Artwork?.QrAssetPath is { } qrAsset)
            {
                using var qr = await ImageInput.LoadAsync<Rgba32>(IOPath.Combine(environment.WebRootPath, qrAsset), ct);
                qr.Mutate(x => x.Resize(164, 164, KnownResamplers.NearestNeighbor));
                image.Mutate(x => x.DrawImage(qr, new Point(Width - 244, Height - 206), 1f));
            }
            return await EncodePngAsync(image, ct);
        }
        finally { _gate.Release(); }
    }

    private static async Task<MemoryStream> EncodePngAsync(Image<Rgba32> image, CancellationToken ct)
    {
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream, ct);
        stream.Position = 0;
        return stream;
    }

    private async Task<MemoryStream> RenderPlacementAsync(InstagramPlacement placement, string name, string slug, CancellationToken ct)
    {
        const int inset = 64;
        const int photoHeight = 1040;
        var fonts = ShareCardCanvas.GetFonts(environment.WebRootPath);
        var mint = Color.ParseHex("bdf4e2");
        using var image = new Image<Rgba32>(Width, Height, Ink);
        var athlete = FindAthlete(athletes.GetAthletesSnapshot().OfType<JsonObject>(), slug);
        var portrait = ProfilePath(athlete?["ProfilePic"]?.GetValue<string>());
        using var photo = portrait is null ? null : await LoadPlacementPhotoAsync(portrait, ct);
        if (photo is null)
            image.Mutate(x => ShareCardCanvas.DrawMonogramDisc(x, fonts, name, Width / 2f, photoHeight * 0.46f, 248, Teal));
        else
        {
            photo.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Size = new Size(Width, photoHeight), Mode = ResizeMode.Crop }));
            image.Mutate(x => x.DrawImage(photo, Point.Empty, 1f));
        }

        var (lines, nameFont) = ShareCardCanvas.FitHeadline(fonts, name, 112, 80, Width - 2 * inset, 2, 52);
        var rank = FormattableString.Invariant($"#{placement.Place}");
        var rankFont = ImageTextLayout.FitFontToWidth(fonts.Display, rank, 150, 100, 260);
        const int leagueX = 370;
        var leagueFont = ImageTextLayout.FitFontToWidth(fonts.Bold, placement.Leaderboard, 63, 44, Width - leagueX - inset);
        using var mark = await ImageLogo.LoadMarkAsync(Asset("HdLogo.png"), ct);
        mark.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(86, 86), Mode = ResizeMode.Max }));
        image.Mutate(ctx =>
        {
            // Keep a light or busy photograph legible under the wordmark and athlete's name.
            ctx.Fill(new LinearGradientBrush(PointF.Empty, new PointF(0, photoHeight), GradientRepetitionMode.None,
                new ColorStop(0, Ink.WithAlpha(.77f)), new ColorStop(.24f, Ink.WithAlpha(0)),
                new ColorStop(.52f, Ink.WithAlpha(0)), new ColorStop(.8f, Ink.WithAlpha(.65f)),
                new ColorStop(1, Ink.WithAlpha(.98f))), new RectangularPolygon(0, 0, Width, photoHeight));
            ctx.DrawImage(mark, new Point(inset - 8, 48), 1f);
            ShareCardCanvas.DrawTrackedText(ctx, fonts, "LONGEVITY WORLD CUP", fonts.Display.CreateFont(25, FontStyle.Bold),
                Color.White, new PointF(inset + 89, 73), 1.8f);
            var lineHeight = nameFont.Size * 1.1f;
            ShareCardCanvas.DrawLines(ctx, fonts, lines, nameFont, Color.White, inset,
                photoHeight - 48 - lines.Count * lineHeight, lineHeight);

            ctx.Fill(mint, new RectangularPolygon(0, photoHeight, Width, Height - photoHeight));
            var middle = (Height + photoHeight) / 2f;
            ctx.DrawText(rank, rankFont, Ink, new PointF(inset, ShareCardCanvas.CenterTextY(rank, rankFont, middle)));
            ctx.DrawLine(Ink.WithAlpha(.3f), 2, new PointF(leagueX - 28, middle - 66), new PointF(leagueX - 28, middle + 66));
            var titleCenter = placement.Detail is null ? middle : middle - 28;
            ctx.DrawText(placement.Leaderboard, leagueFont, Ink,
                new PointF(leagueX, ShareCardCanvas.CenterTextY(placement.Leaderboard, leagueFont, titleCenter)));
            if (placement.Detail is { } detail)
            {
                var font = fonts.Regular.CreateFont(35);
                ctx.DrawText(detail, font, Ink, new PointF(leagueX, ShareCardCanvas.CenterTextY(detail, font, middle + 38)));
            }
        });
        return await EncodePngAsync(image, ct);
    }

    private async Task<Image<Rgba32>?> LoadPlacementPhotoAsync(string path, CancellationToken ct)
    {
        try { return await ImageInput.LoadAsync<Rgba32>(path, ct); }
        catch (Exception ex) when (ex is IOException or UnknownImageFormatException or InvalidImageContentException)
        {
            log.LogWarning("Instagram placement portrait is unavailable ({ErrorType}).", ex.GetType().Name);
            return null;
        }
    }

    private (Font Font, string Text, float Height) Fit(string text, FontFamily family, float start, float min, float maxHeight, bool truncate)
    {
        // Do not silently clip or shrink the announcement to unreadable type.
        text = WithoutMissingDecorations(text, family.CreateFont(start));
        var rendered = text;
        for (var size = start; size >= min; size -= 2)
        {
            var font = family.CreateFont(size, truncate ? FontStyle.Regular : FontStyle.Bold);
            var height = TextMeasurer.MeasureSize(rendered, Options(font, 0)).Height;
            if (height <= maxHeight) return (font, rendered, height);
        }
        var fallback = family.CreateFont(min, truncate ? FontStyle.Regular : FontStyle.Bold);
        if (!truncate)
            throw new InvalidOperationException("The Instagram headline needs a shorter visual summary.");
        var limit = Math.Min(text.Length, 400);
        do
        {
            rendered = InstagramPost.Truncate(text, limit);
            if (TextMeasurer.MeasureSize(rendered, Options(fallback, 0)).Height <= maxHeight)
                return (fallback, rendered, TextMeasurer.MeasureSize(rendered, Options(fallback, 0)).Height);
            limit -= 10;
        } while (limit > 0);
        return (fallback, "", 0);
    }

    private static string WithoutMissingDecorations(string text, Font font)
    {
        // Keep names and words intact; omit unsupported emoji rather than draw missing-glyph boxes.
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 0xFE0E or 0xFE0F or 0x200D) continue;
            var decoration = Rune.GetUnicodeCategory(rune) is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol;
            if (decoration && (!font.TryGetGlyphs(new CodePoint(rune.Value), out var glyphs)
                || glyphs.All(glyph => glyph.GlyphMetrics.GlyphId == 0))) continue;
            result.Append(rune);
        }
        return result.ToString().Trim();
    }

    private static RichTextOptions Options(Font font, float y) => new(font)
    {
        Origin = new PointF(80, y), WrappingLength = 920, LineSpacing = 1.15f,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
    };

    private bool DrawPlatform(Image<Rgba32> image, string platform)
    {
        // These are local, trusted, path-only brand assets. No runtime SVG fetching.
        var file = Asset("social", "platforms", platform + ".svg");
        if (!File.Exists(file)) throw new InvalidOperationException("The Instagram platform logo is missing.");
        var document = XDocument.Load(file);
        var root = document.Root!;
        var box = root.Attribute("viewBox")!.Value.Split(' ').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var scale = 300 / Math.Max(box[2], box[3]);
        var transform = Matrix3x2.CreateTranslation(-box[0], -box[1]) * Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateTranslation((Width - box[2] * scale) / 2, 255);
        foreach (var element in root.Descendants().Where(x => x.Name.LocalName == "path"))
        {
            // The ImageSharp parser expects separators even where SVG allows compact commands.
            var pathData = string.Join(" ", Regex.Matches(element.Attribute("d")!.Value,
                @"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?").Select(x => x.Value));
            if (!DrawingPath.TryParseSvgPath(pathData, out var path))
                throw new InvalidOperationException("The Instagram platform logo is invalid.");
            image.Mutate(x => x.Fill(Color.White, path.Transform(transform)));
        }
        return true;
    }

    private async Task<bool> DrawAthletesAsync(Image<Rgba32> image, IReadOnlyList<string> slugs, CancellationToken ct)
    {
        var snapshot = athletes.GetAthletesSnapshot().OfType<JsonObject>().ToArray();
        var portraits = slugs.Distinct(StringComparer.OrdinalIgnoreCase).Take(2)
            .Select(slug => FindAthlete(snapshot, slug))
            .Select(x => new { Path = ProfilePath(x?["ProfilePic"]?.GetValue<string>()),
                Name = new[] { x?["DisplayName"]?.GetValue<string>(), x?["Name"]?.GetValue<string>() }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "" })
            .Where(x => x.Path is not null).ToArray();
        for (var index = 0; index < portraits.Length; index++)
        {
            using var photo = await ImageInput.LoadAsync<Rgba32>(portraits[index].Path!, ct);
            photo.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Size = new Size(360, 360), Mode = ResizeMode.Crop }));
            var x = portraits.Length == 1 ? 360 : 160 + index * 400;
            image.Mutate(ctx => ctx.DrawImage(photo, new Point(x, 230), 1f));
            var labelFont = _regular!.Value.CreateFont(24);
            var label = ImageTextLayout.EllipsizeToWidth(portraits[index].Name, labelFont, 360);
            image.Mutate(ctx => ctx.DrawText(label, labelFont, Muted, new PointF(x, 605)));
        }
        return portraits.Length > 0;
    }

    private static JsonObject? FindAthlete(IEnumerable<JsonObject> snapshot, string slug)
    {
        var normalized = AthleteSlug.Normalize(slug);
        return snapshot.FirstOrDefault(x => AthleteSlug.Normalize(x["AthleteSlug"]?.GetValue<string>()) == normalized);
    }

    private string? ProfilePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var prefix = url.StartsWith("/generated/profiles/athletes/", StringComparison.Ordinal)
            ? "/generated/profiles/athletes/"
            : url.StartsWith("/athletes/", StringComparison.Ordinal) ? "/athletes/" : null;
        if (prefix is null) return null;
        var path = IOPath.GetFullPath(IOPath.Combine(environment.WebRootPath, url.Split('?')[0].TrimStart('/')));
        var root = IOPath.GetFullPath(IOPath.Combine(environment.WebRootPath, prefix.Trim('/'))) + IOPath.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    private async Task<bool> DrawVideoAsync(Image<Rgba32> image, string videoId, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await http.CreateClient().GetAsync($"https://i.ytimg.com/vi/{Uri.EscapeDataString(videoId)}/hqdefault.jpg", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var thumbnail = await ImageInput.LoadAsync<Rgba32>(stream, deadline.Token);
            thumbnail.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(760, 390), Mode = ResizeMode.Max }));
            image.Mutate(x => x.DrawImage(thumbnail, new Point((Width - thumbnail.Width) / 2, 230), 1f));
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Instagram video artwork is unavailable ({ErrorType}); retaining the announcement headline.", ex.GetType().Name);
            return false;
        }
    }

    private string Asset(params string[] parts) => IOPath.Combine(new[] { environment.WebRootPath, "assets" }.Concat(parts).ToArray());
}
