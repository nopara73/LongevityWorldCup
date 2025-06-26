using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/age-guess")]
    public class AgeGuessController : Controller
    {
        private readonly AthleteDataService _athletes;
        private readonly AgeGuessService _service;

        public AgeGuessController(AthleteDataService athletes, AgeGuessService service)
        {
            _athletes = athletes;
            _service = service;
        }

        public record GuessRequest(int AthleteId, int Guess);

        [HttpPost]
        public async Task<IActionResult> Guess([FromBody] GuessRequest req)
        {
            if (req.Guess < 0 || req.Guess > 125)
                return BadRequest();

            if (req.AthleteId < 1 || req.AthleteId > _athletes.Athletes.Count)
                return BadRequest();

            var athlete = _athletes.Athletes[req.AthleteId - 1]!.AsObject();
            double chrono = AgeCalculation.CalculateChronologicalAge(athlete);
            double bio = AgeCalculation.CalculateLowestPhenoAge(athlete);

            var fp = ComputeFingerprintHash();
            var guess = new AgeGuess
            {
                AthleteId = req.AthleteId,
                Guess = req.Guess,
                WhenUtc = DateTime.UtcNow,
                FingerprintHash = fp
            };
            await _service.AddGuessAsync(guess);

            var crowdAge = await _service.GetCrowdAgeAsync(req.AthleteId);
            bool winner = Math.Abs(req.Guess - chrono) < Math.Abs(crowdAge - chrono);

            var response = new JsonObject
            {
                ["chronological"] = (int)Math.Round(chrono),
                ["biological"] = (int)Math.Round(bio),
                ["crowd"] = Math.Round(crowdAge, 1),
                ["winner"] = winner
            };

            return Ok(response);
        }

        private string ComputeFingerprintHash()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers["User-Agent"].ToString();
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(ip + ua));
            return Convert.ToHexString(bytes);
        }
    }
}
