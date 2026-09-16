using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace LongevityWorldCup.Website.Controllers;

[Controller]
[Route("ai")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class AiController(LeaderboardFactsService facts) : Controller
{
    [HttpGet("leaderboard.md")]
    [HttpHead("leaderboard.md")]
    public IActionResult GetLeaderboardFacts() => RenderMarkdown(facts.GetLeaderboardMarkdown());

    [HttpGet("athlete-names.md")]
    [HttpHead("athlete-names.md")]
    public IActionResult GetAthleteNames() => RenderMarkdown(facts.GetAthleteNamesMarkdown());

    [HttpGet("athletes.md")]
    [HttpHead("athletes.md")]
    public IActionResult RedirectAthletes()
    {
        return RedirectPermanent(Request.PathBase.Add("/ai/leaderboard.md").ToUriComponent() + Request.QueryString.ToUriComponent());
    }

    private IActionResult RenderMarkdown(LeaderboardFactsDocument document)
    {
        var eTag = PublicGetCacheHeaders.BuildWeakContentETag(document.Markdown);

        PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AiFactsCacheControl, PublicGetCacheHeaders.AiFactsMaxAgeSeconds, eTag, document.LastModifiedUtc);
        Response.Headers["X-Robots-Tag"] = "index, follow";
        if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
            return StatusCode(StatusCodes.Status304NotModified);

        if (HttpMethods.IsHead(Request.Method))
        {
            Response.ContentType = "text/markdown; charset=utf-8";
            Response.ContentLength = Encoding.UTF8.GetByteCount(document.Markdown);
            return new EmptyResult();
        }

        return Content(document.Markdown, "text/markdown; charset=utf-8");
    }
}
