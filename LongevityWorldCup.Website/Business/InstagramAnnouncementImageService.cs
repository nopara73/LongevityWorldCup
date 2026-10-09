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
            var stream = new MemoryStream();
            await image.SaveAsPngAsync(stream, ct);
            stream.Position = 0;
            return stream;
        }
        finally { _gate.Release(); }
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
            .Select(slug => snapshot.FirstOrDefault(x => AthleteSlug.Normalize(x["AthleteSlug"]?.GetValue<string>()) == AthleteSlug.Normalize(slug)))
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
