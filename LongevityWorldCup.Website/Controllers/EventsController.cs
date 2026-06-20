using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class EventsController(EventDataService events) : ControllerBase
{
    private readonly EventDataService _events = events;

    [HttpGet]
    public IActionResult Get() => Ok(_events.Events);
}
