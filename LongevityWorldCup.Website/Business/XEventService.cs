using System.Globalization;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public class XEventService
{
    private readonly XApiClient _x;
    private readonly ILogger<XEventService> _log;
    private readonly object _lockObj = new();
    private Dictionary<string, (string Name, int? Rank)> _athDir = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _podcastBySlug = new(StringComparer.OrdinalIgnoreCase);

    public XEventService(XApiClient x, ILogger<XEventService> log)
    {
        _x = x;
        _log = log;
    }

    public void SetAthleteDirectory(IReadOnlyList<(string Slug, string Name, int? CurrentRank)> items)
    {
        var map = new Dictionary<string, (string Name, int? Rank)>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items) map[i.Slug] = (i.Name, i.CurrentRank);
        lock (_lockObj) _athDir = map;
    }

    public void SetPodcastLinks(IReadOnlyList<(string Slug, string PodcastLink)> items)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (!string.IsNullOrWhiteSpace(i.PodcastLink)) map[i.Slug] = i.PodcastLink.Trim();
        }
        lock (_lockObj) _podcastBySlug = map;
    }

    public async Task SendAsync(string text)
    {
        try
        {
            await _x.SendAsync(text);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "X send failed: {Text}", text);
        }
    }

    public async Task SendEventAsync(EventType type, string rawText)
    {
        var msg = BuildMessage(type, rawText);
        if (string.IsNullOrWhiteSpace(msg)) return;
        await SendAsync(msg);
    }

    private string BuildMessage(EventType type, string rawText)
    {
        return XMessageBuilder.ForEventText(type, rawText, SlugToName, GetPodcast);
    }

    private string SlugToName(string slug)
    {
        lock (_lockObj)
        {
            if (_athDir.TryGetValue(slug, out var v) && !string.IsNullOrWhiteSpace(v.Name))
                return v.Name;
        }
        var spaced = slug.Replace('_', '-').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }

    private string? GetPodcast(string slug)
    {
        lock (_lockObj)
        {
            return _podcastBySlug.TryGetValue(slug, out var url) ? url : null;
        }
    }
}
