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
        _ = await TrySendAsync(text, null);
    }

    public async Task SendAsync(string text, IReadOnlyList<string>? mediaIds)
    {
        _ = await TrySendAsync(text, mediaIds);
    }

    public async Task<bool> TrySendAsync(string text)
    {
        return await TrySendAsync(text, null);
    }

    public async Task<bool> TrySendAsync(string text, IReadOnlyList<string>? mediaIds)
    {
        const int maxAttempts = 2;
        const int retryDelayMs = 750;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var tweetId = await _x.SendTweetAsync(text, mediaIds, null, true);
                if (!string.IsNullOrWhiteSpace(tweetId))
                    return true;

                if (attempt < maxAttempts)
                {
                    _log.LogWarning("X send returned no tweet id, retrying ({Attempt}/{MaxAttempts}): {Text}", attempt, maxAttempts, text);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogWarning("X send returned no tweet id after retries: {Text}", text);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt < maxAttempts)
                {
                    _log.LogWarning(ex, "X send failed (attempt {Attempt}/{MaxAttempts}), retrying: {Text}", attempt, maxAttempts, text);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogError(ex, "X send failed after retries: {Text}", text);
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
        var msg = BuildMessage(type, rawText);
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return await TrySendAsync(msg);
    }

    public string? TryBuildMessage(EventType type, string rawText)
    {
        var message = XMessageBuilder.ForEventText(
            type,
            rawText,
            SlugToName,
            sampleForBasis: BuildSampleSize,
            getFieldSizeForLeague: GetFieldSizeForLeague,
            getPodcastLinkForSlug: GetPodcast,
            getLowestPhenoAgeForSlug: GetLowestPhenoAge,
            getLowestBortzAgeForSlug: GetLowestBortzAge,
            getChronoAgeForSlug: GetChronoAge,
            getPhenoDiffForSlug: GetPhenoDiff,
            getBortzDiffForSlug: GetBortzDiff);
        if (string.IsNullOrWhiteSpace(message))
            return null;

        return message;
    }

    public string? TryBuildFillerMessage(FillerType fillerType, string payloadText)
    {
        var athletes = GetAthletes();
        var message = XMessageBuilder.ForFiller(
            fillerType,
            payloadText ?? "",
            SlugToName,
            sampleForBasis: BuildSampleSize,
            getFieldSizeForLeague: GetFieldSizeForLeague,
            getTop3SlugsForLeague: athletes.GetTop3SlugsForLeague,
            getCrowdLowestAgePodium: athletes.GetCrowdLowestAgeBadgePodiumForX,
            getRecentNewcomersForX: athletes.GetRecentNewcomersForX,
            getBestDomainWinnerSlug: athletes.GetBestDomainWinnerSlug);
        if (string.IsNullOrWhiteSpace(message))
            return null;

        return message;
    }

    private string BuildMessage(EventType type, string rawText)
    {
        return XMessageBuilder.ForEventText(
            type,
            rawText,
            SlugToName,
            getFieldSizeForLeague: GetFieldSizeForLeague,
            getPodcastLinkForSlug: GetPodcast,
            getLowestPhenoAgeForSlug: GetLowestPhenoAge,
            getLowestBortzAgeForSlug: GetLowestBortzAge,
            getChronoAgeForSlug: GetChronoAge,
            getPhenoDiffForSlug: GetPhenoDiff,
            getBortzDiffForSlug: GetBortzDiff);
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

    private XPostSampleSize BuildSampleSize(XPostSampleBasis basis)
    {
        var counts = GetSampleCounts();
        var n = basis switch
        {
            XPostSampleBasis.PhenoAge => counts.pheno,
            XPostSampleBasis.Bortz => counts.bortz,
            XPostSampleBasis.Combined => counts.combined,
            _ => counts.combined
        };

        return new XPostSampleSize(
            Basis: basis,
            N: n,
            PhenoCount: counts.pheno,
            BortzCount: counts.bortz,
            CombinedCount: counts.combined);
    }

    private (int pheno, int bortz, int combined) GetSampleCounts()
    {
        AthleteForX[] snapshot;
        lock (_lockObj)
            snapshot = _bySlug.Values.ToArray();

        var pheno = 0;
        var bortz = 0;
        var combined = 0;

        foreach (var athlete in snapshot)
        {
            var hasPheno = athlete.LowestPhenoAge.HasValue;
            var hasBortz = athlete.LowestBortzAge.HasValue;

            if (hasPheno) pheno++;
            if (hasBortz) bortz++;
            if (hasPheno || hasBortz) combined++;
        }

        return (pheno, bortz, combined);
    }

    private int? GetFieldSizeForLeague(string leagueSlug)
    {
        if (string.IsNullOrWhiteSpace(leagueSlug))
            return null;

        return GetAthletes().GetLeagueFieldSize(leagueSlug);
    }

}
