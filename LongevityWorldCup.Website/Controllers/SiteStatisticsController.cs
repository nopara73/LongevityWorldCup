using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Timeouts;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/site-statistics")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class SiteStatisticsController(SiteStatisticsService statistics) : ControllerBase
{
    private readonly SiteStatisticsService _statistics = statistics;

    [HttpPost("event")]
    public async Task<IActionResult> RecordEvent([FromBody] SiteStatisticsEventRequest? request, CancellationToken ct)
    {
        await _statistics.RecordClientEventAsync(request, HttpContext, ct).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] SiteStatisticsDashboardQuery query, CancellationToken ct)
        => Ok(await _statistics.GetDashboardAsync(query, ct).ConfigureAwait(false));
}
