using LongevityWorldCup.Website.Business.IndexNow;

namespace LongevityWorldCup.Website.Business;

/// <summary>Freshness follows the same public content dependencies as change notifications.</summary>
public sealed class PublicContentFreshness(
    IndexNowContentSnapshot content, ContentRevisionStore revisions, LeaderboardFactsService facts,
    TimeProvider? timeProvider = null, ILogger<PublicContentFreshness>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private int _scanRequested = 1;
    private IReadOnlyDictionary<string, DateTimeOffset?> _dates = revisions.GetChangeDates("page:");

    public DateTime? GetLastModifiedUtc(string path)
    {
        // The representation's own revision also drives its HTTP validator.
        if (facts.GetDocumentForPath(path) is { } document) return document.LastModifiedUtc?.UtcDateTime;
        var url = SitemapService.SiteBaseUrl + new PathString(path).ToUriComponent();
        if (!IndexNowContentSnapshot.IsCanonicalUrl(url)) return null;
        Interlocked.Exchange(ref _scanRequested, 1);
        // Rendering never waits on a site-wide proof/template/event scan. Until
        // the background observation completes, retain the last verified date.
        return Volatile.Read(ref _dates).GetValueOrDefault("page:" + url)?.UtcDateTime;
    }

    public void Refresh()
    {
        lock (_gate) RefreshCore(CancellationToken.None);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (Interlocked.Exchange(ref _scanRequested, 0) == 0) continue;
                try
                {
                    lock (_gate) RefreshCore(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _scanRequested, 1);
                    logger?.LogError(ex, "Public content freshness scan failed; retaining the previous verified dates.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void RefreshCore(CancellationToken cancellationToken)
    {
        var hashes = content.Build(cancellationToken, trackEveryPublicChange: true)
            .ToDictionary(pair => "page:" + pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Volatile.Write(ref _dates, revisions.Observe(hashes, _clock.GetUtcNow(), completePrefix: "page:"));
    }
}
