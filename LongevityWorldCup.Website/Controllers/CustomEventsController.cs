using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/custom-events")]
[RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
public sealed class CustomEventsController(EventDataService events, Config config, ILogger<CustomEventsController> log) : ControllerBase
{
    private const int MaxRequestBytes = 16 * 1024;
    private const int MaxSecretLength = 1024;
    private const int MaxTitleLength = 500;
    private const int MaxContentLength = 10_000;

    private readonly EventDataService _events = events;
    private readonly Config _config = config;
    private readonly ILogger<CustomEventsController> _log = log;

    public sealed record CreateCustomEventRequest(
        string? Secret,
        string? Title,
        string? Content,
        bool SendToWebpage,
        bool SendToSlack,
        bool SendToX,
        bool SendToThreads,
        bool SendToFacebook);

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public IActionResult Create([FromBody] CreateCustomEventRequest? request)
    {
        if (request is null)
            return BadRequest("Request body is required.");

        if (string.IsNullOrEmpty(request.Secret) || request.Secret.Length > MaxSecretLength)
        {
            _log.LogWarning("Custom Event Designer direct queue rejected because the secret was missing or too long.");
            return Unauthorized("Invalid secret.");
        }

        var verification = SecretHashVerifier.Verify(request.Secret, _config.CustomEventDesignerSecretHash);
        if (verification == SecretVerificationResult.NotConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Custom Event Designer direct queueing is not configured.");
        if (verification == SecretVerificationResult.InvalidHash)
        {
            _log.LogError("Custom Event Designer direct queueing is disabled because CustomEventDesignerSecretHash is invalid.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Custom Event Designer direct queueing is not configured.");
        }
        if (verification != SecretVerificationResult.Verified)
        {
            _log.LogWarning("Custom Event Designer direct queue rejected because the secret did not match.");
            return Unauthorized("Invalid secret.");
        }

        var title = request.Title?.Trim() ?? "";
        var content = request.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Title is required.");
        if (title.Length > MaxTitleLength)
            return BadRequest($"Title must be {MaxTitleLength} characters or fewer.");
        if (content.Length > MaxContentLength)
            return BadRequest($"Content must be {MaxContentLength} characters or fewer.");

        var targets = new CustomEventDeliveryTargets(
            request.SendToWebpage,
            request.SendToSlack,
            request.SendToX,
            request.SendToThreads,
            request.SendToFacebook);
        var selectedTargets = GetSelectedTargets(targets);
        if (selectedTargets.Count == 0)
            return BadRequest("Select at least one destination.");

        var eventId = _events.CreateCustomEvent(
            titleRaw: title,
            contentRaw: content,
            deliveryTargets: targets);

        _log.LogInformation(
            "Custom Event Designer direct queue created event {EventId} for targets {Targets}.",
            eventId,
            string.Join(",", selectedTargets));

        return Ok(new
        {
            success = true,
            eventId,
            queuedTargets = selectedTargets,
            selectedTargets
        });
    }

    private static List<string> GetSelectedTargets(CustomEventDeliveryTargets targets)
    {
        var selected = new List<string>();
        if (targets.SendToWebpage) selected.Add("webpage");
        if (targets.SendToSlack) selected.Add("slack");
        if (targets.SendToX) selected.Add("x");
        if (targets.SendToThreads) selected.Add("threads");
        if (targets.SendToFacebook) selected.Add("facebook");
        return selected;
    }
}
