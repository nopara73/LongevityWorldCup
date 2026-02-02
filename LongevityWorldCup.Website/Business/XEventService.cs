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
    private Dictionary<string, string> _handlesBySlug = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, double> _phenoBySlug = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, double> _chronoBySlug = new(StringComparer.OrdinalIgnoreCase);

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

    public void SetXHandles(IReadOnlyList<(string Slug, string Handle)> items)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (!string.IsNullOrWhiteSpace(i.Handle)) map[i.Slug] = i.Handle.Trim();
        }
        lock (_lockObj) _handlesBySlug = map;
    }

    public void SetLowestPhenoAges(IReadOnlyList<(string Slug, double? LowestPhenoAge)> items)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (i.LowestPhenoAge.HasValue) map[i.Slug] = i.LowestPhenoAge.Value;
        }
        lock (_lockObj) _phenoBySlug = map;
    }

    public void SetChronoAges(IReadOnlyList<(string Slug, double? ChronoAge)> items)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (i.ChronoAge.HasValue) map[i.Slug] = i.ChronoAge.Value;
        }
        lock (_lockObj) _chronoBySlug = map;
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

    public string? TryBuildMessage(EventType type, string rawText)
    {
        var msg = BuildMessage(type, rawText);
        return string.IsNullOrWhiteSpace(msg) ? null : msg;
    }

    private string BuildMessage(EventType type, string rawText)
    {
        return XMessageBuilder.ForEventText(type, rawText, SlugToName, GetPodcast, GetLowestPhenoAge, GetChronoAge);
    }

    private string SlugToName(string slug)
    {
        lock (_lockObj)
        {
            if (_handlesBySlug.TryGetValue(slug, out var handle) && !string.IsNullOrWhiteSpace(handle))
                return handle;
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

    private double? GetLowestPhenoAge(string slug)
    {
        lock (_lockObj)
        {
            return _phenoBySlug.TryGetValue(slug, out var age) ? age : null;
        }
    }

    private double? GetChronoAge(string slug)
    {
        lock (_lockObj)
        {
            return _chronoBySlug.TryGetValue(slug, out var age) ? age : null;
        }
    }
}
