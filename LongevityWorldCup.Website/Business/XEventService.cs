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

    public async Task<string?> SendRootTweetAsync(string text, IReadOnlyList<string>? mediaIds = null, bool openPreviewInBrowser = true)
    {
        try
        {
            return await _x.SendTweetAsync(text, mediaIds, null, openPreviewInBrowser);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "X root tweet failed: {Text}", text);
            return null;
        }
    }

    public async Task<string?> SendReplyTweetAsync(string parentTweetId, string text, IReadOnlyList<string>? mediaIds = null, bool openPreviewInBrowser = true)
    {
        try
        {
            return await _x.SendTweetAsync(text, mediaIds, parentTweetId, openPreviewInBrowser);
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
        return await TrySendPvpDuelThreadWithInfoTokenAsync(asOfUtc, null, null, shouldSkipToken);
    }

    public async Task<(bool Sent, string? InfoToken)> TrySendPvpDuelThreadWithInfoTokenAsync(
        DateTime? asOfUtc,
        string? forcedSlugA,
        string? forcedSlugB,
        Func<string, bool>? shouldSkipToken = null)
    {
        var pvp = GetPvp();
        var useForcedPair = !string.IsNullOrWhiteSpace(forcedSlugA) && !string.IsNullOrWhiteSpace(forcedSlugB);
        var battle = useForcedPair
            ? pvp.CreateBattleForPair(asOfUtc, 3, forcedSlugA!, forcedSlugB!)
            : pvp.CreateRandomBattle(asOfUtc, 3);
        if (battle == null) return (false, null);

        var infoToken = BuildPvpInfoToken(battle);
        if (!string.IsNullOrWhiteSpace(infoToken) && shouldSkipToken?.Invoke(infoToken) == true)
            return (false, infoToken);

        var intro = XMessageBuilder.ForPvpBattle(battle, SlugToName);
        if (string.IsNullOrWhiteSpace(intro)) return (false, infoToken);

        var rootMediaIds = await TryUploadPvpRootMediaIdsAsync(battle);
        var rootId = await SendRootTweetAsync(intro, rootMediaIds, openPreviewInBrowser: false);
        if (string.IsNullOrWhiteSpace(rootId)) return (false, infoToken);

        var rounds = XMessageBuilder.ForPvpRounds(battle, SlugToName);
        var scoreA = 0;
        var scoreB = 0;
        for (var i = 0; i < rounds.Count; i++)
        {
            var r = rounds[i];
            if (string.IsNullOrWhiteSpace(r)) continue;

            var winnerSlug = i < battle.Rounds.Count ? battle.Rounds[i].WinnerSlug : null;
            if (string.Equals(winnerSlug, battle.AthleteSlugA, StringComparison.OrdinalIgnoreCase))
                scoreA++;
            else if (string.Equals(winnerSlug, battle.AthleteSlugB, StringComparison.OrdinalIgnoreCase))
                scoreB++;
            else
            {
                scoreA++;
                scoreB++;
            }

            var roundMediaIds = await TryUploadPvpRoundMediaIdsAsync(battle, winnerSlug, scoreA, scoreB);
            await SendReplyTweetAsync(rootId, r, roundMediaIds, openPreviewInBrowser: false);
        }

        var finalText = XMessageBuilder.ForPvpFinal(battle, SlugToName);
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            var finalMediaIds = await TryUploadPvpFinalMediaIdsAsync(battle);
            await SendReplyTweetAsync(rootId, finalText, finalMediaIds, openPreviewInBrowser: true);
        }

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

    private XImageService GetImages()
    {
        return _services.GetRequiredService<XImageService>();
    }

    private async Task<IReadOnlyList<string>?> TryUploadPvpRootMediaIdsAsync(PvpBattleResult battle)
    {
        try
        {
            var images = GetImages();
            await using var imageStream = await images.BuildPvpDuelRootImageAsync(battle.AthleteSlugA, battle.AthleteSlugB);
            return await UploadSinglePngAsync(imageStream);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create/upload PvP root image for {A} vs {B}", battle.AthleteSlugA, battle.AthleteSlugB);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>?> TryUploadPvpRoundMediaIdsAsync(PvpBattleResult battle, string? roundWinnerSlug, int scoreA, int scoreB)
    {
        try
        {
            var images = GetImages();
            await using var imageStream = await images.BuildPvpDuelRoundImageAsync(battle.AthleteSlugA, battle.AthleteSlugB, roundWinnerSlug, scoreA, scoreB);
            return await UploadSinglePngAsync(imageStream);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create/upload PvP round image for {A} vs {B}", battle.AthleteSlugA, battle.AthleteSlugB);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>?> TryUploadPvpFinalMediaIdsAsync(PvpBattleResult battle)
    {
        if (string.IsNullOrWhiteSpace(battle.WinnerSlug))
            return null;

        try
        {
            var images = GetImages();
            await using var imageStream = await images.BuildPvpDuelFinalImageAsync(battle.WinnerSlug);
            return await UploadSinglePngAsync(imageStream);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create/upload PvP final image for winner {Winner}", battle.WinnerSlug);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>?> UploadSinglePngAsync(Stream? imageStream)
    {
        if (imageStream == null)
            return null;

        var mediaId = await _x.UploadMediaAsync(imageStream, "image/png");
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        return new[] { mediaId };
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
