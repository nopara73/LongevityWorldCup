using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
    public class GuessController(
        AthleteDataService svc,
        CrowdAgeGuessRateLimiter crowdAgeGuessRateLimiter,
        ILogger<GuessController> logger) : Controller
    {
        private readonly AthleteDataService _svc = svc;
        private readonly CrowdAgeGuessRateLimiter _crowdAgeGuessRateLimiter = crowdAgeGuessRateLimiter;
        private readonly ILogger<GuessController> _logger = logger;

        [HttpPost("athlete-age")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public IActionResult PostAthleteAgeGuess(
            [FromQuery] string athleteName,
            [FromQuery] int ageGuess,
            [FromQuery] string? profileImageId)
        {
            // normalize incoming name (hyphens → underscores)
            var key = athleteName.Replace('-', '_');
            var actualAge = _svc.GetActualAge(key);
            if (actualAge <= 0)
            {
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Athlete not found."
                });
            }

            if (!IsSha256Hex(profileImageId))
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "A valid profile image ID is required."
                });
            }

            var normalizedProfileImageId = profileImageId!.ToLowerInvariant();
            if (!_svc.TryGetProfileImageId(key, out var currentProfileImageId) ||
                !string.Equals(currentProfileImageId, normalizedProfileImageId, StringComparison.Ordinal))
            {
                return ProfileImageChanged();
            }

            // rejection rules ─ hard limits + asymmetric “too old” cap
            const int MinGuess = 10;
            const int MaxGuess = 110;
            const double UpwardPct = 0.30;

            bool unrealistic =
                ageGuess < MinGuess ||
                ageGuess > MaxGuess ||
                (ageGuess > actualAge &&
                 (ageGuess - actualAge) > actualAge * UpwardPct);

            var accepted = false;

            // record only realistic guesses
            if (!unrealistic)
            {
                var clientIdentifier = ClientIdentifier.From(HttpContext);
                if (_crowdAgeGuessRateLimiter.TryAccept(
                        clientIdentifier,
                        key,
                        normalizedProfileImageId,
                        out var retryAfter))
                {
                    // Revalidate under AthleteDataService's reload lock so a picture
                    // replacement between the initial check and persistence is rejected.
                    if (!_svc.TryAddAgeGuess(key, normalizedProfileImageId, ageGuess))
                        return ProfileImageChanged();

                    accepted = true;
                }
                else
                {
                    Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    _logger.LogDebug(
                        "Skipped rate-limited Crowd Age guess for {AthleteSlug}. Retry after {RetryAfterSeconds}s.",
                        key,
                        Math.Ceiling(retryAfter.TotalSeconds));
                }
            }

            // Rate-limited and unrealistic guesses do not enter TryAddAgeGuess, so
            // recheck them too before disclosing the athlete's chronological age.
            if (!_svc.TryGetCrowdStatsForProfileImage(
                    key,
                    normalizedProfileImageId,
                    out var crowdStats))
            {
                return ProfileImageChanged();
            }

            return Ok(new
            {
                CrowdAge = crowdStats.Median,
                CrowdCount = crowdStats.Count,
                ActualAge = actualAge,
                GuessAccepted = accepted,
                ProfileImageId = normalizedProfileImageId
            });
        }

        private IActionResult ProfileImageChanged()
            => Conflict(new
            {
                Code = "profile_image_changed",
                Message = "This athlete's profile picture changed. Please guess again from the current picture."
            });

        private static bool IsSha256Hex(string? value)
            => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}
