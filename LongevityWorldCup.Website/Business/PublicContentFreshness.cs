using LongevityWorldCup.Website.Business.IndexNow;

namespace LongevityWorldCup.Website.Business;

/// <summary>Freshness follows the same public content dependencies as change notifications.</summary>
public sealed class PublicContentFreshness(
    IndexNowContentSnapshot content, ContentRevisionStore revisions, LeaderboardFactsService facts,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private DateTimeOffset? _checkedAt;
    private IReadOnlyDictionary<string, DateTimeOffset?> _dates = new Dictionary<string, DateTimeOffset?>();

    public DateTime? GetLastModifiedUtc(string path)
    {
        // The representation's own revision also drives its HTTP validator.
        if (facts.GetDocumentForPath(path) is { } document) return document.LastModifiedUtc?.UtcDateTime;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_checkedAt is null || now - _checkedAt.Value >= TimeSpan.FromSeconds(10)) RefreshCore(now);
            return _dates.GetValueOrDefault("page:" + SitemapService.SiteBaseUrl + path)?.UtcDateTime;
        }
    }

    public void Refresh()
    {
        lock (_gate) RefreshCore(_clock.GetUtcNow());
    }

    private void RefreshCore(DateTimeOffset now)
    {
        var hashes = content.Build(CancellationToken.None, trackEveryPublicChange: true)
            .ToDictionary(pair => "page:" + pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _dates = revisions.Observe(hashes, now, completePrefix: "page:");
        _checkedAt = now;
    }
}
