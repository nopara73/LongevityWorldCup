namespace LongevityWorldCup.Website.Controllers;

public sealed class PublicAthleteApiDocument
{
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? MediaContact { get; init; }
    public PublicDateOfBirthApiDocument? DateOfBirth { get; init; }
    public IReadOnlyList<PublicBiomarkerRecordApiDocument>? Biomarkers { get; init; }
    public string? Division { get; init; }
    public string? Flag { get; init; }
    public string? Why { get; init; }
    public string? PersonalLink { get; init; }
    public string? PodcastLink { get; init; }
    public string? ExclusiveLeague { get; init; }
    public double CrowdAge { get; init; }
    public int CrowdCount { get; init; }
    public bool IsNew { get; init; }
    public string? AthleteSlug { get; init; }
    public string? ProfilePic { get; init; }
    public string? ProfilePicThumb { get; init; }
    public string? ProfilePicLeaderboardThumb { get; init; }
    public IReadOnlyList<string>? Proofs { get; init; }
    public IReadOnlyList<PublicBadgeApiDocument>? Badges { get; init; }
}

public sealed class PublicDateOfBirthApiDocument
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Day { get; init; }
}

public sealed class PublicBiomarkerRecordApiDocument
{
    public string? Date { get; init; }
    public double? AlbGL { get; init; }
    public double? AlpUL { get; init; }
    public double? AltUL { get; init; }
    public double? ApoA1GL { get; init; }
    public double? CholesterolMmolL { get; init; }
    public double? CreatUmolL { get; init; }
    public double? CrpMgL { get; init; }
    public double? CystatinCMgL { get; init; }
    public double? GgtUL { get; init; }
    public double? GluMmolL { get; init; }
    public double? Hba1cMmolMol { get; init; }
    public double? LymPc { get; init; }
    public double? MchPg { get; init; }
    public double? McvFL { get; init; }
    public double? MonocytePc { get; init; }
    public double? NeutrophilPc { get; init; }
    public double? Rbc10e12L { get; init; }
    public double? RdwPc { get; init; }
    public double? ShbgNmolL { get; init; }
    public double? UreaMmolL { get; init; }
    public double? VitaminDNmolL { get; init; }
    public double? Wbc1000cellsuL { get; init; }
}

public sealed class PublicBadgeApiDocument
{
    public string? BadgeLabel { get; init; }
    public string? BadgeType { get; init; }
    public int? Place { get; init; }
    public string? AwardedAt { get; init; }
}
