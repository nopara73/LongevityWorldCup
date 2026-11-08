using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/reddit/ifttt/receipts")]
[RequestSizeLimit(256 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RedditIftttReceiptsController(Config config, RedditDeliveryStore deliveries, TimeProvider time) : ControllerBase
{
    public sealed record ReceiptRequest(string? Subreddit, string? Author, string? PostUrl, string? Title,
        string? Text, long? PostedAtUnixSeconds);

    [HttpPost]
    public IActionResult Receive([FromBody] ReceiptRequest request)
    {
        if (!config.RedditUseIfttt || string.IsNullOrWhiteSpace(config.RedditIftttReceiptSecretHash))
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var header = Request.Headers.Authorization;
        if (header.Count != 1 || header[0] is not { Length: <= 1024 } value ||
            !value.StartsWith("Bearer ", StringComparison.Ordinal))
            return Unauthorized();
        var verification = SecretHashVerifier.Verify(value[7..], config.RedditIftttReceiptSecretHash);
        if (verification == SecretVerificationResult.InvalidHash)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (verification != SecretVerificationResult.Verified) return Unauthorized();

        var now = time.GetUtcNow();
        var postId = RedditIftttPost.GetPostId(request.PostUrl);
        if (!string.Equals(request.Subreddit, RedditAnnouncementService.Subreddit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Author, RedditIftttPost.Author, StringComparison.OrdinalIgnoreCase) ||
            postId is null || request.Title is not { Length: > 0 and <= RedditPost.MaxTitleLength } title ||
            request.Text is not { Length: > 0 and <= RedditPost.MaxTextLength } text ||
            request.PostedAtUnixSeconds is not > 0 ||
            request.PostedAtUnixSeconds > now.AddMinutes(2).ToUnixTimeSeconds())
            return BadRequest();

        var postedAt = DateTimeOffset.FromUnixTimeSeconds(request.PostedAtUnixSeconds.Value);
        var matched = deliveries.ConfirmIfttt(RedditIftttPost.Fingerprint(title, text),
            RedditIftttPost.FingerprintFromHtml(title, text), postId, postedAt, now);
        return Ok(new { matched });
    }
}
