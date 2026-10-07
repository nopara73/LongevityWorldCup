using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/reddit")]
[RequestSizeLimit(2048)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class RedditBridgeController(Config config, RedditAnnouncementService announcements,
    RedditDeliveryStore deliveries, TimeProvider time) : ControllerBase
{
    public sealed record DeliveryRequest(string? DeliveryId);
    public sealed record ReceiptRequest(string? DeliveryId, string? PostId);

    [HttpPost("next")]
    public async Task<IActionResult> Next(CancellationToken ct)
    {
        if (Authenticate() is { } rejected) return rejected;
        var next = await announcements.GetNextAsync(ct);
        return next is null ? NoContent() : Ok(next);
    }

    [HttpPost("begin")]
    public IActionResult Begin([FromBody] DeliveryRequest request)
    {
        if (Authenticate() is { } rejected) return rejected;
        if (!ValidKey(request.DeliveryId)) return BadRequest();
        var result = deliveries.Begin(request.DeliveryId!, time.GetUtcNow());
        return result == RedditBeginResult.Started ? Ok(new { started = true }) : Conflict(new { reason = result.ToString() });
    }

    [HttpPost("receipts")]
    public IActionResult Receipt([FromBody] ReceiptRequest request)
    {
        if (Authenticate() is { } rejected) return rejected;
        if (!ValidKey(request.DeliveryId) || request.PostId is null || !PostIdPattern().IsMatch(request.PostId)) return BadRequest();
        return deliveries.Complete(request.DeliveryId!, request.PostId, time.GetUtcNow()) ? Ok(new { saved = true }) : Conflict();
    }

    [HttpPost("uncertain")]
    public IActionResult Uncertain([FromBody] DeliveryRequest request)
    {
        if (Authenticate() is { } rejected) return rejected;
        if (!ValidKey(request.DeliveryId)) return BadRequest();
        return deliveries.RequireReview(request.DeliveryId!, time.GetUtcNow()) ? Ok(new { saved = true }) : Conflict();
    }

    private IActionResult? Authenticate()
    {
        if (!config.RedditEnabled) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || header[0] is not { Length: <= 1024 } value || !value.StartsWith("Bearer ", StringComparison.Ordinal)) return Unauthorized();
        var verification = SecretHashVerifier.Verify(value[7..], config.RedditBridgeSecretHash);
        if (verification is SecretVerificationResult.NotConfigured or SecretVerificationResult.InvalidHash)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        return verification == SecretVerificationResult.Verified ? null : Unauthorized();
    }

    private static bool ValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);
    [GeneratedRegex(@"\Alwc-[a-f0-9]{64}\z")]
    private static partial Regex KeyPattern();
    [GeneratedRegex(@"\At3_[a-z0-9]{1,32}\z")]
    private static partial Regex PostIdPattern();
}
