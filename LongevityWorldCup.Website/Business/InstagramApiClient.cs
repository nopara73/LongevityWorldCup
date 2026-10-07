using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record InstagramPostRequest(string Caption, string ImageUrl, string AltText);

public sealed class InstagramApiException(string code, bool outcomeUnknown = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}

public sealed class InstagramApiClient(Config config, IHttpClientFactory clients, TimeProvider time, ILogger<InstagramApiClient> log)
{
    internal const string GraphBase = "https://graph.instagram.com/v26.0";
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(14);
    private static readonly TimeSpan RefreshRetryInterval = TimeSpan.FromHours(25);

    public bool IsConfigured => IsId(config.InstagramAccountId) && !string.IsNullOrWhiteSpace(config.InstagramAccessToken);

    // Run this even on days without eligible Events. Expired tokens cannot be revived.
    public async Task EnsureAccessTokenFreshAsync(CancellationToken ct = default)
    {
        if (!IsConfigured || !await _refreshGate.WaitAsync(0, ct)) return;
        try
        {
            var now = time.GetUtcNow();
            var expiry = ParseTime(config.InstagramAccessTokenExpiresAtUtc);
            if (expiry is { } expires && expires - now > RefreshWindow) return;
            if (ParseTime(config.InstagramAccessTokenLastRefreshAttemptAtUtc) is { } attempt && now - attempt < RefreshRetryInterval) return;

            config.InstagramAccessTokenLastRefreshAttemptAtUtc = now.UtcDateTime.ToString("o");
            await config.SaveAsync();
            if (expiry is { } expired && expired <= now)
            {
                log.LogWarning("Instagram token has expired; account authorization is required");
                return;
            }

            try
            {
                // This endpoint requires a query token. The named HTTP client disables
                // request loggers and redirects so the token cannot appear in logs.
                var url = "https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token="
                    + Uri.EscapeDataString(config.InstagramAccessToken!);
                var json = await SendAsync(HttpMethod.Get, url, null, false, ct);
                var token = Text(json, "access_token");
                if (string.IsNullOrWhiteSpace(token) || !json.TryGetProperty("expires_in", out var lifetime)
                    || !lifetime.TryGetInt64(out var seconds) || seconds <= 0 || seconds > 60 * 24 * 60 * 60)
                    throw new InstagramApiException("InvalidRefreshReceipt");
                await VerifyAccountTokenAsync(token, ct);
                config.InstagramAccessToken = token;
                config.InstagramAccessTokenExpiresAtUtc = time.GetUtcNow().AddSeconds(seconds).UtcDateTime.ToString("o");
                await config.SaveAsync();
                log.LogInformation("Instagram token renewed until {ExpiresAtUtc}", config.InstagramAccessTokenExpiresAtUtc);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // HTTP exceptions may contain token-bearing URLs. Never log the exception.
                log.LogWarning("Instagram token renewal failed with {ErrorCode}", (ex as InstagramApiException)?.Code ?? ex.GetType().Name);
            }
        }
        finally { _refreshGate.Release(); }
    }

    public Task VerifyAccountAsync(CancellationToken ct = default)
        => VerifyAccountTokenAsync(config.InstagramAccessToken ?? "", ct);

    private async Task VerifyAccountTokenAsync(string token, CancellationToken ct)
    {
        if (!IsId(config.InstagramAccountId) || string.IsNullOrWhiteSpace(token)) throw new InstagramApiException("NotConfigured");
        var json = await SendAsync(HttpMethod.Get, GraphBase + "/me?fields=user_id,username", null, false, ct, token);
        if (Text(json, "user_id") != config.InstagramAccountId)
            throw new InstagramApiException("AccountMismatch");
    }

    public async Task<string> CreateContainerAsync(InstagramPostRequest post, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(post.Caption) || post.Caption.Length > InstagramPost.MaxCaptionLength
            || post.AltText.Length > InstagramPost.MaxAltTextLength || !IsImageUrl(post.ImageUrl))
            throw new InstagramApiException("InvalidPost");
        await VerifyAccountAsync(ct);
        var json = await SendAsync(HttpMethod.Post, GraphBase + "/" + config.InstagramAccountId + "/media",
            new { image_url = post.ImageUrl, caption = post.Caption, alt_text = post.AltText }, false, ct);
        return ReadId(json, false);
    }

    public async Task<string> GetContainerStatusAsync(string containerId, CancellationToken ct = default)
    {
        if (!IsId(containerId)) throw new InstagramApiException("InvalidContainerId");
        var json = await SendAsync(HttpMethod.Get, GraphBase + "/" + containerId + "?fields=id,status_code", null, false, ct);
        if (Text(json, "id") != containerId) throw new InstagramApiException("ContainerMismatch");
        return Text(json, "status_code") ?? throw new InstagramApiException("MissingContainerStatus");
    }

    public async Task<string> PublishContainerAsync(string containerId, CancellationToken ct = default)
    {
        if (!IsId(containerId)) throw new InstagramApiException("InvalidContainerId");
        await VerifyAccountAsync(ct);
        var json = await SendAsync(HttpMethod.Post, GraphBase + "/" + config.InstagramAccountId + "/media_publish",
            new { creation_id = containerId }, true, ct);
        return ReadId(json, true);
    }

    internal async Task<SocialPostReceipt> ReadReceiptAsync(string mediaId, string caption, CancellationToken ct = default)
    {
        if (!IsId(mediaId)) throw new InstagramApiException("InvalidMediaId");
        var json = await SendAsync(HttpMethod.Get, GraphBase + "/" + mediaId + "?fields=id,caption,media_type,permalink", null, false, ct);
        var url = Text(json, "permalink");
        if (Text(json, "id") != mediaId || Text(json, "caption") != caption || Text(json, "media_type") != "IMAGE"
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host is not ("www.instagram.com" or "instagram.com") || !uri.AbsolutePath.StartsWith("/p/", StringComparison.Ordinal)
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
            throw new InstagramApiException("InvalidPostReadback");
        return new(mediaId, url!);
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, object? body, bool publishing, CancellationToken ct, string? token = null)
    {
        using var client = clients.CreateClient(nameof(InstagramApiClient));
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? config.InstagramAccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Treat only explicit authentication/rate-limit rejections as safely retryable
                // writes. An arbitrary error after media_publish may hide a completed post.
                var unknown = publishing && response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);
                throw new InstagramApiException("Http" + (int)response.StatusCode, unknown);
            }
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length > 1_000_000) throw new InstagramApiException("OversizedResponse", publishing);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty("error", out _))
                throw new InstagramApiException("InvalidResponse", publishing);
            return document.RootElement.Clone();
        }
        catch (InstagramApiException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw new InstagramApiException(ex is JsonException ? "InvalidJson" : "NetworkError", publishing);
        }
    }

    private static string ReadId(JsonElement json, bool publishing)
        => Text(json, "id") is { } id && IsId(id) ? id : throw new InstagramApiException("MissingReceipt", publishing);
    private static string? Text(JsonElement json, string key)
        => json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool IsId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 32 && id.All(char.IsAsciiDigit);
    private static DateTimeOffset? ParseTime(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : null;
    private static bool IsImageUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "longevityworldcup.com"
            && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && uri.AbsolutePath.StartsWith("/generated/instagram/", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith(".jpg", StringComparison.Ordinal);
}
