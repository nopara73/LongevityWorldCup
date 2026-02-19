using System.Globalization;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace LongevityWorldCup.Website.Business;

public class XEventService
{
    private readonly XApiClient _x;
    private readonly ILogger<XEventService> _log;
    private readonly IServiceProvider _services;
    private readonly object _lockObj = new();
    private Dictionary<string, AthleteForX> _bySlug = new(StringComparer.OrdinalIgnoreCase);

    public XEventService(XApiClient x, ILogger<XEventService> log, IServiceProvider services)
    {
        _x = x;
        _log = log;
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void SetAthletesForX(IReadOnlyList<AthleteForX> items)
    {
        var map = new Dictionary<string, AthleteForX>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (!string.IsNullOrWhiteSpace(i.Slug)) map[i.Slug] = i;
        }
        lock (_lockObj) _bySlug = map;
    }

    public async Task SendAsync(string text)
    {
        await SendAsync(text, null);
    }

    public async Task SendAsync(string text, IReadOnlyList<string>? mediaIds)
    {
        try
        {
            await _x.SendAsync(text, mediaIds);
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

    public string? TryBuildFillerMessage(FillerType fillerType, string payloadText)
    {
        var athletes = GetAthletes();
        var msg = XMessageBuilder.ForFiller(
            fillerType,
            payloadText ?? "",
            SlugToName,
            athletes.GetTop3SlugsForLeague,
            athletes.GetCrowdLowestAgeTop3,
            athletes.GetRecentNewcomersForX,
            athletes.GetBestDomainWinnerSlug);
        return string.IsNullOrWhiteSpace(msg) ? null : msg;
    }

    private string BuildMessage(EventType type, string rawText)
    {
        return XMessageBuilder.ForEventText(type, rawText, SlugToName, GetPodcast, GetLowestPhenoAge, GetLowestBortzAge, GetChronoAge, GetPhenoDiff, GetBortzDiff);
    }

    private string SlugToName(string slug)
    {
        lock (_lockObj)
        {
            if (_bySlug.TryGetValue(slug, out var a))
            {
                if (!string.IsNullOrWhiteSpace(a.XHandle)) return a.XHandle;
                if (!string.IsNullOrWhiteSpace(a.Name)) return a.Name;
            }
        }
        var spaced = slug.Replace('_', '-').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }

    private string? GetPodcast(string slug)
    {
        lock (_lockObj)
            return _bySlug.TryGetValue(slug, out var a) ? a.PodcastLink : null;
    }

    private double? GetLowestPhenoAge(string slug)
    {
        lock (_lockObj)
            return _bySlug.TryGetValue(slug, out var a) ? a.LowestPhenoAge : null;
    }

    private double? GetChronoAge(string slug)
    {
        lock (_lockObj)
            return _bySlug.TryGetValue(slug, out var a) ? a.ChronoAge : null;
    }

    private double? GetPhenoDiff(string slug)
    {
        lock (_lockObj)
            return _bySlug.TryGetValue(slug, out var a) ? a.PhenoAgeDiffFromBaseline : null;
    }

    private double? GetLowestBortzAge(string slug)
    {
        lock (_lockObj)
            return _bySlug.TryGetValue(slug, out var a) ? a.LowestBortzAge : null;
    }

    private double? GetBortzDiff(string slug)
    {
        lock (_lockObj)
            return _bySlug.TryGetValue(slug, out var a) ? a.BortzAgeDiffFromBaseline : null;
    }

    private AthleteDataService GetAthletes()
    {
        return _services.GetRequiredService<AthleteDataService>();
    }

}
