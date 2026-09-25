using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/previews/youtube")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
[EnableRateLimiting(YouTubePreviewService.RateLimitPolicy)]
public sealed class YouTubePreviewController(YouTubePreviewService previews) : ControllerBase
{
    [HttpGet("{videoId}")]
    public async Task<IActionResult> Get(string videoId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!YouTubePreviewService.IsVideoId(videoId)) return BadRequest();
        var preview = await previews.FetchAsync(videoId, ct);
        if (preview is null)
        {
            Response.Headers.RetryAfter = "30";
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(preview);
    }
}
