using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GuessController(AthleteDataService svc) : Controller
    {
        private readonly AthleteDataService _svc = svc;

        [HttpPost("athlete-age")]
        public IActionResult PostAthleteAgeGuess(string athleteName, int ageGuess)
        {
            if (string.IsNullOrWhiteSpace(athleteName))
                return BadRequest("Athlete name is required.");
            
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

            // record only realistic guesses
            if (!unrealistic)
                _svc.AddAgeGuess(key, ageGuess);

            // always return fresh crowd stats (median‑based)
            var (median, count) = _svc.GetCrowdStats(key);

            return Ok(new
            {
                CrowdAge = median,
                CrowdCount = count,
                ActualAge = actualAge
            });
        }
    }
}