namespace LongevityWorldCup.Website.Business;

/// <summary>
/// Request model for the returning athlete confirmation/update endpoint.
/// Most fields are optional -- null means "unchanged".
/// </summary>
public class AthleteConfirmationData
{
    /// <summary>Identifies the athlete (required).</summary>
    public string? AthleteSlug { get; set; }

    /// <summary>"confirm", "update", or "new_results" (required).</summary>
    public string? Action { get; set; }

    // --- Optional profile fields (null = unchanged) ---
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Division { get; set; }
    public string? Flag { get; set; }
    public string? Why { get; set; }
    public string? MediaContact { get; set; }
    public string? PersonalLink { get; set; }
    public string? AccountEmail { get; set; }

    // --- Optional biomarker / image fields ---
    public List<BiomarkerData>? Biomarkers { get; set; }
    public List<string>? ProofPics { get; set; }
    public string? ProfilePic { get; set; }

    // --- Webhook for status notifications ---
    public string? WebhookUrl { get; set; }
}
