using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("og/page")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class PageOgController(PageOgImageService pageOgImages) : ControllerBase
{
    private readonly PageOgImageService _pageOgImages = pageOgImages;

    [HttpGet("{slug}.png")]
    public async Task<IActionResult> GetPageOgImage(string slug, CancellationToken ct)
    {
        if (!_pageOgImages.IsConfigured)
            return NotFound();

        if (!_pageOgImages.TryGetCurrentPayload(slug, out var payload))
            return NotFound();

        var path = await _pageOgImages.EnsureRenderedImageAsync(payload, ct);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(path, "image/png");
    }
}
