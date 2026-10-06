using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/score-xray")]
[ApiExplorerSettings(IgnoreApi = true)]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class ScoreXrayController(AthleteDataService athletes, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public IActionResult Directory()
    {
        ReviewHeaders();
        return Ok(Snapshot().Directory());
    }

    [HttpGet("{slug}")]
    public IActionResult Athlete(string slug)
    {
        ReviewHeaders();
        var document = Snapshot().Find(AthleteSlug.Normalize(slug));
        return document is null ? NotFound() : Ok(document);
    }

    private ScoreXraySnapshot Snapshot() => new(athletes.GetAthletesSnapshot(), clock.GetUtcNow().UtcDateTime);
    private void ReviewHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
