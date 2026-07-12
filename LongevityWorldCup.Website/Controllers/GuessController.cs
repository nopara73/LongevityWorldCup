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
        public IActionResult PostAthleteAgeGuess(string athleteName, int ageGuess)
        {
            // normalize incoming name (hyphens → underscores)
            var key = athleteName.Replace('-', '_');
            if (!_svc.IsOfficialAthleteSlug(key))
            {
                return NotFound(new
                {
                    message = "Crowd age guesses are available only for approved Longevity athletes."
                });
            }

            var actualAge = _svc.GetActualAge(key);

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
                if (_crowdAgeGuessRateLimiter.TryAccept(clientIdentifier, key, out var retryAfter))
                {
                    accepted = _svc.AddAgeGuess(key, ageGuess);
                    if (!accepted)
                    {
                        return NotFound(new
                        {
                            message = "Crowd age guesses are available only for approved Longevity athletes."
                        });
                    }
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

            // always return fresh crowd stats (median‑based)
            var (median, count) = _svc.GetCrowdStats(key);

            return Ok(new
            {
                CrowdAge = median,
                CrowdCount = count,
                ActualAge = actualAge,
                GuessAccepted = accepted
            });
        }
    }
}
