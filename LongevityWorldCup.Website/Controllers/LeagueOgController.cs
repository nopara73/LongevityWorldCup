using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("og/league")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
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

        return await RenderAsync(payload, ct);
    }

    [HttpGet("/og/leaderboard.png")]
    public async Task<IActionResult> GetLeaderboardOgImage(CancellationToken ct)
    {
        var path = Request.Query["path"].FirstOrDefault() ?? "/leaderboard";
        if (!_leagueOgImages.IsConfigured) return NotFound();
        if (!_leagueOgImages.TryGetLeaderboardPayload(path, Request.Query, out var payload))
            return path is "/" or "/leaderboard" ? Redirect("/assets/og-image.png") : NotFound();
        return await RenderAsync(payload, ct);
    }

    private async Task<IActionResult> RenderAsync(LeagueOgImageService.LeagueOgPayload payload, CancellationToken ct)
    {

        var path = await _leagueOgImages.EnsureRenderedImageAsync(payload, ct);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = Request.Query["v"] == payload.Signature
            ? "public, max-age=31536000, immutable"
            : "public, max-age=0, must-revalidate";
        return PhysicalFile(path, "image/png");
    }
}
