using System.Text.Json.Serialization;

namespace LongevityWorldCup.Website.Controllers;

/// <summary>
/// Public profile record returned by the athlete or combined leaderboard-profile endpoint.
/// </summary>
/// <remarks>
/// This documents the stable public fields. The live JSON is produced from hydrated approved-athlete data and validated OpenData transcriptions and can include additional public fields as the competition evolves.
/// </remarks>
public sealed class PublicAthleteApiDocument
{
    /// <summary>`Athlete` for an approved applicant or `OpenData` for an unranked public-data profile.</summary>
    [JsonPropertyName("ProfileType")]
    public string? ProfileType { get; init; }

    /// <summary>Required provenance and non-participation disclosure for OpenData profiles; absent for approved athletes.</summary>
    [JsonPropertyName("OpenData")]
    public PublicOpenDataMetadataApiDocument? OpenData { get; init; }

    /// <summary>Original public profile name.</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>Preferred display name when it differs from the original public name.</summary>
    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; init; }

    /// <summary>Public media contact URL or handle for attribution and outreach.</summary>
    [JsonPropertyName("MediaContact")]
    public string? MediaContact { get; init; }

    /// <summary>Date of birth supplied by an approved athlete. OpenData profiles omit this field and publish only age at each blood draw.</summary>
    [JsonPropertyName("DateOfBirth")]
    public PublicDateOfBirthApiDocument? DateOfBirth { get; init; }

    /// <summary>Public biomarker records submitted by an approved athlete or transcribed from the cited sources of an OpenData profile.</summary>
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

    /// <summary>Latest eligible pheno age minus worst eligible pheno age when at least two eligible pheno age submissions exist.</summary>
    [JsonPropertyName("PhenoAgeImprovementFromWorst")]
    public double? PhenoAgeImprovementFromWorst { get; init; }

    /// <summary>Latest eligible bortz age minus worst eligible bortz age when at least two eligible bortz age submissions exist.</summary>
    [JsonPropertyName("BortzAgeImprovementFromWorst")]
    public double? BortzAgeImprovementFromWorst { get; init; }

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
/// One public biomarker record from submitted athlete evidence or cited OpenData bloodwork.
/// </summary>
public sealed class PublicBiomarkerRecordApiDocument
{
    /// <summary>Subject age in years at the source-dated panel. Required for OpenData records so exact date of birth is not republished.</summary>
    [JsonPropertyName("AgeYears")]
    public double? AgeYears { get; init; }

    /// <summary>Source identifiers from the containing OpenData metadata that support this transcription.</summary>
    [JsonPropertyName("SourceIds")]
    public IReadOnlyList<string>? SourceIds { get; init; }

    /// <summary>Source date in ISO format. DateBasis distinguishes collection dates from dated unified reports; month precision uses the first of the published month.</summary>
    [JsonPropertyName("Date")]
    public string? Date { get; init; }

    /// <summary>Optional source precision for an OpenData panel date: Day or Month. Missing means day precision.</summary>
    [JsonPropertyName("DatePrecision")]
    public string? DatePrecision { get; init; }

    /// <summary>
    /// `Collection` when Date is the specimen date, or `Report` when the source dates only one unified laboratory report.
    /// Absent values default to collection-date semantics.
    /// </summary>
    [JsonPropertyName("DateBasis")]
    public string? DateBasis { get; init; }

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

    /// <summary>Optional detection-limit qualifiers keyed by biomarker field; values are &lt; or &gt;.</summary>
    [JsonPropertyName("MeasurementQualifiers")]
    public IReadOnlyDictionary<string, string>? MeasurementQualifiers { get; init; }
}

/// <summary>Disclosure and provenance attached only to an unranked OpenData profile.</summary>
public sealed class PublicOpenDataMetadataApiDocument
{
    /// <summary>Always true: the subject did not apply to or join the competition.</summary>
    [JsonPropertyName("SubjectDidNotApply")]
    public bool SubjectDidNotApply { get; init; }

    /// <summary>Optional public names used by the same subject, included solely to prevent duplicate or applicant-colliding profiles.</summary>
    [JsonPropertyName("Aliases")]
    public IReadOnlyList<string>? Aliases { get; init; }

    /// <summary>Concise, sourced context explaining why this subject meets the public-notability bar.</summary>
    [JsonPropertyName("Notability")]
    public PublicOpenDataNotabilityApiDocument? Notability { get; init; }

    /// <summary>Required licensed portrait attribution and the service-generated, versioned portrait URL.</summary>
    [JsonPropertyName("Portrait")]
    public PublicOpenDataPortraitApiDocument? Portrait { get; init; }

    /// <summary>Date on which the transcription and its sources were last reviewed.</summary>
    [JsonPropertyName("ReviewedAt")]
    public string? ReviewedAt { get; init; }

    /// <summary>Linked sources supporting the subject identity and transcribed bloodwork.</summary>
    [JsonPropertyName("Sources")]
    public IReadOnlyList<PublicOpenDataSourceApiDocument>? Sources { get; init; }

    /// <summary>Source identifiers that establish the subject identity without relying on exact date-of-birth publication.</summary>
    [JsonPropertyName("IdentitySourceIds")]
    public IReadOnlyList<string>? IdentitySourceIds { get; init; }

    /// <summary>Optional concise notes about unit conversion or transcription decisions.</summary>
    [JsonPropertyName("TranscriptionNotes")]
    public IReadOnlyList<string>? TranscriptionNotes { get; init; }
}

/// <summary>Sourced editorial context for an OpenData subject's broad notability.</summary>
public sealed class PublicOpenDataNotabilityApiDocument
{
    /// <summary>Neutral public-facing explanation, limited to 280 characters.</summary>
    [JsonPropertyName("Summary")]
    public string? Summary { get; init; }

    /// <summary>Identifiers of linked Identity sources that support the summary.</summary>
    [JsonPropertyName("SourceIds")]
    public IReadOnlyList<string>? SourceIds { get; init; }
}

/// <summary>Licensed portrait attribution for an OpenData subject.</summary>
public sealed class PublicOpenDataPortraitApiDocument
{
    /// <summary>Absolute HTTPS page that publishes the portrait and its attribution or license context.</summary>
    [JsonPropertyName("SourcePageUrl")]
    public string? SourcePageUrl { get; init; }

    /// <summary>Absolute HTTPS URL of the original image published by the source.</summary>
    [JsonPropertyName("OriginalUrl")]
    public string? OriginalUrl { get; init; }

    /// <summary>Concise author or creator credit supplied by the source.</summary>
    [JsonPropertyName("Author")]
    public string? Author { get; init; }

    /// <summary>Name of the license under which the portrait may be reused.</summary>
    [JsonPropertyName("LicenseName")]
    public string? LicenseName { get; init; }

    /// <summary>Absolute HTTPS URL of the applicable license text.</summary>
    [JsonPropertyName("LicenseUrl")]
    public string? LicenseUrl { get; init; }

    /// <summary>Concise disclosure of edits made to the locally served portrait.</summary>
    [JsonPropertyName("EditNote")]
    public string? EditNote { get; init; }

    /// <summary>Service-generated, content-versioned local URL for the validated 640x640 WebP.</summary>
    [JsonPropertyName("AssetUrl")]
    public string? AssetUrl { get; init; }
}

/// <summary>One external source used by an OpenData profile.</summary>
public sealed class PublicOpenDataSourceApiDocument
{
    /// <summary>Stable identifier referenced by biomarker records.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>Source kind: `Bloodwork` or `Identity`.</summary>
    [JsonPropertyName("Kind")]
    public string? Kind { get; init; }

    /// <summary>Human-readable source title.</summary>
    [JsonPropertyName("Title")]
    public string? Title { get; init; }

    /// <summary>Absolute HTTPS source URL.</summary>
    [JsonPropertyName("Url")]
    public string? Url { get; init; }

    /// <summary>Date on which the source was checked.</summary>
    [JsonPropertyName("AccessedOn")]
    public string? AccessedOn { get; init; }

    /// <summary>
    /// How the subject authorized publication: either self-published or explicitly authorized with linked evidence.
    /// At least one authorized Bloodwork source is required.
    /// </summary>
    [JsonPropertyName("SubjectAuthorization")]
    public PublicOpenDataSubjectAuthorizationApiDocument? SubjectAuthorization { get; init; }

    /// <summary>Whether this is the single source preferred for the profile's primary display link.</summary>
    [JsonPropertyName("PreferredForDisplay")]
    public bool? PreferredForDisplay { get; init; }
}

/// <summary>Evidence that an OpenData source was intentionally made public by its subject.</summary>
public sealed class PublicOpenDataSubjectAuthorizationApiDocument
{
    /// <summary>Authorization kind: `SelfPublished` or `ExplicitlyAuthorized`.</summary>
    [JsonPropertyName("Kind")]
    public string? Kind { get; init; }

    /// <summary>
    /// Absolute HTTPS link to direct authorization evidence. Required only for `ExplicitlyAuthorized` sources.
    /// </summary>
    [JsonPropertyName("EvidenceUrl")]
    public string? EvidenceUrl { get; init; }

    /// <summary>
    /// Concise explanation of the subject's explicit authorization. Required only for `ExplicitlyAuthorized` sources.
    /// </summary>
    [JsonPropertyName("EvidenceNote")]
    public string? EvidenceNote { get; init; }
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
