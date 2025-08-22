using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class EventsController(EventDataService events) : ControllerBase
{
    private readonly EventDataService _events = events;

    [HttpGet]
    public IActionResult Get() => Ok(_events.Events);
}