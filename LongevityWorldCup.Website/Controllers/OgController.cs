using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("og/athlete")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class OgController(AthleteOgImageService ogImages) : ControllerBase
{
    private readonly AthleteOgImageService _ogImages = ogImages;

    // The sheet shares the canonical profile, regardless of the page or clock
    // from which it was opened. Resolve that profile's current card on demand.
    [HttpGet("{slug}/preview")]
    public IActionResult GetAthleteSharePreview(string slug)
    {
        Response.Headers.CacheControl = "no-store";
        if (!_ogImages.IsConfigured || !_ogImages.TryGetCurrentPayload(slug, out var payload))
            return NotFound();

        return Ok(new { imageUrl = _ogImages.BuildVersionedImageUrl("", payload) });
    }

    [HttpGet("{slug}.png")]
    public async Task<IActionResult> GetAthleteOgImage(string slug, [FromQuery] string? ctx, CancellationToken ct)
    {
        if (!_ogImages.IsConfigured)
            return NotFound();

        if (!_ogImages.TryGetCurrentPayload(slug, ctx, out var payload))
            return NotFound();

        var path = await _ogImages.EnsureRenderedImageAsync(payload, ct);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = Request.Query["v"] == payload.Signature
            ? "public, max-age=31536000, immutable"
            : "public, max-age=0, must-revalidate";
        return PhysicalFile(path, "image/png");
    }
}
