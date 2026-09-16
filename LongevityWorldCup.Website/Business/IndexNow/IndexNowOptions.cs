namespace LongevityWorldCup.Website.Business.IndexNow;

public sealed class IndexNowOptions
{
    public bool Enabled { get; set; }
    public int BatchSize { get; set; } = 500;
    // Increment after correcting a permanent protocol/configuration error to resume retained work.
    public string RetryVersion { get; set; } = "";

    public const string Endpoint = "https://api.indexnow.org/indexnow";
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MinimumUrlInterval = TimeSpan.FromMinutes(5);
    public static bool CanSubmit(IHostEnvironment environment, IndexNowOptions options) =>
        environment.IsProduction() && options.Enabled;
}
