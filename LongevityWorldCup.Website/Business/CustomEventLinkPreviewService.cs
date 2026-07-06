using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace LongevityWorldCup.Website.Business;

public sealed record CustomEventLinkPreview(
    string Url,
    string Domain,
    string Title,
    string Description,
    string Image);

public sealed class CustomEventLinkPreviewService(IHttpClientFactory httpClientFactory, ILogger<CustomEventLinkPreviewService> log)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<CustomEventLinkPreviewService> _log = log;

    public async Task<CustomEventLinkPreview?> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!TryNormalizeHttpUrl(url, out var normalizedUrl, out _))
            return null;

        var youtubeVideoId = TryGetYouTubeVideoId(normalizedUrl);
        var microlink = await TryFetchMicrolinkAsync(normalizedUrl, ct);
        if (IsUsablePreview(microlink))
            return microlink;

        if (!string.IsNullOrWhiteSpace(youtubeVideoId))
        {
            var youtube = await TryFetchYouTubeOEmbedAsync(normalizedUrl, youtubeVideoId, ct);
            if (IsUsablePreview(youtube))
                return youtube;
        }

        return microlink;
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

    private async Task<CustomEventLinkPreview?> TryFetchYouTubeOEmbedAsync(string originalUrl, string videoId, CancellationToken ct)
    {
        try
        {
            var canonicalUrl = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
            var endpoint = $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(canonicalUrl)}&format=json";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClientFactory.CreateClient().SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogDebug("YouTube oEmbed preview fetch failed for {Url}: {StatusCode}", originalUrl, response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var data = await JsonSerializer.DeserializeAsync<YouTubeOEmbedResponse>(stream, JsonOptions, ct);
            if (data is null)
                return null;

            var title = FirstNonEmpty(data.Title, "YouTube video");
            var domain = FirstNonEmpty(data.ProviderName, "YouTube");
            var description = data.AuthorName ?? "";
            var image = FirstNonEmpty(data.ThumbnailUrl, $"https://i.ytimg.com/vi/{Uri.EscapeDataString(videoId)}/hqdefault.jpg");

            return new CustomEventLinkPreview(originalUrl, domain, title, description, image);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "YouTube oEmbed preview fetch failed for {Url}", originalUrl);
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

    private static string? TryGetYouTubeVideoId(string url)
    {
        if (!TryNormalizeHttpUrl(url, out _, out var parsed) || parsed is null)
            return null;

        var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? parsed.Host[4..]
            : parsed.Host;
        host = host.ToLowerInvariant();

        if (host == "youtu.be")
        {
            var id = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return IsYouTubeVideoId(id) ? id : null;
        }

        if (host is "youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            var query = QueryHelpers.ParseQuery(parsed.Query);
            var fromQuery = query.TryGetValue("v", out var values) ? values.FirstOrDefault() : null;
            if (IsYouTubeVideoId(fromQuery))
                return fromQuery;

            var parts = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] is "shorts" or "embed" or "live" && IsYouTubeVideoId(parts[i + 1]))
                    return parts[i + 1];
            }
        }

        return null;
    }

    private static bool IsYouTubeVideoId(string? value)
    {
        if (value?.Length != 11)
            return false;

        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '_' && ch != '-')
                return false;
        }

        return true;
    }

    private static bool IsUsablePreview(CustomEventLinkPreview? preview)
    {
        return preview is not null &&
               !string.IsNullOrWhiteSpace(preview.Title) &&
               !string.Equals(preview.Title, GetDomain(preview.Url), StringComparison.OrdinalIgnoreCase);
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

    private sealed record YouTubeOEmbedResponse(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("author_name")] string? AuthorName,
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl);
}
