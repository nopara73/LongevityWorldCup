using System.Globalization;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public class FacebookEventService
{
    private readonly FacebookApiClient _facebook;
    private readonly ILogger<FacebookEventService> _log;
    private readonly CustomEventImageService _customEventImages;
    private readonly object _lockObj = new();
    private Dictionary<string, AthleteForX> _bySlug = new(StringComparer.OrdinalIgnoreCase);

    public FacebookEventService(FacebookApiClient facebook, ILogger<FacebookEventService> log, CustomEventImageService customEventImages)
    {
        _facebook = facebook;
        _log = log;
        _customEventImages = customEventImages ?? throw new ArgumentNullException(nameof(customEventImages));
    }

    public bool IsConfigured => _facebook.IsConfigured;

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
        return await TrySendEventAsync(type, rawText, eventId: null);
    }

    public async Task<bool> TrySendEventAsync(EventType type, string rawText, string? eventId, bool visibleOnWebsite = true)
    {
        if (type != EventType.CustomEvent || string.IsNullOrWhiteSpace(eventId))
        {
            _log.LogWarning("Facebook event send skipped because only custom events with an event id are supported. Type: {EventType}, EventIdPresent: {HasEventId}", type, !string.IsNullOrWhiteSpace(eventId));
            return false;
        }

        var plan = CustomEventSocialComposer.BuildPlan(eventId, rawText, 63206, ResolveMention, includeEventUrl: visibleOnWebsite);
        _log.LogInformation(
            "Facebook custom event plan for event {EventId}: mode {Mode}, visibleOnWebsite {VisibleOnWebsite}, postLength {PostLength}, titleLength {TitleLength}, bodyLength {BodyLength}",
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
            _log.LogWarning("Facebook custom event image send skipped because custom event images are not configured for event {EventId}.", eventId);
            return false;
        }

        var imageAsset = await _customEventImages.RenderAsync(eventId, rawText, ResolveMention);
        if (imageAsset is null)
        {
            _log.LogWarning("Facebook custom event image render returned no asset for event {EventId}.", eventId);
            return false;
        }

        _log.LogInformation(
            "Facebook custom event {EventId} sending image post with imageUrl {ImageUrl} and postLength {PostLength}",
            eventId,
            imageAsset.Value.PublicUrl,
            plan.PostText.Length);

        const int maxAttempts = 2;
        const int retryDelayMs = 750;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var postId = await _facebook.SendPhotoPostAsync(plan.PostText, imageAsset.Value.PublicUrl);
                if (!string.IsNullOrWhiteSpace(postId))
                    return true;

                if (attempt < maxAttempts)
                {
                    _log.LogWarning("Facebook image send returned no post id, retrying ({Attempt}/{MaxAttempts}): {Text}", attempt, maxAttempts, plan.PostText);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogWarning("Facebook image send returned no post id after retries: {Text}", plan.PostText);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt < maxAttempts)
                {
                    _log.LogWarning(ex, "Facebook image send failed (attempt {Attempt}/{MaxAttempts}), retrying: {Text}", attempt, maxAttempts, plan.PostText);
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                _log.LogError(ex, "Facebook image send failed after retries: {Text}", plan.PostText);
                return false;
            }
        }

        return false;
    }

    public string? TryBuildMessage(EventType type, string rawText, string? eventId = null, bool visibleOnWebsite = true)
    {
        if (type != EventType.CustomEvent || string.IsNullOrWhiteSpace(eventId))
            return null;

        return CustomEventSocialComposer.BuildPlan(eventId, rawText, 63206, ResolveMention, includeEventUrl: visibleOnWebsite).PostText;
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

    private string ResolveMention(string slug)
    {
        lock (_lockObj)
        {
            if (_bySlug.TryGetValue(slug, out var athlete) && !string.IsNullOrWhiteSpace(athlete.Name))
                return athlete.Name;
        }

        return SlugToName(slug);
    }
}
