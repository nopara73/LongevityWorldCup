using System.Globalization;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace LongevityWorldCup.Website.Business;

public class ThreadsEventService
{
    private readonly ThreadsApiClient _threads;
    private readonly ILogger<ThreadsEventService> _log;
    private readonly IServiceProvider _services;
    private readonly CustomEventImageService _customEventImages;
    private readonly object _lockObj = new();
    private Dictionary<string, AthleteForX> _bySlug = new(StringComparer.OrdinalIgnoreCase);

    public ThreadsEventService(ThreadsApiClient threads, ILogger<ThreadsEventService> log, IServiceProvider services, CustomEventImageService customEventImages)
    {
        _threads = threads;
        _log = log;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _customEventImages = customEventImages ?? throw new ArgumentNullException(nameof(customEventImages));
    }

    public bool IsConfigured => _threads.IsConfigured;

    public void SetAthletesForThreads(IReadOnlyList<AthleteForX> items)
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
                var postId = await _threads.SendPostAsync(text);
                if (!string.IsNullOrWhiteSpace(postId))
                    return true;

                if (attempt < maxAttempts)
                {
                    _log.LogWarning("Threads send returned no post id, retrying ({Attempt}/{MaxAttempts}): {Text}", attempt, maxAttempts, text);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogWarning("Threads send returned no post id after retries: {Text}", text);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt < maxAttempts)
                {
                    _log.LogWarning(ex, "Threads send failed (attempt {Attempt}/{MaxAttempts}), retrying: {Text}", attempt, maxAttempts, text);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogError(ex, "Threads send failed after retries: {Text}", text);
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
        return await TrySendEventAsync(type, rawText, eventId: null);
    }

    public async Task<bool> TrySendEventAsync(EventType type, string rawText, string? eventId, bool visibleOnWebsite = true)
    {
        if (type == EventType.CustomEvent)
            return await TrySendCustomEventAsync(rawText, eventId, visibleOnWebsite);

        var msg = BuildMessage(type, rawText);
        if (string.IsNullOrWhiteSpace(msg)) return false;
        return await TrySendAsync(msg);
    }

    public string? TryBuildMessage(EventType type, string rawText, string? eventId = null, bool visibleOnWebsite = true)
    {
        if (type == EventType.CustomEvent)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                return null;

            return CustomEventSocialComposer.BuildPlan(eventId, rawText, 500, ResolveMention, includeEventUrl: visibleOnWebsite).PostText;
        }

        var message = ThreadsMessageBuilder.ForEventText(
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
        var message = ThreadsMessageBuilder.ForFiller(
            fillerType,
            payloadText ?? "",
            SlugToName,
            sampleForBasis: BuildSampleSize,
            getFieldSizeForLeague: GetFieldSizeForLeague,
            getBortzFieldSizeForLeague: GetBortzFieldSizeForLeague,
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
        return ThreadsMessageBuilder.ForEventText(
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

    private async Task<bool> TrySendCustomEventAsync(string rawText, string? eventId, bool visibleOnWebsite)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            _log.LogWarning("Threads custom event send skipped because event id was missing.");
            return false;
        }

        var plan = CustomEventSocialComposer.BuildPlan(eventId, rawText, 500, ResolveMention, includeEventUrl: visibleOnWebsite);
        _log.LogInformation(
            "Threads custom event plan for event {EventId}: mode {Mode}, visibleOnWebsite {VisibleOnWebsite}, captionLength {CaptionLength}, titleLength {TitleLength}, bodyLength {BodyLength}",
            eventId,
            plan.Mode,
            visibleOnWebsite,
            plan.PostText.Length,
            plan.TitleText.Length,
            plan.BodyText.Length);

        if (plan.Mode == CustomEventPostMode.Text)
            return await TrySendAsync(plan.PostText);

        if (!_customEventImages.IsConfigured)
        {
            _log.LogWarning("Threads custom event image send skipped because custom event images are not configured for event {EventId}.", eventId);
            return false;
        }

        var imageAsset = await _customEventImages.RenderAsync(eventId, rawText, ResolveMention);
        if (imageAsset is null)
        {
            _log.LogWarning("Threads custom event image render returned no asset for event {EventId}.", eventId);
            return false;
        }

        _log.LogInformation(
            "Threads custom event {EventId} sending image post with imageUrl {ImageUrl} and captionLength {CaptionLength}",
            eventId,
            imageAsset.Value.PublicUrl,
            plan.PostText.Length);

        const int maxAttempts = 2;
        const int retryDelayMs = 750;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var postId = await _threads.SendImagePostAsync(plan.PostText, imageAsset.Value.PublicUrl);
                if (!string.IsNullOrWhiteSpace(postId))
                    return true;

                if (attempt < maxAttempts)
                {
                    _log.LogWarning("Threads image send returned no post id, retrying ({Attempt}/{MaxAttempts}): {Text}", attempt, maxAttempts, plan.PostText);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogWarning("Threads image send returned no post id after retries: {Text}", plan.PostText);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt < maxAttempts)
                {
                    _log.LogWarning(ex, "Threads image send failed (attempt {Attempt}/{MaxAttempts}), retrying: {Text}", attempt, maxAttempts, plan.PostText);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogError(ex, "Threads image send failed after retries: {Text}", plan.PostText);
                return false;
            }
        }

        return false;
    }

    private string SlugToName(string slug)
    {
        lock (_lockObj)
        {
            if (_bySlug.TryGetValue(slug, out var a))
            {
                if (!string.IsNullOrWhiteSpace(a.Name)) return a.Name;
            }
        }
        var spaced = slug.Replace('_', '-').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }

    private string ResolveMention(string slug)
    {
        lock (_lockObj)
        {
            if (_bySlug.TryGetValue(slug, out var athlete))
            {
                var mention = SocialContactParser.TryBuildMention(athlete.MediaContact, SocialPlatform.Threads);
                if (!string.IsNullOrWhiteSpace(mention))
                    return mention;

                if (!string.IsNullOrWhiteSpace(athlete.Name))
                    return athlete.Name;
            }
        }

        return SlugToName(slug);
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

    private int? GetBortzFieldSizeForLeague(string leagueSlug)
    {
        if (string.IsNullOrWhiteSpace(leagueSlug))
            return null;

        return GetAthletes().GetLeagueBortzFieldSize(leagueSlug);
    }

}
