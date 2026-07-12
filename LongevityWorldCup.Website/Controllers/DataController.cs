using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace LongevityWorldCup.Website.Controllers
{
    /// <summary>
    /// Public no-auth data endpoints for Longevity World Cup clients.
    /// </summary>
    [ApiController]
    [Route("api/data")]
    [ApiExplorerSettings(GroupName = "public-v1")]
    [Produces("application/json")]
    [RequestTimeout(PublicRequestTimeoutPolicies.PublicWork)]
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
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        public IActionResult GetFlags()
        {
            var eTag = PublicGetCacheHeaders.BuildWeakContentETag(JsonSerializer.Serialize(Flags.Flag));
            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.StaticReferenceCacheControl, PublicGetCacheHeaders.StaticReferenceMaxAgeSeconds, eTag);
            if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
                return StatusCode(StatusCodes.Status304NotModified);

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
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        public IActionResult GetDivisions()
        {
            var eTag = PublicGetCacheHeaders.BuildWeakContentETag(JsonSerializer.Serialize(Divisions.Division));
            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.StaticReferenceCacheControl, PublicGetCacheHeaders.StaticReferenceMaxAgeSeconds, eTag);
            if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(Divisions.Division);
        }

        /// <summary>
        /// List public longevity athlete data.
        /// </summary>
        /// <remarks>
        /// Returns approved applicants only. Records include profile fields, public biomarker records,
        /// crowd age fields, generated asset URLs, and computed badges. Unranked OpenData profiles are
        /// available only from the leaderboard-profiles endpoint.
        /// </remarks>
        /// <response code="200">The current public athlete snapshot.</response>
        [HttpGet("athletes")]
        [ProducesResponseType(typeof(IReadOnlyList<PublicAthleteApiDocument>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        public IActionResult GetAthletes()
        {
            var snapshot = _svc.GetAthletesSnapshot();
            var eTag = PublicGetCacheHeaders.BuildWeakContentETag(snapshot.ToJsonString());

            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AthleteSnapshotCacheControl, PublicGetCacheHeaders.AthleteSnapshotMaxAgeSeconds, eTag);

            if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(snapshot);
        }

        /// <summary>
        /// List all leaderboard profiles, including clearly marked unranked open-data profiles.
        /// </summary>
        /// <remarks>
        /// Returns approved Longevity athletes plus a capped set of OpenData profiles whose complete
        /// nine-marker Pheno panels and age at draw were transcribed from linked, self-published bloodwork. A source that publishes only a month is represented explicitly as month precision; no day is inferred. OpenData subjects did not apply and never participate in
        /// ranks, badges, placements, prizes, athlete counts, crowd age, or competition Events.
        /// Inspect `ProfileType` and the `OpenData` provenance object before presenting a record.
        /// </remarks>
        /// <response code="200">The current combined leaderboard profile snapshot.</response>
        [HttpGet("leaderboard-profiles")]
        [ProducesResponseType(typeof(IReadOnlyList<PublicAthleteApiDocument>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        public IActionResult GetLeaderboardProfiles()
        {
            var snapshot = _svc.GetLeaderboardProfilesSnapshot();
            var eTag = PublicGetCacheHeaders.BuildWeakContentETag(snapshot.ToJsonString());

            PublicGetCacheHeaders.Apply(Response, PublicGetCacheHeaders.AthleteSnapshotCacheControl, PublicGetCacheHeaders.AthleteSnapshotMaxAgeSeconds, eTag);

            if (PublicGetCacheHeaders.RequestHasMatchingETag(Request.Headers, eTag))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(snapshot);
        }

        /// <summary>
        /// Calculate a Pheno Age result.
        /// </summary>
        /// <remarks>
        /// Calculates pheno age from chronological age and the nine blood biomarkers used by the Longevity World Cup Amateur clock. The response includes Biological Age Difference so clients can pass the result to the hypothetical rank endpoint.
        /// </remarks>
        /// <response code="200">The calculated Pheno Age result and model contribution values.</response>
        /// <response code="400">The request body is missing, malformed, or contains out-of-range biological aging clock inputs.</response>
        [HttpPost("pheno-age")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(PhenoAgeCalculationResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        public IActionResult CalculatePhenoAge([FromBody] PhenoAgeCalculationRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!request.TryBuildMarkerValues(out var values, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            var biologicalAge = PhenoAgeHelper.CalculatePhenoAge(values);
            if (!double.IsFinite(biologicalAge))
                return BadRequest(new { message = "Unable to calculate Pheno Age from the supplied inputs." });

            var chronologicalAge = request.ChronologicalAge!.Value;
            var result = new PhenoAgeCalculationResult(
                biologicalAge,
                biologicalAge - chronologicalAge,
                biologicalAge / chronologicalAge,
                new PhenoAgeDomainContributions(
                    PhenoAgeHelper.CalculateLiverPhenoAgeContributor(values),
                    PhenoAgeHelper.CalculateKidneyPhenoAgeContributor(values),
                    PhenoAgeHelper.CalculateMetabolicPhenoAgeContributor(values),
                    PhenoAgeHelper.CalculateInflammationPhenoAgeContributor(values),
                    PhenoAgeHelper.CalculateImmunePhenoAgeContributor(values)));

            return Ok(result);
        }

        /// <summary>
        /// Calculate a Bortz Age result.
        /// </summary>
        /// <remarks>
        /// Calculates bortz age from chronological age and the public biomarker panel used by the Longevity World Cup Pro clock. Monocyte and neutrophil counts are derived from WBC and percentage inputs to match the website calculation.
        /// </remarks>
        /// <response code="200">The calculated Bortz Age result, biological age acceleration, and derived count inputs.</response>
        /// <response code="400">The request body is missing, malformed, or contains out-of-range biological aging clock inputs.</response>
        [HttpPost("bortz-age")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(BortzAgeCalculationResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        public IActionResult CalculateBortzAge([FromBody] BortzAgeCalculationRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (!request.TryBuildMarkerValues(out var values, out var monocyteCount, out var neutrophilCount, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            var biologicalAgeAcceleration = BortzAgeHelper.CalculateBAA(values);
            var chronologicalAge = request.ChronologicalAge!.Value;
            var biologicalAge = BortzAgeHelper.CalculateBortzAge(chronologicalAge, biologicalAgeAcceleration);
            if (!double.IsFinite(biologicalAge))
                return BadRequest(new { message = "Unable to calculate Bortz Age from the supplied inputs." });

            var result = new BortzAgeCalculationResult(
                biologicalAge,
                biologicalAge - chronologicalAge,
                biologicalAge / chronologicalAge,
                biologicalAgeAcceleration,
                monocyteCount,
                neutrophilCount);

            return Ok(result);
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

    /// <summary>
    /// Request body for calculating Pheno Age.
    /// </summary>
    public sealed class PhenoAgeCalculationRequest
    {
        /// <summary>Chronological age in years at the biomarker measurement date.</summary>
        /// <example>45.5</example>
        [Required]
        [Range(1, 300)]
        public double? ChronologicalAge { get; init; }

        /// <summary>Albumin in g/L.</summary>
        /// <example>46</example>
        [Required]
        [Range(0.1, 200)]
        public double? AlbGL { get; init; }

        /// <summary>Creatinine in umol/L.</summary>
        /// <example>88.4</example>
        [Required]
        [Range(0.1, 3000)]
        public double? CreatUmolL { get; init; }

        /// <summary>Glucose in mmol/L.</summary>
        /// <example>5.05</example>
        [Required]
        [Range(0.1, 100)]
        public double? GluMmolL { get; init; }

        /// <summary>C-reactive protein in mg/L. Must be greater than 0 because the clock applies a logarithm.</summary>
        /// <example>0.8</example>
        [Required]
        [Range(0.000001, 10000)]
        public double? CrpMgL { get; init; }

        /// <summary>White blood cell count in 1000 cells/uL.</summary>
        /// <example>6.3</example>
        [Required]
        [Range(0.1, 1000)]
        public double? Wbc1000cellsuL { get; init; }

        /// <summary>Lymphocyte percentage.</summary>
        /// <example>25.5</example>
        [Required]
        [Range(0, 100)]
        public double? LymPc { get; init; }

        /// <summary>Mean corpuscular volume in fL.</summary>
        /// <example>96.7</example>
        [Required]
        [Range(1, 300)]
        public double? McvFL { get; init; }

        /// <summary>Red cell distribution width percentage.</summary>
        /// <example>12.5</example>
        [Required]
        [Range(0.1, 100)]
        public double? RdwPc { get; init; }

        /// <summary>Alkaline phosphatase in U/L.</summary>
        /// <example>51</example>
        [Required]
        [Range(0.1, 10000)]
        public double? AlpUL { get; init; }

        internal bool TryBuildMarkerValues(out double[] values, out string errorMessage)
        {
            values = Array.Empty<double>();
            errorMessage = "";
            var chronologicalAge = ChronologicalAge!.Value;
            if (!PublicCalculationValidation.AllFinite(chronologicalAge, AlbGL, CreatUmolL, GluMmolL, CrpMgL, Wbc1000cellsuL, LymPc, McvFL, RdwPc, AlpUL))
            {
                errorMessage = "All Pheno Age inputs must be finite numbers.";
                return false;
            }

            if (CrpMgL!.Value <= 0)
            {
                errorMessage = "CRP must be greater than 0.";
                return false;
            }

            values =
            [
                chronologicalAge,
                AlbGL!.Value,
                CreatUmolL!.Value,
                GluMmolL!.Value,
                Math.Log(CrpMgL.Value / 10.0),
                Wbc1000cellsuL!.Value,
                LymPc!.Value,
                McvFL!.Value,
                RdwPc!.Value,
                AlpUL!.Value
            ];
            return true;
        }
    }

    /// <summary>
    /// Request body for calculating Bortz Age.
    /// </summary>
    public sealed class BortzAgeCalculationRequest
    {
        /// <summary>Chronological age in years at the biomarker measurement date.</summary>
        /// <example>45.5</example>
        [Required]
        [Range(1, 300)]
        public double? ChronologicalAge { get; init; }

        /// <summary>Albumin in g/L.</summary>
        /// <example>46</example>
        [Required]
        [Range(0.1, 200)]
        public double? AlbGL { get; init; }

        /// <summary>Alkaline phosphatase in U/L.</summary>
        /// <example>51</example>
        [Required]
        [Range(0.1, 10000)]
        public double? AlpUL { get; init; }

        /// <summary>Urea in mmol/L.</summary>
        /// <example>5.4</example>
        [Required]
        [Range(0.1, 100)]
        public double? UreaMmolL { get; init; }

        /// <summary>Total cholesterol in mmol/L.</summary>
        /// <example>4.8</example>
        [Required]
        [Range(0.1, 100)]
        public double? CholesterolMmolL { get; init; }

        /// <summary>Creatinine in umol/L.</summary>
        /// <example>88.4</example>
        [Required]
        [Range(0.1, 3000)]
        public double? CreatUmolL { get; init; }

        /// <summary>Cystatin C in mg/L.</summary>
        /// <example>0.85</example>
        [Required]
        [Range(0.1, 100)]
        public double? CystatinCMgL { get; init; }

        /// <summary>HbA1c in mmol/mol.</summary>
        /// <example>34</example>
        [Required]
        [Range(1, 300)]
        public double? Hba1cMmolMol { get; init; }

        /// <summary>C-reactive protein in mg/L. Must be greater than 0 because the clock applies a logarithm.</summary>
        /// <example>0.8</example>
        [Required]
        [Range(0.000001, 10000)]
        public double? CrpMgL { get; init; }

        /// <summary>Gamma-glutamyl transferase in U/L. Must be greater than 0 because the clock applies a logarithm.</summary>
        /// <example>22</example>
        [Required]
        [Range(0.000001, 10000)]
        public double? GgtUL { get; init; }

        /// <summary>Red blood cell count in 10e12/L.</summary>
        /// <example>4.7</example>
        [Required]
        [Range(0.1, 100)]
        public double? Rbc10e12L { get; init; }

        /// <summary>Mean corpuscular volume in fL.</summary>
        /// <example>96.7</example>
        [Required]
        [Range(1, 300)]
        public double? McvFL { get; init; }

        /// <summary>Red cell distribution width percentage.</summary>
        /// <example>12.5</example>
        [Required]
        [Range(0.1, 100)]
        public double? RdwPc { get; init; }

        /// <summary>White blood cell count in 1000 cells/uL.</summary>
        /// <example>6.3</example>
        [Required]
        [Range(0.1, 1000)]
        public double? Wbc1000cellsuL { get; init; }

        /// <summary>Monocyte percentage.</summary>
        /// <example>7.2</example>
        [Required]
        [Range(0, 100)]
        public double? MonocytePc { get; init; }

        /// <summary>Neutrophil percentage.</summary>
        /// <example>58.1</example>
        [Required]
        [Range(0, 100)]
        public double? NeutrophilPc { get; init; }

        /// <summary>Lymphocyte percentage.</summary>
        /// <example>25.5</example>
        [Required]
        [Range(0, 100)]
        public double? LymPc { get; init; }

        /// <summary>Alanine aminotransferase in U/L. Must be greater than 0 because the clock applies a logarithm.</summary>
        /// <example>24</example>
        [Required]
        [Range(0.000001, 10000)]
        public double? AltUL { get; init; }

        /// <summary>Sex hormone-binding globulin in nmol/L. Must be greater than 0 because the clock applies a logarithm.</summary>
        /// <example>45</example>
        [Required]
        [Range(0.000001, 10000)]
        public double? ShbgNmolL { get; init; }

        /// <summary>Vitamin D in nmol/L. Must be greater than 0 because the clock applies a logarithm.</summary>
        /// <example>85</example>
        [Required]
        [Range(0.000001, 10000)]
        public double? VitaminDNmolL { get; init; }

        /// <summary>Glucose in mmol/L.</summary>
        /// <example>5.05</example>
        [Required]
        [Range(0.1, 100)]
        public double? GluMmolL { get; init; }

        /// <summary>Mean corpuscular hemoglobin in pg.</summary>
        /// <example>31.5</example>
        [Required]
        [Range(0.1, 100)]
        public double? MchPg { get; init; }

        /// <summary>Apolipoprotein A1 in g/L.</summary>
        /// <example>1.55</example>
        [Required]
        [Range(0.1, 100)]
        public double? ApoA1GL { get; init; }

        internal bool TryBuildMarkerValues(out double[] values, out double monocyteCount, out double neutrophilCount, out string errorMessage)
        {
            values = Array.Empty<double>();
            monocyteCount = 0;
            neutrophilCount = 0;
            errorMessage = "";
            var chronologicalAge = ChronologicalAge!.Value;
            if (!PublicCalculationValidation.AllFinite(chronologicalAge, AlbGL, AlpUL, UreaMmolL, CholesterolMmolL, CreatUmolL, CystatinCMgL,
                    Hba1cMmolMol, CrpMgL, GgtUL, Rbc10e12L, McvFL, RdwPc, Wbc1000cellsuL, MonocytePc,
                    NeutrophilPc, LymPc, AltUL, ShbgNmolL, VitaminDNmolL, GluMmolL, MchPg, ApoA1GL))
            {
                errorMessage = "All Bortz Age inputs must be finite numbers.";
                return false;
            }

            if (CrpMgL!.Value <= 0 || GgtUL!.Value <= 0 || AltUL!.Value <= 0 || ShbgNmolL!.Value <= 0 || VitaminDNmolL!.Value <= 0)
            {
                errorMessage = "CRP, GGT, ALT, SHBG, and Vitamin D must be greater than 0.";
                return false;
            }

            monocyteCount = BortzAgeHelper.DeriveMonocyteCountFromPc(Wbc1000cellsuL!.Value, MonocytePc!.Value);
            neutrophilCount = BortzAgeHelper.DeriveNeutrophilCountFromPc(Wbc1000cellsuL.Value, NeutrophilPc!.Value);
            values =
            [
                chronologicalAge,
                AlbGL!.Value,
                AlpUL!.Value,
                UreaMmolL!.Value,
                CholesterolMmolL!.Value,
                CreatUmolL!.Value,
                CystatinCMgL!.Value,
                Hba1cMmolMol!.Value,
                CrpMgL.Value,
                GgtUL.Value,
                Rbc10e12L!.Value,
                McvFL!.Value,
                RdwPc!.Value,
                monocyteCount,
                neutrophilCount,
                LymPc!.Value,
                AltUL.Value,
                ShbgNmolL.Value,
                VitaminDNmolL.Value,
                GluMmolL!.Value,
                MchPg!.Value,
                ApoA1GL!.Value
            ];
            return true;
        }
    }

    /// <summary>
    /// Result returned by the public Pheno Age calculation endpoint.
    /// </summary>
    /// <param name="BiologicalAge">Calculated Pheno Age in years.</param>
    /// <param name="BiologicalAgeDifference">Signed biological age minus chronological age. Lower and more negative values rank higher within the Amateur field.</param>
    /// <param name="PaceOfAging">Calculated biological age divided by chronological age.</param>
    /// <param name="DomainContributions">Model contribution values used by the website's Pheno Age explanation UI.</param>
    public sealed record PhenoAgeCalculationResult(
        double BiologicalAge,
        double BiologicalAgeDifference,
        double PaceOfAging,
        PhenoAgeDomainContributions DomainContributions);

    /// <summary>
    /// Pheno Age model contribution values grouped by biological domain.
    /// </summary>
    /// <param name="Liver">Albumin and alkaline phosphatase contribution value.</param>
    /// <param name="Kidney">Creatinine contribution value.</param>
    /// <param name="Metabolic">Glucose contribution value.</param>
    /// <param name="Inflammation">C-reactive protein contribution value.</param>
    /// <param name="Immune">White blood cell, lymphocyte, MCV, and RDW contribution value.</param>
    public sealed record PhenoAgeDomainContributions(
        double Liver,
        double Kidney,
        double Metabolic,
        double Inflammation,
        double Immune);

    /// <summary>
    /// Result returned by the public Bortz Age calculation endpoint.
    /// </summary>
    /// <param name="BiologicalAge">Calculated Bortz Age in years.</param>
    /// <param name="BiologicalAgeDifference">Signed biological age minus chronological age. Lower and more negative values rank higher within the Pro field.</param>
    /// <param name="PaceOfAging">Calculated biological age divided by chronological age.</param>
    /// <param name="BiologicalAgeAcceleration">Bortz model biological age acceleration before it is added to chronological age.</param>
    /// <param name="DerivedMonocyteCount10e9L">Monocyte count derived from WBC and monocyte percentage.</param>
    /// <param name="DerivedNeutrophilCount10e9L">Neutrophil count derived from WBC and neutrophil percentage.</param>
    public sealed record BortzAgeCalculationResult(
        double BiologicalAge,
        double BiologicalAgeDifference,
        double PaceOfAging,
        double BiologicalAgeAcceleration,
        double DerivedMonocyteCount10e9L,
        double DerivedNeutrophilCount10e9L);

    internal static class PublicCalculationValidation
    {
        public static bool AllFinite(params double?[] values)
        {
            return values.All(value => value.HasValue && double.IsFinite(value.Value));
        }
    }
}
