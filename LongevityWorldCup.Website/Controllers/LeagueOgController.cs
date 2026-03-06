using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("og/league")]
public sealed class LeagueOgController(LeagueOgImageService leagueOgImages) : ControllerBase
{
    private readonly LeagueOgImageService _leagueOgImages = leagueOgImages;

    [HttpGet("{slug}.png")]
    public async Task<IActionResult> GetLeagueOgImage(string slug, CancellationToken ct)
    {
        if (!_leagueOgImages.IsConfigured)
            return NotFound();

        if (!_leagueOgImages.TryGetCurrentPayload(slug, out var payload))
            return NotFound();

        var path = await _leagueOgImages.EnsureRenderedImageAsync(payload, ct);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(path, "image/png");
    }
}
