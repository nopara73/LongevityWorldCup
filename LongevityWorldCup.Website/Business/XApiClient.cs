using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace LongevityWorldCup.Website.Business;

public class XApiClient
{
    private const string TweetsEndpoint = "https://api.twitter.com/2/tweets";
    private const string MediaUploadEndpoint = "https://upload.twitter.com/1.1/media/upload.json";
    private const string TokenEndpoint = "https://api.twitter.com/2/oauth2/token";
    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly ILogger<XApiClient> _log;
    private readonly IWebHostEnvironment _env;
    private readonly XDevPreviewService _preview;
    private readonly object _tokenLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;

    public sealed record XApiFailure(
        bool Retryable,
        HttpStatusCode? StatusCode,
        string? Summary,
        string? ResponseBody,
        string? RateLimitRemaining,
        string? RateLimitReset);

    public sealed record XMediaUploadResult(
        string? MediaId,
        XApiFailure? Failure);

    public XApiClient(HttpClient http, Config config, IWebHostEnvironment env, ILogger<XApiClient> log, XDevPreviewService preview)
    {
        _http = http;
        _config = config;
        _env = env;
        _log = log;
        _preview = preview;
        _accessToken = config.XAccessToken;
    }

    public bool IsConfigured => _env.IsDevelopment() || !string.IsNullOrWhiteSpace(GetAccessToken());

    public async Task SendAsync(string text, IReadOnlyList<string>? mediaIds = null)
    {
        await SendTweetAsync(text, mediaIds, null, true);
    }

    public async Task<string?> UploadMediaAsync(Stream content, string contentType)
    {
        var result = await UploadMediaDetailedAsync(content, contentType);
        return result.MediaId;
    }

    public async Task<XMediaUploadResult> UploadMediaDetailedAsync(Stream content, string contentType)
    {
        if (_env.IsDevelopment())
        {
            var mediaId = await _preview.UploadMediaPreviewAsync(content, contentType);
            return new XMediaUploadResult(mediaId, null);
        }

        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogInformation("X credentials not configured. Would have uploaded media with contentType {ContentType}", contentType);
            return new XMediaUploadResult(null, new XApiFailure(false, null, "X credentials not configured", null, null, null));
        }

        var oauth1 = GetOAuth1MediaCredentials();
        if (oauth1 is null)
        {
            _log.LogError(
                "X media upload cannot start because OAuth 1.0a media credentials are missing. HasConsumerKey={HasConsumerKey} HasConsumerSecret={HasConsumerSecret} HasUserAccessToken={HasUserAccessToken} HasUserAccessTokenSecret={HasUserAccessTokenSecret}",
                !string.IsNullOrWhiteSpace(_config.XConsumerKey),
                !string.IsNullOrWhiteSpace(_config.XConsumerSecret),
                !string.IsNullOrWhiteSpace(_config.XUserAccessToken),
                !string.IsNullOrWhiteSpace(_config.XUserAccessTokenSecret));
            return new XMediaUploadResult(null, new XApiFailure(false, null, "Missing OAuth 1.0a media credentials", null, null, null));
        }

        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "media", "media");

        _log.LogInformation(
            "X media upload started with contentType {ContentType}. AuthMode=OAuth1 user context. HasRefreshToken={HasRefreshToken}",
            contentType,
            !string.IsNullOrWhiteSpace(_config.XRefreshToken));

        using var req = new HttpRequestMessage(HttpMethod.Post, MediaUploadEndpoint);
        req.Headers.TryAddWithoutValidation("Authorization", BuildOAuth1AuthorizationHeader(
            HttpMethod.Post,
            MediaUploadEndpoint,
            oauth1.Value.ConsumerKey,
            oauth1.Value.ConsumerSecret,
            oauth1.Value.AccessToken,
            oauth1.Value.AccessTokenSecret));
        req.Content = form;

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            var failure = BuildFailure(res.StatusCode, json, res.Headers, res.Content.Headers);
            _log.LogError(
                "X media upload failed. StatusCode={StatusCode} Retryable={Retryable} Summary={Summary} RateLimitRemaining={RateLimitRemaining} RateLimitReset={RateLimitReset} Body={Body}",
                failure.StatusCode,
                failure.Retryable,
                failure.Summary,
                failure.RateLimitRemaining,
                failure.RateLimitReset,
                failure.ResponseBody);
            return new XMediaUploadResult(null, failure);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("media_id_string", out var idEl))
            {
                var mediaId = idEl.GetString();
                _log.LogInformation("X media upload succeeded with mediaId {MediaId}", mediaId);
                return new XMediaUploadResult(mediaId, null);
            }
            if (root.TryGetProperty("media_id", out var idElNum))
            {
                var mediaId = idElNum.GetRawText();
                _log.LogInformation("X media upload succeeded with mediaId {MediaId}", mediaId);
                return new XMediaUploadResult(mediaId, null);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "X media upload response parse failed: {Json}", json);
        }

        return new XMediaUploadResult(null, new XApiFailure(false, res.StatusCode, "X media upload succeeded but no media id was returned", json, null, null));
    }

    public async Task<string?> SendTweetAsync(string text, IReadOnlyList<string>? mediaIds = null, string? inReplyToTweetId = null, bool openPreviewInBrowser = true)
    {
        if (_env.IsDevelopment())
            return await _preview.WriteTweetPreviewAsync(text, mediaIds, inReplyToTweetId, openPreviewInBrowser);

        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            if (mediaIds is { Count: > 0 })
                _log.LogInformation("X credentials not configured. Would have posted: {Content} with mediaIds: {MediaIds}", text, string.Join(", ", mediaIds));
            else
                _log.LogInformation("X credentials not configured. Would have posted: {Content}", text);
            return null;
        }

        object payloadObj;
        if (mediaIds is { Count: > 0 } && !string.IsNullOrWhiteSpace(inReplyToTweetId))
            payloadObj = new { text, media = new { media_ids = mediaIds }, reply = new { in_reply_to_tweet_id = inReplyToTweetId } };
        else if (mediaIds is { Count: > 0 })
            payloadObj = new { text, media = new { media_ids = mediaIds } };
        else if (!string.IsNullOrWhiteSpace(inReplyToTweetId))
            payloadObj = new { text, reply = new { in_reply_to_tweet_id = inReplyToTweetId } };
        else
            payloadObj = new { text };

        var payload = JsonSerializer.Serialize(payloadObj);
        const int maxAttempts = 3;
        const int delayMs = 1000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TweetsEndpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var idEl))
                        return idEl.GetString();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "X send succeeded but response parse failed.");
                }
                return null;
            }

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                var refreshed = await TryRefreshTokenAsync();
                if (refreshed)
                {
                    token = GetAccessToken();
                    if (!string.IsNullOrWhiteSpace(token))
                        continue;
                }
            }

            var retryable = (int)res.StatusCode >= 500 || res.StatusCode == HttpStatusCode.TooManyRequests;
            if (attempt < maxAttempts && retryable)
            {
                var failure = BuildFailure(res.StatusCode, json, res.Headers, res.Content.Headers);
                _log.LogWarning(
                    "X API error while sending tweet. StatusCode={StatusCode} Retryable={Retryable} Summary={Summary} RateLimitRemaining={RateLimitRemaining} RateLimitReset={RateLimitReset}. Retrying in {Delay}ms (attempt {Attempt}). Body={Body}",
                    failure.StatusCode,
                    failure.Retryable,
                    failure.Summary,
                    failure.RateLimitRemaining,
                    failure.RateLimitReset,
                    delayMs,
                    attempt,
                    failure.ResponseBody);
                await Task.Delay(delayMs);
                continue;
            }

            if (!retryable)
            {
                var failure = BuildFailure(res.StatusCode, json, res.Headers, res.Content.Headers);
                _log.LogError(
                    "X tweet send failed with non-retryable response. StatusCode={StatusCode} Summary={Summary} RateLimitRemaining={RateLimitRemaining} RateLimitReset={RateLimitReset} Body={Body}",
                    failure.StatusCode,
                    failure.Summary,
                    failure.RateLimitRemaining,
                    failure.RateLimitReset,
                    failure.ResponseBody);
            }

            res.EnsureSuccessStatusCode();
        }

        return null;
    }

    private string? GetAccessToken()
    {
        lock (_tokenLock)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken)) return _accessToken;
            _accessToken = _config.XAccessToken;
            return _accessToken;
        }
    }

    private async Task<bool> TryRefreshTokenAsync()
    {
        var refreshToken = _config.XRefreshToken;
        var clientId = _config.XApiKey;
        var clientSecret = _config.XApiSecret;

        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _log.LogWarning("X refresh token or API credentials not configured. Cannot refresh.");
            return false;
        }

        await _refreshLock.WaitAsync();
        try
        {
            var body = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken)
            };
            var bodyStr = string.Join("&", body.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            req.Headers.Add("Authorization", $"Basic {basic}");
            req.Content = new StringContent(bodyStr, Encoding.UTF8, "application/x-www-form-urlencoded");

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _log.LogError("X token refresh failed: {StatusCode} {Response}", res.StatusCode, json);
                return false;
            }

            string? newAccess = null;
            string? newRefresh = null;
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("access_token", out var at))
                    newAccess = at.GetString();
                if (root.TryGetProperty("refresh_token", out var rt))
                    newRefresh = rt.GetString();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "X token refresh response parse failed: {Json}", json);
                return false;
            }

            if (string.IsNullOrWhiteSpace(newAccess))
            {
                _log.LogError("X token refresh did not return access_token.");
                return false;
            }

            lock (_tokenLock)
            {
                _accessToken = newAccess;
                _config.XAccessToken = newAccess;
                if (!string.IsNullOrWhiteSpace(newRefresh))
                    _config.XRefreshToken = newRefresh;
            }

            try
            {
                await _config.SaveAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save X tokens to config.");
            }

            _log.LogInformation("X access token refreshed successfully.");
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private (string ConsumerKey, string ConsumerSecret, string AccessToken, string AccessTokenSecret)? GetOAuth1MediaCredentials()
    {
        var consumerKey = _config.XConsumerKey?.Trim();
        var consumerSecret = _config.XConsumerSecret?.Trim();
        var accessToken = _config.XUserAccessToken?.Trim();
        var accessTokenSecret = _config.XUserAccessTokenSecret?.Trim();

        if (string.IsNullOrWhiteSpace(consumerKey) ||
            string.IsNullOrWhiteSpace(consumerSecret) ||
            string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(accessTokenSecret))
            return null;

        return (consumerKey, consumerSecret, accessToken, accessTokenSecret);
    }

    private static string BuildOAuth1AuthorizationHeader(
        HttpMethod method,
        string url,
        string consumerKey,
        string consumerSecret,
        string accessToken,
        string accessTokenSecret,
        IReadOnlyDictionary<string, string>? additionalParameters = null)
    {
        var uri = new Uri(url);
        var oauthParameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_nonce"] = Guid.NewGuid().ToString("N"),
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["oauth_token"] = accessToken,
            ["oauth_version"] = "1.0"
        };

        if (additionalParameters is not null)
        {
            foreach (var pair in additionalParameters)
                oauthParameters[pair.Key] = pair.Value;
        }

        var signature = CreateOAuth1Signature(
            method.Method,
            uri,
            oauthParameters,
            consumerSecret,
            accessTokenSecret);
        oauthParameters["oauth_signature"] = signature;

        var headerValue = string.Join(", ",
            oauthParameters
                .Where(kv => kv.Key.StartsWith("oauth_", StringComparison.Ordinal))
                .Select(kv => $"{PercentEncode(kv.Key)}=\"{PercentEncode(kv.Value)}\""));

        return $"OAuth {headerValue}";
    }

    private static string CreateOAuth1Signature(
        string httpMethod,
        Uri uri,
        SortedDictionary<string, string> oauthParameters,
        string consumerSecret,
        string accessTokenSecret)
    {
        var allParameters = new List<KeyValuePair<string, string>>();

        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in query)
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                allParameters.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        allParameters.AddRange(oauthParameters.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)));

        var normalizedParameters = string.Join("&",
            allParameters
                .OrderBy(kv => PercentEncode(kv.Key), StringComparer.Ordinal)
                .ThenBy(kv => PercentEncode(kv.Value), StringComparer.Ordinal)
                .Select(kv => $"{PercentEncode(kv.Key)}={PercentEncode(kv.Value)}"));

        var normalizedUrl = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{uri.AbsolutePath}";
        var signatureBaseString =
            $"{httpMethod.ToUpperInvariant()}&{PercentEncode(normalizedUrl)}&{PercentEncode(normalizedParameters)}";

        var signingKey = $"{PercentEncode(consumerSecret)}&{PercentEncode(accessTokenSecret)}";
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
        var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(signatureBaseString));
        return Convert.ToBase64String(hash);
    }

    private static string PercentEncode(string value)
    {
        return Uri.EscapeDataString(value ?? string.Empty)
            .Replace("+", "%20", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal)
            .Replace("%7E", "~", StringComparison.Ordinal);
    }

    private static XApiFailure BuildFailure(
        HttpStatusCode statusCode,
        string? responseBody,
        System.Net.Http.Headers.HttpResponseHeaders headers,
        System.Net.Http.Headers.HttpContentHeaders contentHeaders)
    {
        var retryable = (int)statusCode >= 500 || statusCode == HttpStatusCode.TooManyRequests;
        var summary = BuildFailureSummary(responseBody);
        var rateLimitRemaining = TryGetHeader(headers, "x-rate-limit-remaining");
        var rateLimitReset = TryGetHeader(headers, "x-rate-limit-reset");

        _ = contentHeaders;

        return new XApiFailure(
            Retryable: retryable,
            StatusCode: statusCode,
            Summary: summary,
            ResponseBody: responseBody,
            RateLimitRemaining: rateLimitRemaining,
            RateLimitReset: rateLimitReset);
    }

    private static string? BuildFailureSummary(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array && errorsEl.GetArrayLength() > 0)
            {
                var parts = new List<string>();
                foreach (var item in errorsEl.EnumerateArray())
                {
                    var code = item.TryGetProperty("code", out var codeEl) ? codeEl.ToString() : null;
                    var message = item.TryGetProperty("message", out var messageEl) ? messageEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
                        parts.Add(string.IsNullOrWhiteSpace(code) ? message! : $"{code}: {message}");
                }

                if (parts.Count > 0)
                    return string.Join(" | ", parts);
            }

            var detail = root.TryGetProperty("detail", out var detailEl) ? detailEl.GetString() : null;
            var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            var summaryParts = new[] { title, detail, type }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (summaryParts.Length > 0)
                return string.Join(" | ", summaryParts);
        }
        catch
        {
        }

        return responseBody.Length <= 300 ? responseBody : responseBody[..300];
    }

    private static string? TryGetHeader(System.Net.Http.Headers.HttpHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
            return null;

        return values.FirstOrDefault();
    }
}
