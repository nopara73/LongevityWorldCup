using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/custom-event-preview")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class CustomEventPreviewController(CustomEventImageService images, AthleteDataService athletes) : ControllerBase
{
    private readonly CustomEventImageService _images = images;
    private readonly AthleteDataService _athletes = athletes;

    public sealed record ImagePreviewRequest(string? Title, string? Content, string? Platform);

    [HttpPost("image")]
    public async Task<IActionResult> RenderImagePreview([FromBody] ImagePreviewRequest request, CancellationToken ct)
    {
        var title = request.Title?.Trim() ?? "";
        var content = request.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
            return BadRequest("Title or content is required.");

        if (!_images.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Custom event image rendering is not configured.");

        var rawText = string.IsNullOrWhiteSpace(content)
            ? title
            : $"{title}\n\n{content}";
        var platform = ParsePlatform(request.Platform);
        var stream = await _images.RenderToStreamAsync(rawText, slug => ResolveMention(slug, platform), ct);
        if (stream is null)
            return StatusCode(StatusCodes.Status500InternalServerError, "Custom event image preview could not be rendered.");

        Response.Headers[HeaderNames.CacheControl] = "no-store,max-age=0";
        return File(stream, "image/png");
    }

    private static SocialPlatform ParsePlatform(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "threads" => SocialPlatform.Threads,
            "facebook" => SocialPlatform.Facebook,
            _ => SocialPlatform.X
        };
    }

    private string ResolveMention(string slug, SocialPlatform platform)
    {
        var normalized = slug?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        var athlete = _athletes.GetAthletesSnapshot()
            .OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(GetString(x, "AthleteSlug"), normalized, StringComparison.OrdinalIgnoreCase));
        if (athlete is not null)
        {
            var name = FirstNonEmpty(GetString(athlete, "DisplayName"), GetString(athlete, "Name"));
            if (platform is SocialPlatform.X or SocialPlatform.Threads)
            {
                var mention = SocialContactParser.TryBuildMention(GetString(athlete, "MediaContact"), platform);
                if (!string.IsNullOrWhiteSpace(mention))
                    return mention;
            }

            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return HumanizeSlug(normalized);
    }

    private static string? GetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string HumanizeSlug(string value)
    {
        var spaced = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return string.IsNullOrWhiteSpace(spaced)
            ? value
            : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }
}
