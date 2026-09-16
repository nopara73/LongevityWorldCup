using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using LongevityWorldCup.Website.Middleware;

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

    [HttpGet("league/{slug}.md")]
    [HttpHead("league/{slug}.md")]
    public IActionResult GetLeague(string slug)
    {
        var view = LeaderboardViewCatalog.Find(slug);
        if (view is null) return NotFound();
        if (RouteCanonicalization.RedirectToCanonical(HttpContext, view.MarkdownPath)) return new EmptyResult();
        return RenderMarkdown(facts.GetLeagueMarkdown(view.Slug)!);
    }

    [HttpGet("athlete/{slug}.md")]
    [HttpHead("athlete/{slug}.md")]
    public IActionResult GetAthlete(string slug)
    {
        var document = facts.GetAthleteMarkdown(slug);
        if (document is null) return NotFound();
        var path = "/ai/athlete/" + AthleteSlug.Normalize(slug).Replace('_', '-') + ".md";
        if (RouteCanonicalization.RedirectToCanonical(HttpContext, path)) return new EmptyResult();
        return RenderMarkdown(document);
    }

    [HttpGet("/llms.txt")]
    [HttpHead("/llms.txt")]
    public IActionResult GetLlms() => RenderMarkdown(facts.Document("/llms.txt", "/", AiDiscoveryCatalog.Markdown(false), false), "text/plain");

    [HttpGet("/llms-full.txt")]
    [HttpHead("/llms-full.txt")]
    public IActionResult GetLlmsFull() => RenderMarkdown(facts.Document("/llms-full.txt", "/", AiDiscoveryCatalog.Markdown(true), false), "text/plain");

    [HttpGet("index.md")]
    [HttpHead("index.md")]
    public IActionResult GetIndex() => RenderMarkdown(facts.Document("/ai/index.md", "/", AiDiscoveryCatalog.Markdown(false), false));

    [HttpGet("/.well-known/agent-card.json")]
    [HttpHead("/.well-known/agent-card.json")]
    public IActionResult GetAgentCard() => RenderMarkdown(facts.Document("/.well-known/agent-card.json", "/", AiDiscoveryCatalog.AgentCard(), false), "application/json");

    [HttpGet("athletes.md")]
    [HttpHead("athletes.md")]
    public IActionResult RedirectAthletes()
    {
        return RedirectPermanent(Request.PathBase.Add("/ai/leaderboard.md").ToUriComponent() + Request.QueryString.ToUriComponent());
    }

    private IActionResult RenderMarkdown(LeaderboardFactsDocument document, string mediaType = "text/markdown")
    {
        var eTag = PublicGetCacheHeaders.BuildWeakContentETag(document.Markdown);

        PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AiFactsCacheControl, PublicGetCacheHeaders.AiFactsMaxAgeSeconds, eTag, document.LastModifiedUtc);
        Response.Headers["X-Robots-Tag"] = "index, follow";
        if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
            return StatusCode(StatusCodes.Status304NotModified);

        if (HttpMethods.IsHead(Request.Method))
        {
            Response.ContentType = mediaType + "; charset=utf-8";
            Response.ContentLength = Encoding.UTF8.GetByteCount(document.Markdown);
            return new EmptyResult();
        }

        return Content(document.Markdown, mediaType + "; charset=utf-8");
    }
}
