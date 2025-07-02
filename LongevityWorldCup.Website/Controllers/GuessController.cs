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
            // normalize incoming name (hyphens → underscores)
            var key = athleteName.Replace('-', '_');

            // record the guess
            _svc.AddAgeGuess(key, ageGuess);

            // recompute median & count
            var (median, count) = _svc.GetCrowdStats(key);

            // return updated crowd stats
            return Ok(new { CrowdAge = median, CrowdCount = count });
        }
    }
}