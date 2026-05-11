using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace LongevityWorldCup.Website.Controllers;

[Controller]
[Route("ai")]
public sealed class AiController(LeaderboardFactsService facts) : Controller
{
    [HttpGet("leaderboard.md")]
    public IActionResult GetLeaderboardFacts()
    {
        var document = facts.GetLeaderboardMarkdown();

        Response.Headers[HeaderNames.CacheControl] = "public, max-age=300, must-revalidate";
        Response.Headers[HeaderNames.LastModified] = document.LastModifiedUtc.UtcDateTime.ToString("R");
        Response.Headers["X-Robots-Tag"] = "index, follow";

        return Content(document.Markdown, "text/markdown; charset=utf-8");
    }

    [HttpGet("athlete-names.md")]
    public IActionResult GetAthleteNames()
    {
        var document = facts.GetAthleteNamesMarkdown();

        Response.Headers[HeaderNames.CacheControl] = "public, max-age=300, must-revalidate";
        Response.Headers[HeaderNames.LastModified] = document.LastModifiedUtc.UtcDateTime.ToString("R");
        Response.Headers["X-Robots-Tag"] = "index, follow";

        return Content(document.Markdown, "text/markdown; charset=utf-8");
    }

    [HttpGet("athletes.md")]
    public IActionResult RedirectAthletes()
    {
        return RedirectPermanent("/ai/leaderboard.md");
    }
}
