namespace LongevityWorldCup.Website.Tools;

public static class PublicRequestTimeoutPolicies
{
    public const string PublicWork = "public-work";
    public const string ApplicationSubmission = "application-submission";
    public static readonly TimeSpan PublicWorkTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ApplicationSubmissionTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ApplicationSubmissionWorkTimeout = TimeSpan.FromSeconds(270);
}
