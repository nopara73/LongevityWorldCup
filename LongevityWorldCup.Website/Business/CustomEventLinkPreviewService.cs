using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public sealed record CustomEventLinkPreview(
    string Url,
    string Domain,
    string Title,
    string Description,
    string Image);

public sealed class CustomEventLinkPreviewService(IHttpClientFactory httpClientFactory, ILogger<CustomEventLinkPreviewService> log, YouTubePreviewService youtube)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<CustomEventLinkPreviewService> _log = log;

    public async Task<CustomEventLinkPreview?> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!TryNormalizeHttpUrl(url, out var normalizedUrl, out _))
            return null;

        var videoId = YouTubePreviewService.TryGetVideoId(normalizedUrl);
        if (videoId is not null && await youtube.FetchAsync(videoId, ct) is { } preview)
            return new(normalizedUrl, "YouTube", preview.Title, preview.AuthorName, preview.ThumbnailUrl);
        return await TryFetchMicrolinkAsync(normalizedUrl, ct);
    }

    private async Task<CustomEventLinkPreview?> TryFetchMicrolinkAsync(string url, CancellationToken ct)
    {
        try
        {
            var endpoint = $"https://api.microlink.io/?url={Uri.EscapeDataString(url)}&audio=false&video=false&screenshot=false&force=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClientFactory.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogDebug("Microlink preview fetch failed for {Url}: {StatusCode}", url, response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("data", out var data))
                return null;

            var domain = FirstNonEmpty(
                GetNestedString(data, "publisher"),
                GetNestedString(data, "author"),
                GetDomain(url));
            var title = FirstNonEmpty(GetNestedString(data, "title"), GetDomain(url));
            var description = GetNestedString(data, "description") ?? "";
            var image = FirstNonEmpty(
                GetNestedString(data, "image", "url"),
                GetNestedString(data, "logo", "url"),
                "");

            return new CustomEventLinkPreview(url, domain, title, description, image);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Microlink preview fetch failed for {Url}", url);
            return null;
        }
    }

    private static bool TryNormalizeHttpUrl(string? value, out string normalizedUrl, out Uri? uri)
    {
        normalizedUrl = "";
        uri = null;

        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 2048)
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;

        normalizedUrl = parsed.ToString();
        uri = parsed;
        return true;
    }

    private static string GetDomain(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : url;
    }

    private static string? GetNestedString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()?.Trim()
            : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

}
