using System.Globalization;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public class XEventService
{
    private readonly XApiClient _x;
    private readonly ILogger<XEventService> _log;
    private readonly AthleteDataService _athletes;
    private readonly PvpBattleService _pvp;
    private readonly object _lockObj = new();
    private Dictionary<string, AthleteForX> _bySlug = new(StringComparer.OrdinalIgnoreCase);

    public XEventService(XApiClient x, ILogger<XEventService> log, AthleteDataService athletes, PvpBattleService pvp)
    {
        _x = x;
        _log = log;
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _pvp = pvp ?? throw new ArgumentNullException(nameof(pvp));
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
        var msg = XMessageBuilder.ForFiller(
            fillerType,
            payloadText ?? "",
            SlugToName,
            _athletes.GetTop3SlugsForLeague,
            _athletes.GetCrowdLowestAgeTop3,
            _athletes.GetRecentNewcomersForX,
            _athletes.GetBestDomainWinnerSlug);
        return string.IsNullOrWhiteSpace(msg) ? null : msg;
    }

    private string BuildMessage(EventType type, string rawText)
    {
        return XMessageBuilder.ForEventText(type, rawText, SlugToName, GetPodcast, GetLowestPhenoAge, GetChronoAge, GetPhenoDiff);
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

    public async Task SendRandomPvpBattleAsync(DateTime? asOfUtc, int biomarkerCount)
    {
        var battle = _pvp.CreateRandomBattle(asOfUtc, biomarkerCount);
        if (battle == null) return;
        var text = XMessageBuilder.ForPvpBattle(battle, SlugToName);
        if (string.IsNullOrWhiteSpace(text)) return;
        await SendAsync(text);
    }
}
