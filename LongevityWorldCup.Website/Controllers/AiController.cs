using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[Controller]
[Route("ai")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class AiController(LeaderboardFactsService facts) : Controller
{
    [HttpGet("leaderboard.md")]
    public IActionResult GetLeaderboardFacts()
    {
        var document = facts.GetLeaderboardMarkdown();
        var eTag = PublicGetCacheHeaders.BuildWeakContentETag(document.Markdown);

        PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AiFactsCacheControl, PublicGetCacheHeaders.AiFactsMaxAgeSeconds, eTag, document.LastModifiedUtc);
        Response.Headers["X-Robots-Tag"] = "index, follow";
        if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
            return StatusCode(StatusCodes.Status304NotModified);

        return Content(document.Markdown, "text/markdown; charset=utf-8");
    }

    [HttpGet("athlete-names.md")]
    public IActionResult GetAthleteNames()
    {
        var document = facts.GetAthleteNamesMarkdown();
        var eTag = PublicGetCacheHeaders.BuildWeakContentETag(document.Markdown);

        PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AiFactsCacheControl, PublicGetCacheHeaders.AiFactsMaxAgeSeconds, eTag, document.LastModifiedUtc);
        Response.Headers["X-Robots-Tag"] = "index, follow";
        if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
            return StatusCode(StatusCodes.Status304NotModified);

        return Content(document.Markdown, "text/markdown; charset=utf-8");
    }

    [HttpGet("athletes.md")]
    public IActionResult RedirectAthletes()
    {
        return RedirectPermanent("/ai/leaderboard.md");
    }
}
