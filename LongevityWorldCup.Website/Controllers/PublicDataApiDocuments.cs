using System.Text.Json.Serialization;

namespace LongevityWorldCup.Website.Controllers;

/// <summary>
/// Public athlete record returned by the athlete snapshot endpoint.
/// </summary>
/// <remarks>
/// This documents the stable public fields. The live JSON is produced from hydrated athlete data and can include additional public fields as the competition evolves.
/// </remarks>
public sealed class PublicAthleteApiDocument
{
    /// <summary>Original public athlete name.</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>Preferred display name when it differs from the original public name.</summary>
    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; init; }

    /// <summary>Public media contact URL or handle for attribution and outreach.</summary>
    [JsonPropertyName("MediaContact")]
    public string? MediaContact { get; init; }

    /// <summary>Date of birth used for chronological age calculations and ranking tie-breakers.</summary>
    [JsonPropertyName("DateOfBirth")]
    public PublicDateOfBirthApiDocument? DateOfBirth { get; init; }

    /// <summary>Public biomarker records submitted by the athlete.</summary>
    [JsonPropertyName("Biomarkers")]
    public IReadOnlyList<PublicBiomarkerRecordApiDocument>? Biomarkers { get; init; }

    /// <summary>Competition division, such as Men's, Women's, or Open.</summary>
    [JsonPropertyName("Division")]
    public string? Division { get; init; }

    /// <summary>Public flag or place label selected by the athlete.</summary>
    [JsonPropertyName("Flag")]
    public string? Flag { get; init; }

    /// <summary>Athlete-provided reason for participating when public.</summary>
    [JsonPropertyName("Why")]
    public string? Why { get; init; }

    /// <summary>Optional public personal website or profile link.</summary>
    [JsonPropertyName("PersonalLink")]
    public string? PersonalLink { get; init; }

    /// <summary>Optional public podcast or interview link.</summary>
    [JsonPropertyName("PodcastLink")]
    public string? PodcastLink { get; init; }

    /// <summary>Optional special league assignment, currently used for exclusive leagues such as Prosperan.</summary>
    [JsonPropertyName("ExclusiveLeague")]
    public string? ExclusiveLeague { get; init; }

    /// <summary>Median realistic age guessed by visitors in Guess My Age.</summary>
    [JsonPropertyName("CrowdAge")]
    public double CrowdAge { get; init; }

    /// <summary>Number of accepted realistic crowd age guesses.</summary>
    [JsonPropertyName("CrowdCount")]
    public int CrowdCount { get; init; }

    /// <summary>Whether the athlete was recently added to the public field.</summary>
    [JsonPropertyName("IsNew")]
    public bool IsNew { get; init; }

    /// <summary>Stable slug used by API clients and public athlete URLs.</summary>
    [JsonPropertyName("AthleteSlug")]
    public string? AthleteSlug { get; init; }

    /// <summary>Versioned public profile picture URL when available.</summary>
    [JsonPropertyName("ProfilePic")]
    public string? ProfilePic { get; init; }

    /// <summary>Small generated profile picture thumbnail URL when available.</summary>
    [JsonPropertyName("ProfilePicThumb")]
    public string? ProfilePicThumb { get; init; }

    /// <summary>Leaderboard-sized generated profile picture thumbnail URL when available.</summary>
    [JsonPropertyName("ProfilePicLeaderboardThumb")]
    public string? ProfilePicLeaderboardThumb { get; init; }

    /// <summary>Versioned public proof asset URLs when available.</summary>
    [JsonPropertyName("Proofs")]
    public IReadOnlyList<string>? Proofs { get; init; }

    /// <summary>Computed public badges awarded to the athlete.</summary>
    [JsonPropertyName("Badges")]
    public IReadOnlyList<PublicBadgeApiDocument>? Badges { get; init; }
}

/// <summary>
/// Date of birth object stored as separate year, month, and day fields.
/// </summary>
public sealed class PublicDateOfBirthApiDocument
{
    /// <summary>Four-digit birth year.</summary>
    [JsonPropertyName("Year")]
    public int Year { get; init; }

    /// <summary>Birth month from 1 to 12.</summary>
    [JsonPropertyName("Month")]
    public int Month { get; init; }

    /// <summary>Birth day from 1 to 31.</summary>
    [JsonPropertyName("Day")]
    public int Day { get; init; }
}

/// <summary>
/// One public biomarker record from an athlete's submitted evidence.
/// </summary>
public sealed class PublicBiomarkerRecordApiDocument
{
    /// <summary>Measurement date in ISO date format.</summary>
    [JsonPropertyName("Date")]
    public string? Date { get; init; }

    /// <summary>Albumin in g/L.</summary>
    [JsonPropertyName("AlbGL")]
    public double? AlbGL { get; init; }

    /// <summary>Alkaline phosphatase in U/L.</summary>
    [JsonPropertyName("AlpUL")]
    public double? AlpUL { get; init; }

    /// <summary>Alanine aminotransferase in U/L.</summary>
    [JsonPropertyName("AltUL")]
    public double? AltUL { get; init; }

    /// <summary>Apolipoprotein A1 in g/L.</summary>
    [JsonPropertyName("ApoA1GL")]
    public double? ApoA1GL { get; init; }

    /// <summary>Total cholesterol in mmol/L.</summary>
    [JsonPropertyName("CholesterolMmolL")]
    public double? CholesterolMmolL { get; init; }

    /// <summary>Creatinine in umol/L.</summary>
    [JsonPropertyName("CreatUmolL")]
    public double? CreatUmolL { get; init; }

    /// <summary>C-reactive protein in mg/L.</summary>
    [JsonPropertyName("CrpMgL")]
    public double? CrpMgL { get; init; }

    /// <summary>Cystatin C in mg/L.</summary>
    [JsonPropertyName("CystatinCMgL")]
    public double? CystatinCMgL { get; init; }

    /// <summary>Gamma-glutamyl transferase in U/L.</summary>
    [JsonPropertyName("GgtUL")]
    public double? GgtUL { get; init; }

    /// <summary>Glucose in mmol/L.</summary>
    [JsonPropertyName("GluMmolL")]
    public double? GluMmolL { get; init; }

    /// <summary>HbA1c in mmol/mol.</summary>
    [JsonPropertyName("Hba1cMmolMol")]
    public double? Hba1cMmolMol { get; init; }

    /// <summary>Lymphocyte percentage.</summary>
    [JsonPropertyName("LymPc")]
    public double? LymPc { get; init; }

    /// <summary>Mean corpuscular hemoglobin in pg.</summary>
    [JsonPropertyName("MchPg")]
    public double? MchPg { get; init; }

    /// <summary>Mean corpuscular volume in fL.</summary>
    [JsonPropertyName("McvFL")]
    public double? McvFL { get; init; }

    /// <summary>Monocyte percentage.</summary>
    [JsonPropertyName("MonocytePc")]
    public double? MonocytePc { get; init; }

    /// <summary>Neutrophil percentage.</summary>
    [JsonPropertyName("NeutrophilPc")]
    public double? NeutrophilPc { get; init; }

    /// <summary>Red blood cell count in 10e12/L.</summary>
    [JsonPropertyName("Rbc10e12L")]
    public double? Rbc10e12L { get; init; }

    /// <summary>Red cell distribution width percentage.</summary>
    [JsonPropertyName("RdwPc")]
    public double? RdwPc { get; init; }

    /// <summary>Sex hormone-binding globulin in nmol/L.</summary>
    [JsonPropertyName("ShbgNmolL")]
    public double? ShbgNmolL { get; init; }

    /// <summary>Urea in mmol/L.</summary>
    [JsonPropertyName("UreaMmolL")]
    public double? UreaMmolL { get; init; }

    /// <summary>Vitamin D in nmol/L.</summary>
    [JsonPropertyName("VitaminDNmolL")]
    public double? VitaminDNmolL { get; init; }

    /// <summary>White blood cell count in 1000 cells/uL.</summary>
    [JsonPropertyName("Wbc1000cellsuL")]
    public double? Wbc1000cellsuL { get; init; }
}

/// <summary>
/// Public computed badge awarded to an athlete.
/// </summary>
public sealed class PublicBadgeApiDocument
{
    /// <summary>Human-readable badge label.</summary>
    [JsonPropertyName("BadgeLabel")]
    public string? BadgeLabel { get; init; }

    /// <summary>Badge category or kind when provided by the badge system.</summary>
    [JsonPropertyName("BadgeType")]
    public string? BadgeType { get; init; }

    /// <summary>Placement associated with a ranking badge when applicable.</summary>
    [JsonPropertyName("Place")]
    public int? Place { get; init; }

    /// <summary>Award timestamp or date when provided by the badge system.</summary>
    [JsonPropertyName("AwardedAt")]
    public string? AwardedAt { get; init; }
}

/// <summary>
/// Simple public API error body returned for semantic validation failures.
/// </summary>
public sealed class PublicDataErrorResponse
{
    /// <summary>Human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
