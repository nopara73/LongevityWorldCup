using System.Globalization;

namespace LongevityWorldCup.Website.Business;

public class FacebookEventService
{
    private readonly FacebookApiClient _facebook;
    private readonly ILogger<FacebookEventService> _log;
    private readonly object _lockObj = new();
    private Dictionary<string, AthleteForX> _bySlug = new(StringComparer.OrdinalIgnoreCase);

    public FacebookEventService(FacebookApiClient facebook, ILogger<FacebookEventService> log)
    {
        _facebook = facebook;
        _log = log;
    }

    public void SetAthletesForFacebook(IReadOnlyList<AthleteForX> items)
    {
        var map = new Dictionary<string, AthleteForX>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (!string.IsNullOrWhiteSpace(i.Slug))
                map[i.Slug] = i;
        }

        lock (_lockObj)
            _bySlug = map;
    }

    public async Task SendAsync(string text)
    {
        _ = await TrySendAsync(text);
    }

    public async Task<bool> TrySendAsync(string text)
    {
        const int maxAttempts = 2;
        const int retryDelayMs = 750;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var postId = await _facebook.SendPostAsync(text);
                if (!string.IsNullOrWhiteSpace(postId))
                    return true;

                if (attempt < maxAttempts)
                {
                    _log.LogWarning("Facebook send returned no post id, retrying ({Attempt}/{MaxAttempts}): {Text}", attempt, maxAttempts, text);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogWarning("Facebook send returned no post id after retries: {Text}", text);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt < maxAttempts)
                {
                    _log.LogWarning(ex, "Facebook send failed (attempt {Attempt}/{MaxAttempts}), retrying: {Text}", attempt, maxAttempts, text);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogError(ex, "Facebook send failed after retries: {Text}", text);
                return false;
            }
        }

        return false;
    }

    public async Task SendEventAsync(EventType type, string rawText)
    {
        _ = await TrySendEventAsync(type, rawText);
    }

    public async Task<bool> TrySendEventAsync(EventType type, string rawText)
    {
        var msg = TryBuildMessage(type, rawText);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        return await TrySendAsync(msg);
    }

    public string? TryBuildMessage(EventType type, string rawText)
    {
        _ = type;
        _ = rawText;
        return null;
    }

    public string? TryBuildFillerMessage(FillerType fillerType, string payloadText)
    {
        _ = fillerType;
        _ = payloadText;
        return null;
    }

    public string SlugToName(string slug)
    {
        lock (_lockObj)
        {
            if (_bySlug.TryGetValue(slug, out var a) && !string.IsNullOrWhiteSpace(a.Name))
                return a.Name;
        }

        var spaced = slug.Replace('_', '-').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }
}
