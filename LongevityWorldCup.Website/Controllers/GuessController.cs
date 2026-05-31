using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
                var clientIdentifier = GetClientIdentifier(HttpContext);
                if (_crowdAgeGuessRateLimiter.TryAccept(clientIdentifier, key, out var retryAfter))
                {
                    _svc.AddAgeGuess(key, ageGuess);
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

        private static string GetClientIdentifier(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (IsTrustedProxyAddress(remoteIp))
            {
                var forwardedIp = TryGetForwardedIp(context);
                if (forwardedIp is not null)
                    return forwardedIp.ToString();
            }

            return remoteIp?.ToString() ?? "unknown";
        }

        private static IPAddress? TryGetForwardedIp(HttpContext context)
        {
            var cloudflareIp = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
            if (TryParseIp(cloudflareIp, out var parsedCloudflareIp))
                return parsedCloudflareIp;

            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var firstHop = forwardedFor.Split(',')[0].Trim();
                if (TryParseIp(firstHop, out var parsedForwardedIp))
                    return parsedForwardedIp;
            }

            return null;
        }

        private static bool TryParseIp(string? value, out IPAddress? ipAddress)
        {
            ipAddress = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!IPAddress.TryParse(value.Trim(), out var parsed))
                return false;

            ipAddress = parsed;
            return true;
        }

        private static bool IsTrustedProxyAddress(IPAddress? ipAddress)
        {
            if (ipAddress is null)
                return false;

            if (IPAddress.IsLoopback(ipAddress))
                return true;

            if (ipAddress.IsIPv4MappedToIPv6)
                ipAddress = ipAddress.MapToIPv4();

            if (ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var bytes = ipAddress.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        }
    }
}
