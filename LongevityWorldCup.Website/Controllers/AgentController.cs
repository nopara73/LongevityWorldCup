using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly ApplicationService _appService;
    private readonly AgentApplicationDataService _agentDataService;
    private readonly AthleteDataService _athleteDataService;
    private readonly CycleParticipationDataService _cycleService;
    private readonly SlackEventService _slackEventService;
    private readonly Config _config;
    private readonly ILogger<AgentController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public AgentController(
        ApplicationService appService,
        AgentApplicationDataService agentDataService,
        AthleteDataService athleteDataService,
        CycleParticipationDataService cycleService,
        SlackEventService slackEventService,
        Config config,
        ILogger<AgentController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _appService = appService;
        _agentDataService = agentDataService;
        _athleteDataService = athleteDataService;
        _cycleService = cycleService;
        _slackEventService = slackEventService;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] AgentApplicantData data)
    {
        // Validate required fields
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Trim().Length < 3)
            errors.Add("Name is required and must be at least 3 characters.");

        if (string.IsNullOrWhiteSpace(data.Division) || !Divisions.Division.Contains(data.Division))
            errors.Add($"Division is required and must be one of: {string.Join(", ", Divisions.Division)}");

        if (string.IsNullOrWhiteSpace(data.Flag) || !Flags.Flag.Contains(data.Flag))
            errors.Add("Flag is required and must be a valid flag/country.");

        if (data.Biomarkers == null || data.Biomarkers.Count == 0)
            errors.Add("At least one complete set of biomarkers is required.");

        if (data.ProofPics == null || data.ProofPics.Count == 0)
            errors.Add("At least one proof picture is required.");

        if (string.IsNullOrWhiteSpace(data.ProfilePic))
            errors.Add("A profile picture is required.");

        if (data.DateOfBirth == null)
            errors.Add("Date of birth is required.");

        if (string.IsNullOrWhiteSpace(data.AccountEmail))
            errors.Add("Account email is required.");

        if (errors.Count > 0)
            return BadRequest(new { errors });

        // Check name uniqueness against existing athletes
        var slug = ApplicationService.SanitizeFileName(data.Name!);
        if (FindAthleteBySlug(slug) is not null)
        {
            return BadRequest(new { errors = new[] { $"An athlete with the name '{data.Name}' (or a similar name) already exists." } });
        }

        // Process the application through the shared pipeline
        var (success, error) = await _appService.ProcessApplicationAsync(data);

        if (!success)
        {
            _logger.LogError("Agent application failed for {Name}: {Error}", data.Name, error);
            return StatusCode(500, new { error = SanitizeApplicationError(error, "Application processing failed.") });
        }

        // Create tracking token
        var token = _agentDataService.CreateApplication(data.Name!.Trim(), data.AccountEmail?.Trim(), data.WebhookUrl);

        // Send Slack notification
        try
        {
            await _slackEventService.SendImmediateAsync(
                EventType.CustomEvent,
                $"New agent-submitted application: {data.Name!.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Slack notification for agent application");
        }

        return Ok(new
        {
            token,
            status = "pending",
            message = $"Application for '{data.Name!.Trim()}' submitted successfully. Use the token to check your application status."
        });
    }

    [HttpGet("status/{token}")]
    public IActionResult GetStatus(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Token is required." });

        var result = _agentDataService.GetStatus(token);
        if (result == null)
            return NotFound(new { error = "Application not found for the given token." });

        return Ok(new
        {
            token = result.Value.Token,
            name = result.Value.Name,
            status = result.Value.Status
        });
    }

    [HttpPost("notify")]
    public async Task<IActionResult> Notify([FromBody] AgentNotifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AdminKey) || request.AdminKey != _config.AgentAdminKey)
            return Unauthorized(new { error = "Invalid admin key." });

        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Token is required." });

        if (string.IsNullOrWhiteSpace(request.Status) ||
            (request.Status != "approved" && request.Status != "rejected"))
            return BadRequest(new { error = "Status must be 'approved' or 'rejected'." });

        var (updated, webhookUrl) = _agentDataService.UpdateStatus(request.Token, request.Status);

        if (!updated)
            return NotFound(new { error = "Application not found for the given token." });

        // Fire webhook if configured (best-effort)
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    var statusResult = _agentDataService.GetStatus(request.Token);
                    var payload = JsonSerializer.Serialize(new
                    {
                        token = request.Token,
                        status = request.Status,
                        name = statusResult?.Name
                    });
                    await client.PostAsync(webhookUrl,
                        new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fire webhook for token {Token}", request.Token);
                }
            });
        }

        return Ok(new { token = request.Token, status = request.Status, message = "Status updated." });
    }

    [HttpGet("lookup")]
    public IActionResult Lookup([FromQuery] string name, [FromQuery] string adminKey)
    {
        if (string.IsNullOrWhiteSpace(adminKey) || adminKey != _config.AgentAdminKey)
            return Unauthorized(new { error = "Invalid admin key." });

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Name is required." });

        var result = _agentDataService.LookupByName(name);
        if (result == null)
            return NotFound(new { error = "No agent application found for this name." });

        return Ok(new
        {
            token = result.Value.Token,
            status = result.Value.Status
        });
    }

    private static string SanitizeApplicationError(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        // Don't expose internal server details (parameter names, stack traces, credentials)
        if (raw.Contains("Parameter '", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("SmtpClient", StringComparison.OrdinalIgnoreCase))
        {
            return "Application submission was saved locally but the notification email could not be sent. An administrator will review it shortly.";
        }

        return raw;
    }

    private static void CheckBiomarkerRange(JsonObject entry, string key, string label, double min, double max, string unit, List<string> oor, List<string> missing)
    {
        var node = entry[key];
        if (node is null)
        {
            missing.Add(label);
            return;
        }

        try
        {
            var val = node.GetValue<double>();
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                missing.Add(label);
                return;
            }

            if (val < min)
                oor.Add($"{label} = {val} {unit} is below expected range ({min}-{max}).");
            else if (val > max)
                oor.Add($"{label} = {val} {unit} is above expected range ({min}-{max}).");
        }
        catch
        {
            missing.Add(label);
        }
    }

    private JsonObject? FindAthleteBySlug(string slug)
    {
        var snapshot = _athleteDataService.GetAthletesSnapshot();
        return snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                o["AthleteSlug"]?.GetValue<string>(),
                slug,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// GET /api/agent/athlete/{slug} - Fetch full athlete profile for confirmation screen.
    /// </summary>
    [HttpGet("athlete/{slug}")]
    public IActionResult GetAthlete(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { error = "Athlete slug is required." });

        var athleteNode = FindAthleteBySlug(slug);

        if (athleteNode is null)
            return NotFound(new { error = $"Athlete '{slug}' not found." });

        // Compute PhenoAge stats
        var stats = PhenoStatsCalculator.Compute(athleteNode, DateTime.UtcNow);

        // Build rankings to find current rank
        var rankings = _athleteDataService.GetRankingsOrder();
        int? rank = null;
        var totalRanked = rankings.Count;
        for (int i = 0; i < rankings.Count; i++)
        {
            if (rankings[i] is JsonObject ro &&
                string.Equals(ro["AthleteSlug"]?.GetValue<string>(), slug, StringComparison.OrdinalIgnoreCase))
            {
                rank = i + 1;
                break;
            }
        }

        // Cycle participation
        var currentCycle = CycleParticipationDataService.GetCurrentCycleId();
        var hasParticipated = _cycleService.HasParticipated(currentCycle, slug);

        // Extract biomarkers array
        var biomarkers = athleteNode["Biomarkers"]?.DeepClone();

        // Compute effective display name and data quality hints
        var rawName = athleteNode["Name"]?.GetValue<string>();
        var rawDisplayName = athleteNode["DisplayName"]?.GetValue<string>();
        var effectiveDisplayName = !string.IsNullOrWhiteSpace(rawDisplayName) ? rawDisplayName : rawName;
        var rawFlag = athleteNode["Flag"]?.GetValue<string>();
        var rawWhy = athleteNode["Why"]?.GetValue<string>();
        var rawPersonalLink = athleteNode["PersonalLink"]?.GetValue<string>();
        var rawMediaContact = athleteNode["MediaContact"]?.GetValue<string>();

        // Data quality: identify fields the skill should prompt the user to improve
        var suggestions = new List<string>();
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(rawName) && !rawName.Contains(' '))
            suggestions.Add("Name appears to be a single word. Consider updating with your full name.");
        if (string.IsNullOrWhiteSpace(rawWhy))
            suggestions.Add("The 'Why I compete' statement is empty. Consider adding one.");
        if (string.IsNullOrWhiteSpace(rawPersonalLink))
            suggestions.Add("No personal link on your profile. Consider adding a website or social link.");
        if (stats.SubmissionCount <= 1)
            suggestions.Add("Only 1 biomarker submission on file. Submitting new results can improve your ranking.");

        // Proofs check
        var proofsNode = athleteNode["Proofs"];
        if (proofsNode is null || (proofsNode is JsonArray pa && pa.Count == 0))
            warnings.Add("No proof images on file. Proof of lab results is required for verification.");

        // Positive ageDifference (biologically older)
        if (stats.AgeReduction.HasValue && stats.AgeReduction.Value > 0)
            suggestions.Add("Your biological age is currently higher than your chronological age. Submitting improved results could change this.");

        // Biomarker range validation
        if (athleteNode["Biomarkers"] is JsonArray bioArr)
        {
            var incompleteSets = 0;
            foreach (var entry in bioArr.OfType<JsonObject>())
            {
                var date = entry["Date"]?.GetValue<string>() ?? "unknown";
                var oor = new List<string>();
                var missing = new List<string>();

                CheckBiomarkerRange(entry, "AlbGL", "Albumin", 30, 55, "g/L", oor, missing);
                CheckBiomarkerRange(entry, "CreatUmolL", "Creatinine", 40, 130, "umol/L", oor, missing);
                CheckBiomarkerRange(entry, "GluMmolL", "Glucose", 3, 8, "mmol/L", oor, missing);
                CheckBiomarkerRange(entry, "CrpMgL", "CRP", 0, 20, "mg/L", oor, missing);
                CheckBiomarkerRange(entry, "LymPc", "Lymphocytes", 10, 60, "%", oor, missing);
                CheckBiomarkerRange(entry, "McvFL", "MCV", 70, 110, "fL", oor, missing);
                CheckBiomarkerRange(entry, "RdwPc", "RDW", 10, 20, "%", oor, missing);
                CheckBiomarkerRange(entry, "AlpUL", "ALP", 20, 200, "U/L", oor, missing);
                CheckBiomarkerRange(entry, "Wbc1000cellsuL", "WBC", 2, 15, "10^3/uL", oor, missing);

                if (missing.Count > 0)
                {
                    incompleteSets++;
                    warnings.Add($"Biomarker set ({date}): incomplete -- missing {string.Join(", ", missing)}.");
                }

                foreach (var flag in oor)
                    warnings.Add($"Biomarker set ({date}): {flag}");
            }
        }

        // Build response
        return Ok(new
        {
            athleteSlug = slug,
            name = rawName,
            displayName = rawDisplayName,
            effectiveDisplayName,
            division = stats.Division,
            flag = rawFlag,
            dateOfBirth = athleteNode["DateOfBirth"]?.DeepClone(),
            why = rawWhy,
            mediaContact = rawMediaContact,
            personalLink = rawPersonalLink,
            profilePic = athleteNode["ProfilePic"]?.GetValue<string>(),
            proofs = athleteNode["Proofs"]?.DeepClone(),
            biomarkers,
            phenoAge = stats.LowestPhenoAge.HasValue ? Math.Round(stats.LowestPhenoAge.Value, 1) : (double?)null,
            chronologicalAge = stats.ChronoAge.HasValue ? Math.Round(stats.ChronoAge.Value, 2) : (double?)null,
            ageDifference = stats.AgeReduction.HasValue ? Math.Round(stats.AgeReduction.Value, 2) : (double?)null,
            submissionCount = stats.SubmissionCount,
            currentRank = rank,
            totalRanked,
            currentCycle,
            hasParticipatedThisCycle = hasParticipated,
            suggestions,
            warnings
        });
    }

    /// <summary>
    /// POST /api/agent/confirm - Submit confirmation, update, or new results for a returning athlete.
    /// </summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] AthleteConfirmationData data)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(data.AthleteSlug))
            return BadRequest(new { error = "AthleteSlug is required." });

        if (string.IsNullOrWhiteSpace(data.Action) ||
            (data.Action != "confirm" && data.Action != "update" && data.Action != "new_results"))
            return BadRequest(new { error = "Action must be 'confirm', 'update', or 'new_results'." });

        // Verify athlete exists
        if (FindAthleteBySlug(data.AthleteSlug) is null)
            return NotFound(new { error = $"Athlete '{data.AthleteSlug}' not found." });

        var currentCycle = CycleParticipationDataService.GetCurrentCycleId();
        string? token = null;

        if (data.Action == "confirm")
        {
            // Pure confirmation -- no admin review needed
            _cycleService.RecordParticipation(currentCycle, data.AthleteSlug, "confirmation",
                details: null, agentToken: null);

            return Ok(new
            {
                athleteSlug = data.AthleteSlug,
                action = "confirm",
                cycle = currentCycle,
                message = $"Athlete '{data.AthleteSlug}' confirmed for the {currentCycle} season."
            });
        }

        if (data.Action == "update")
        {
            // Profile field changes -- route through ApplicationService (isEditSubmissionOnly path)
            var applicantData = new AgentApplicantData
            {
                Name = data.Name ?? data.AthleteSlug,
                DisplayName = data.DisplayName,
                Division = data.Division,
                Flag = data.Flag,
                Why = data.Why,
                MediaContact = data.MediaContact,
                PersonalLink = data.PersonalLink,
                AccountEmail = data.AccountEmail,
                WebhookUrl = data.WebhookUrl,
                // Null biomarkers/proofs/DOB triggers isEditSubmissionOnly in ApplicationService
                Biomarkers = null,
                ProofPics = null,
                ProfilePic = data.ProfilePic,
                DateOfBirth = null
            };

            var (success, error) = await _appService.ProcessApplicationAsync(applicantData);
            if (!success)
            {
                _logger.LogError("Agent update failed for {Slug}: {Error}", data.AthleteSlug, error);
                return StatusCode(500, new { error = SanitizeApplicationError(error, "Update processing failed.") });
            }

            token = _agentDataService.CreateApplication(
                applicantData.Name!.Trim(), data.AccountEmail?.Trim(), data.WebhookUrl);

            _cycleService.RecordParticipation(currentCycle, data.AthleteSlug, "data_update",
                details: null, agentToken: token);

            return Ok(new
            {
                athleteSlug = data.AthleteSlug,
                action = "update",
                cycle = currentCycle,
                token,
                message = $"Profile update for '{data.AthleteSlug}' submitted for review."
            });
        }

        // data.Action == "new_results"
        if (data.Biomarkers == null || data.Biomarkers.Count == 0)
            return BadRequest(new { error = "Biomarkers are required for new_results." });

        if (data.ProofPics == null || data.ProofPics.Count == 0)
            return BadRequest(new { error = "Proof pictures are required for new_results." });

        var resultData = new AgentApplicantData
        {
            Name = data.Name ?? data.AthleteSlug,
            DisplayName = data.DisplayName,
            Biomarkers = data.Biomarkers,
            ProofPics = data.ProofPics,
            AccountEmail = data.AccountEmail,
            WebhookUrl = data.WebhookUrl,
            // Null profile fields triggers isResultSubmissionOnly in ApplicationService
            ProfilePic = null,
            DateOfBirth = null,
            MediaContact = null,
            Division = null,
            Flag = null,
            Why = null,
            PersonalLink = null
        };

        var (resultSuccess, resultError) = await _appService.ProcessApplicationAsync(resultData);
        if (!resultSuccess)
        {
            _logger.LogError("Agent new_results failed for {Slug}: {Error}", data.AthleteSlug, resultError);
            return StatusCode(500, new { error = SanitizeApplicationError(resultError, "New results processing failed.") });
        }

        token = _agentDataService.CreateApplication(
            resultData.Name!.Trim(), data.AccountEmail?.Trim(), data.WebhookUrl);

        _cycleService.RecordParticipation(currentCycle, data.AthleteSlug, "new_results",
            details: null, agentToken: token);

        try
        {
            await _slackEventService.SendImmediateAsync(
                EventType.CustomEvent,
                $"Returning athlete new results: {data.AthleteSlug}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Slack notification for returning athlete results");
        }

        return Ok(new
        {
            athleteSlug = data.AthleteSlug,
            action = "new_results",
            cycle = currentCycle,
            token,
            message = $"New results for '{data.AthleteSlug}' submitted for review."
        });
    }

    /// <summary>
    /// GET /api/agent/cycle-stats - Return participation stats for all seasons.
    /// </summary>
    [HttpGet("cycle-stats")]
    public IActionResult GetCycleStats()
    {
        var allStats = _cycleService.GetAllCycleStats();
        var currentCycle = CycleParticipationDataService.GetCurrentCycleId();

        return Ok(new
        {
            currentCycle,
            seasons = allStats.Select(s => new
            {
                s.CycleId,
                s.Total,
                s.NewApplications,
                s.Confirmations,
                s.DataUpdates,
                s.NewResults
            })
        });
    }
}

public class AgentNotifyRequest
{
    public string? Token { get; set; }
    public string? Status { get; set; }
    public string? AdminKey { get; set; }
}
