using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace LongevityWorldCup.Website.Business;

public sealed record YouTubePreview(string VideoId, string Title, string AuthorName, string ThumbnailUrl);

public sealed class YouTubePreviewService(IHttpClientFactory clients, ILogger<YouTubePreviewService> log) : IDisposable
{
    public const string RateLimitPolicy = "youtube-previews";
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 512 });
    // Bounded stripes coalesce identical requests without retaining a lock per video.
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 8).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public static bool IsVideoId(string? id) => id?.Length == 11 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static string? TryGetVideoId(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            return null;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/').Split('/');
        string? id = null;
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) && path.Length == 2)
            id = path[1];
        else if (host.ToLowerInvariant() is "youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            if (uri.AbsolutePath == "/watch") id = QueryHelpers.ParseQuery(uri.Query)["v"].FirstOrDefault();
            else if (path.Length == 3 && path[1] is "shorts" or "live" or "embed") id = path[2];
        }
        return IsVideoId(id) ? id : null;
    }

    public async Task<YouTubePreview?> FetchAsync(string videoId, CancellationToken ct = default)
    {
        if (!IsVideoId(videoId)) return null;
        if (_cache.TryGetValue<CachedPreview>(videoId, out var cached)) return cached!.Preview;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        var gate = _gates[(uint)StringComparer.Ordinal.GetHashCode(videoId) % (uint)_gates.Length];
        var entered = false;
        try
        {
            await gate.WaitAsync(deadline.Token);
            entered = true;
            if (_cache.TryGetValue<CachedPreview>(videoId, out cached)) return cached!.Preview;
            var canonical = $"https://www.youtube.com/watch?v={videoId}";
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(canonical)}&format=json");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await clients.CreateClient(nameof(YouTubePreviewService))
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            YouTubePreview? preview = null;
            if (response.IsSuccessStatusCode)
            {
                await response.Content.LoadIntoBufferAsync(64 * 1024, deadline.Token);
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                var data = await JsonSerializer.DeserializeAsync<OEmbedResponse>(stream, cancellationToken: deadline.Token);
                if (!string.IsNullOrWhiteSpace(data?.Title) && data.Title.Length <= 1000 && (data.AuthorName?.Length ?? 0) <= 500)
                    // Ignore oEmbed HTML and remote image URLs; only text and this video ID reach the client.
                    preview = new(videoId, data.Title.Trim(), data.AuthorName?.Trim() ?? "", $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");
            }
            Remember(videoId, preview);
            return preview;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            log.LogDebug(ex, "YouTube preview unavailable for {VideoId}", videoId);
            if (entered) Remember(videoId, null);
            return null;
        }
        finally { if (entered) gate.Release(); }
    }

    private void Remember(string videoId, YouTubePreview? preview) => _cache.Set(videoId, new CachedPreview(preview),
        new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = preview is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromHours(6) });

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var gate in _gates) gate.Dispose();
    }

    private sealed record CachedPreview(YouTubePreview? Preview);
    private sealed record OEmbedResponse([property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("author_name")] string? AuthorName);
}
