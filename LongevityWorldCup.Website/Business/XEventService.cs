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

    public async Task<string?> SendRootTweetAsync(string text, IReadOnlyList<string>? mediaIds = null)
    {
        try
        {
            return await _x.SendTweetAsync(text, mediaIds, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "X root tweet failed: {Text}", text);
            return null;
        }
    }

    public async Task<string?> SendReplyTweetAsync(string parentTweetId, string text, IReadOnlyList<string>? mediaIds = null)
    {
        try
        {
            return await _x.SendTweetAsync(text, mediaIds, parentTweetId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "X reply tweet failed: {Text}", text);
            return null;
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

    public async Task SendRandomPvpBattleAsync(DateTime? asOfUtc)
    {
        var battle = GetPvp().CreateRandomBattle(asOfUtc, 3);
        if (battle == null) return;
        var text = XMessageBuilder.ForPvpBattle(battle, SlugToName);
        if (string.IsNullOrWhiteSpace(text)) return;
        await SendAsync(text);
    }

    public async Task<bool> TrySendPvpDuelThreadAsync(DateTime? asOfUtc)
    {
        var (sent, _) = await TrySendPvpDuelThreadWithInfoTokenAsync(asOfUtc);
        return sent;
    }

    public async Task<(bool Sent, string? InfoToken)> TrySendPvpDuelThreadWithInfoTokenAsync(DateTime? asOfUtc, Func<string, bool>? shouldSkipToken = null)
    {
        var battle = GetPvp().CreateRandomBattle(asOfUtc, 3);
        if (battle == null) return (false, null);

        var infoToken = BuildPvpInfoToken(battle);
        if (!string.IsNullOrWhiteSpace(infoToken) && shouldSkipToken?.Invoke(infoToken) == true)
            return (false, infoToken);

        var intro = XMessageBuilder.ForPvpBattle(battle, SlugToName);
        if (string.IsNullOrWhiteSpace(intro)) return (false, infoToken);

        var rootId = await SendRootTweetAsync(intro);
        if (string.IsNullOrWhiteSpace(rootId)) return (false, infoToken);

        var rounds = XMessageBuilder.ForPvpRounds(battle, SlugToName);
        foreach (var r in rounds)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            await SendReplyTweetAsync(rootId, r);
        }

        var finalText = XMessageBuilder.ForPvpFinal(battle, SlugToName);
        if (!string.IsNullOrWhiteSpace(finalText))
            await SendReplyTweetAsync(rootId, finalText);

        return (true, infoToken);
    }

    private AthleteDataService GetAthletes()
    {
        return _services.GetRequiredService<AthleteDataService>();
    }

    private PvpBattleService GetPvp()
    {
        return _services.GetRequiredService<PvpBattleService>();
    }

    private static string BuildPvpInfoToken(PvpBattleResult battle)
    {
        static string Norm(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return s.Trim().ToLowerInvariant().Replace(' ', '-');
        }

        var pair = new[] { battle.AthleteSlugA ?? "", battle.AthleteSlugB ?? "" }
            .Select(Norm)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var rounds = battle.Rounds
            .Select(r => Norm(r.BiomarkerName))
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var winner = string.IsNullOrWhiteSpace(battle.WinnerSlug) ? "tie" : Norm(battle.WinnerSlug);
        return $"pair[{string.Join(", ", pair)}] rounds[{string.Join(", ", rounds)}] winner[{winner}]";
    }
}
