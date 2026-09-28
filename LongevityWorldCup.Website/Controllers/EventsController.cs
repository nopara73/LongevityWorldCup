using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/events")]
[ApiExplorerSettings(GroupName = "public-v1")]
[Produces("application/json")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class EventsController(EventDataService events) : ControllerBase
{
    private readonly EventDataService _events = events;

    /// <summary>List public competition Events.</summary>
    /// <remarks>
    /// Returns the complete website-visible Event snapshot, newest first, including athlete-profile-only
    /// accepted-result Events. Social-only and hidden Events are excluded. See the response schema and
    /// Event type catalog for payload formats and historical date semantics.
    /// </remarks>
    /// <response code="200">All website-visible Events, or an empty array when none exist.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PublicEventApiDocument>), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(_events.Events);
}
