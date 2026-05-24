using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;

namespace LongevityWorldCup.Website.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController(AthleteDataService svc) : Controller
    {
        private readonly AthleteDataService _svc = svc;

        [HttpGet("flags")]
        public IActionResult GetFlags()
        {
            return Ok(Flags.Flag);
        }

        [HttpGet("divisions")]
        public IActionResult GetDivisions()
        {
            return Ok(Divisions.Division);
        }

        [HttpGet("athletes")]
        public IActionResult GetAthletes()
        {
            Response.Headers[HeaderNames.CacheControl] = "no-cache,max-age=0,must-revalidate";
            return Ok(_svc.GetAthletesSnapshot());
        }

        [HttpPost("hypothetical-rank")]
        public IActionResult GetHypotheticalRank([FromBody] HypotheticalRankRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!double.IsFinite(request.ChronologicalAge) || request.ChronologicalAge <= 0 ||
                !double.IsFinite(request.BiologicalAge) || request.BiologicalAge < 0)
            {
                return BadRequest(new { message = "Invalid age values." });
            }

            var calculator = request.Calculator?.Trim().ToLowerInvariant();
            var hasBortz = calculator switch
            {
                "bortz" => true,
                "pheno" => false,
                _ => (bool?)null
            };
            if (!hasBortz.HasValue)
                return BadRequest(new { message = "Calculator must be pheno or bortz." });

            DateTime dobUtc;
            try
            {
                dobUtc = new DateTime(request.BirthYear, request.BirthMonth, request.BirthDay, 0, 0, 0, DateTimeKind.Utc);
            }
            catch
            {
                return BadRequest(new { message = "Invalid date of birth." });
            }

            Response.Headers[HeaderNames.CacheControl] = "no-cache,max-age=0,must-revalidate";
            var result = _svc.CalculateHypotheticalRank(request.ChronologicalAge, request.BiologicalAge, dobUtc, hasBortz.Value);
            return Ok(result);
        }
    }

    public sealed class HypotheticalRankRequest
    {
        [Required]
        public string? Calculator { get; init; }

        [Range(1, 300)]
        public double ChronologicalAge { get; init; }

        [Range(0, 300)]
        public double BiologicalAge { get; init; }

        [Range(1900, 2100)]
        public int BirthYear { get; init; }

        [Range(1, 12)]
        public int BirthMonth { get; init; }

        [Range(1, 31)]
        public int BirthDay { get; init; }
    }
}
