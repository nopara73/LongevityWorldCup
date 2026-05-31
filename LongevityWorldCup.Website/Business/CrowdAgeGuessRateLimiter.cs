namespace LongevityWorldCup.Website.Business;

public sealed class CrowdAgeGuessRateLimiter
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _window;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _acceptedUntilByKey = new(StringComparer.Ordinal);
    private int _operationCount;

    public CrowdAgeGuessRateLimiter()
        : this(DefaultWindow)
    {
    }

    public CrowdAgeGuessRateLimiter(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        _window = window;
    }

    public bool TryAccept(string clientIdentifier, string athleteSlug, DateTimeOffset nowUtc, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(clientIdentifier))
            clientIdentifier = "unknown";
        if (string.IsNullOrWhiteSpace(athleteSlug))
            return false;

        var key = BuildKey(clientIdentifier, athleteSlug);

        lock (_lock)
        {
            _operationCount++;
            if (_operationCount % 256 == 0)
                RemoveExpiredEntries(nowUtc);

            if (_acceptedUntilByKey.TryGetValue(key, out var acceptedUntil) && acceptedUntil > nowUtc)
            {
                retryAfter = acceptedUntil - nowUtc;
                return false;
            }

            _acceptedUntilByKey[key] = nowUtc.Add(_window);
            return true;
        }
    }

    public bool TryAccept(string clientIdentifier, string athleteSlug, out TimeSpan retryAfter)
        => TryAccept(clientIdentifier, athleteSlug, DateTimeOffset.UtcNow, out retryAfter);

    private static string BuildKey(string clientIdentifier, string athleteSlug)
        => string.Join(
            '|',
            clientIdentifier.Trim().ToUpperInvariant(),
            athleteSlug.Trim().Replace('-', '_').ToUpperInvariant());

    private void RemoveExpiredEntries(DateTimeOffset nowUtc)
    {
        var expiredKeys = _acceptedUntilByKey
            .Where(kv => kv.Value <= nowUtc)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
            _acceptedUntilByKey.Remove(key);
    }
}
