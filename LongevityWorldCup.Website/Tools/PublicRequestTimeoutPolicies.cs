namespace LongevityWorldCup.Website.Tools;

public static class PublicRequestTimeoutPolicies
{
    public const string PublicWork = "public-work";
    public static readonly TimeSpan PublicWorkTimeout = TimeSpan.FromSeconds(60);
}
