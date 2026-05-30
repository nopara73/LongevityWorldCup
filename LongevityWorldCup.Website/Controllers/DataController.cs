using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;

namespace LongevityWorldCup.Website.Controllers
{
    /// <summary>
    /// Public no-auth data endpoints for Longevity World Cup clients.
    /// </summary>
    [ApiController]
    [Route("api/data")]
    [ApiExplorerSettings(GroupName = "public-v1")]
    [Produces("application/json")]
    public class DataController(AthleteDataService svc) : Controller
    {
        private readonly AthleteDataService _svc = svc;

        /// <summary>
        /// List selectable flags.
        /// </summary>
        /// <remarks>
        /// Returns the complete public flag list accepted by athlete profile and onboarding flows.
        /// </remarks>
        /// <response code="200">The ordered list of selectable flag names.</response>
        [HttpGet("flags")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        public IActionResult GetFlags()
        {
            return Ok(Flags.Flag);
        }

        /// <summary>
        /// List competition divisions.
        /// </summary>
        /// <remarks>
        /// Returns the public division names used to group longevity athletes.
        /// </remarks>
        /// <response code="200">The ordered list of division names.</response>
        [HttpGet("divisions")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        public IActionResult GetDivisions()
        {
            return Ok(Divisions.Division);
        }

        /// <summary>
        /// List public longevity athlete data.
        /// </summary>
        /// <remarks>
        /// Returns the hydrated public athlete snapshot used by the website. Records include profile fields, public biomarker records, crowd age fields, generated asset URLs, and computed badges. The response may gain additional public fields over time.
        /// </remarks>
        /// <response code="200">The current public athlete snapshot.</response>
        [HttpGet("athletes")]
        [ProducesResponseType(typeof(IReadOnlyList<PublicAthleteApiDocument>), StatusCodes.Status200OK)]
        public IActionResult GetAthletes()
        {
            Response.Headers[HeaderNames.CacheControl] = "no-cache,max-age=0,must-revalidate";
            return Ok(_svc.GetAthletesSnapshot());
        }

        /// <summary>
        /// Preview a hypothetical Ultimate League rank.
        /// </summary>
        /// <remarks>
        /// Calculates where a hypothetical biological age result would rank in the current Ultimate League field. Use `pheno` for an Amateur Pheno Age preview and `bortz` for a Pro Bortz Age preview.
        /// </remarks>
        /// <response code="200">The hypothetical rank, field sizes, age difference, and nearby athletes.</response>
        /// <response code="400">The request body is missing, malformed, outside accepted ranges, or uses an unsupported calculator value.</response>
        [HttpPost("hypothetical-rank")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(HypotheticalRankResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
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

    /// <summary>
    /// Request body for a hypothetical rank preview.
    /// </summary>
    public sealed class HypotheticalRankRequest
    {
        /// <summary>
        /// Biological aging clock to preview: `pheno` for Amateur or `bortz` for Pro.
        /// </summary>
        /// <example>pheno</example>
        [Required]
        public string? Calculator { get; init; }

        /// <summary>
        /// Chronological age in years at the measurement date.
        /// </summary>
        /// <example>45.5</example>
        [Range(1, 300)]
        public double ChronologicalAge { get; init; }

        /// <summary>
        /// Biological age result in years from the selected clock.
        /// </summary>
        /// <example>38.2</example>
        [Range(0, 300)]
        public double BiologicalAge { get; init; }

        /// <summary>
        /// Birth year used for tie-breaking against the current field.
        /// </summary>
        /// <example>1980</example>
        [Range(1900, 2100)]
        public int BirthYear { get; init; }

        /// <summary>
        /// Birth month used for tie-breaking against the current field.
        /// </summary>
        /// <example>6</example>
        [Range(1, 12)]
        public int BirthMonth { get; init; }

        /// <summary>
        /// Birth day used for tie-breaking against the current field.
        /// </summary>
        /// <example>15</example>
        [Range(1, 31)]
        public int BirthDay { get; init; }
    }
}
