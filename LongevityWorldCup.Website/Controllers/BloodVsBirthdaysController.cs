using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/blood-vs-birthdays")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class BloodVsBirthdaysController(BloodVsBirthdaysService game,
    ILogger<BloodVsBirthdaysController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        try
        {
            return Ok(game.GetToday());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            logger.LogError(ex, "Could not load the Blood vs. Birthdays daily puzzle");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Today's matchups are temporarily unavailable.");
        }
    }
}
