using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/web-push")]
[EnableRateLimiting(WebPushTransport.RateLimitPolicy)]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class WebPushController(WebPushTransport transport, WebPushStore store) : ControllerBase
{
    [HttpGet("configuration")]
    public IActionResult Configuration()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { publicKey = transport.PublicKey });
    }

    [HttpPost("subscribe")]
    [RequestSizeLimit(4096)]
    public IActionResult Subscribe([FromBody] WebPushSubscription? subscription)
    {
        if (!FromThisWebsite()) return BadRequest("Open the website to manage notifications.");
        if (!transport.IsConfigured) return StatusCode(503, "Website notifications are not configured.");
        if (!WebPushTransport.IsValid(subscription)) return BadRequest("Invalid push subscription.");
        try { store.Subscribe(subscription!, DateTimeOffset.UtcNow); }
        catch (InvalidOperationException) { return Conflict("The subscription could not be saved."); }
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpPost("unsubscribe")]
    [RequestSizeLimit(4096)]
    public IActionResult Unsubscribe([FromBody] WebPushSubscription? subscription)
    {
        if (!FromThisWebsite()) return BadRequest("Open the website to manage notifications.");
        if (!WebPushTransport.IsValid(subscription)) return BadRequest("Invalid push subscription.");
        store.Unsubscribe(subscription!);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    private bool FromThisWebsite()
    {
        if (Request.Headers["X-LWC-Push"] != "1") return false;
        var origin = Request.Headers.Origin.ToString();
        if (origin.Length == 0) return Request.Headers["Sec-Fetch-Site"] == "same-origin";
        return string.Equals(origin, $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase)
            && Request.Headers["Sec-Fetch-Site"] != "cross-site";
    }
}
