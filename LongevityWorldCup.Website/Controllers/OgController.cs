using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("og/athlete")]
public sealed class OgController(AthleteOgImageService ogImages) : ControllerBase
{
    private readonly AthleteOgImageService _ogImages = ogImages;

    [HttpGet("{slug}.png")]
    public async Task<IActionResult> GetAthleteOgImage(string slug, CancellationToken ct)
    {
        if (!_ogImages.IsConfigured)
            return NotFound();

        if (!_ogImages.TryGetCurrentPayload(slug, out var payload))
            return NotFound();

        var path = await _ogImages.EnsureRenderedImageAsync(payload, ct);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(path, "image/png");
    }
}
