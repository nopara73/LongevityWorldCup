using System.Net;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public class FacebookApiClient
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v23.0";
    private const string TokenUrl = "https://graph.facebook.com/v23.0/oauth/access_token";
    private const int MaxTextLength = 63206;

    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly ILogger<FacebookApiClient> _log;
    private readonly object _tokenLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _pageId;
    private string? _pageAccessToken;
    private string? _userAccessToken;

    public FacebookApiClient(HttpClient http, Config config, ILogger<FacebookApiClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
        _pageId = config.FacebookPageId;
        _pageAccessToken = config.FacebookPageAccessToken;
        _userAccessToken = config.FacebookUserAccessToken;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GetPageId()) &&
        !string.IsNullOrWhiteSpace(GetPageAccessToken());

    public async Task SendAsync(string text)
    {
        _ = await SendPostAsync(text);
    }

    public async Task<bool> TrySendAsync(string text)
    {
        return !string.IsNullOrWhiteSpace(await SendPostAsync(text));
    }

    public async Task<string?> SendPostAsync(string text)
    {
        text ??= "";
        if (string.IsNullOrWhiteSpace(text))
        {
            _log.LogWarning("Facebook send skipped because text was empty.");
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            _log.LogWarning("Facebook send skipped because text length {Length} exceeds limit {Limit}.", text.Length, MaxTextLength);
            return null;
        }

        var pageId = GetPageId();
        var accessToken = GetPageAccessToken();

        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(accessToken))
        {
            _log.LogInformation("Facebook credentials not configured. Would have posted: {Content}", text);
            return null;
        }

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var endpoint = $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/feed";
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("message", text),
                new KeyValuePair<string, string>("access_token", accessToken)
            });

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _log.LogError("Facebook publish failed: {StatusCode} {Body}", res.StatusCode, json);
                if (ShouldRefreshToken(res.StatusCode, json) && attempt < maxAttempts)
                {
                    var refreshed = await TryRefreshPageAccessTokenAsync();
                    if (refreshed)
                    {
                        pageId = GetPageId();
                        accessToken = GetPageAccessToken();
                        if (!string.IsNullOrWhiteSpace(pageId) && !string.IsNullOrWhiteSpace(accessToken))
                            continue;
                    }
                }

                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                    return idEl.GetString();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Facebook publish response parse failed: {Json}", json);
                return null;
            }

            _log.LogWarning("Facebook publish returned no id.");
            return null;
        }

        return null;
    }

    public async Task<string?> SendPhotoPostAsync(string text, string imageUrl)
    {
        text ??= "";
        imageUrl = imageUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            _log.LogWarning("Facebook image send skipped because image URL was empty.");
            return null;
        }

        var pageId = GetPageId();
        var accessToken = GetPageAccessToken();
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(accessToken))
        {
            _log.LogInformation("Facebook credentials not configured. Would have posted image {ImageUrl} with content: {Content}", imageUrl, text);
            return null;
        }

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var endpoint = $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/photos";
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("url", imageUrl),
                new KeyValuePair<string, string>("message", text),
                new KeyValuePair<string, string>("access_token", accessToken)
            });

            var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _log.LogError("Facebook photo publish failed: {StatusCode} {Body}", res.StatusCode, json);
                if (ShouldRefreshToken(res.StatusCode, json) && attempt < maxAttempts)
                {
                    var refreshed = await TryRefreshPageAccessTokenAsync();
                    if (refreshed)
                    {
                        pageId = GetPageId();
                        accessToken = GetPageAccessToken();
                        if (!string.IsNullOrWhiteSpace(pageId) && !string.IsNullOrWhiteSpace(accessToken))
                            continue;
                    }
                }

                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("post_id", out var postIdEl))
                    return postIdEl.GetString();
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                    return idEl.GetString();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Facebook photo publish response parse failed: {Json}", json);
                return null;
            }

            _log.LogWarning("Facebook photo publish returned no id.");
            return null;
        }

        return null;
    }

    private string? GetPageId()
    {
        lock (_tokenLock)
        {
            if (!string.IsNullOrWhiteSpace(_pageId))
                return _pageId;
            _pageId = _config.FacebookPageId;
            return _pageId;
        }
    }

    private string? GetPageAccessToken()
    {
        lock (_tokenLock)
        {
            if (!string.IsNullOrWhiteSpace(_pageAccessToken))
                return _pageAccessToken;
            _pageAccessToken = _config.FacebookPageAccessToken;
            return _pageAccessToken;
        }
    }

    private string? GetUserAccessToken()
    {
        lock (_tokenLock)
        {
            if (!string.IsNullOrWhiteSpace(_userAccessToken))
                return _userAccessToken;
            _userAccessToken = _config.FacebookUserAccessToken;
            return _userAccessToken;
        }
    }

    private async Task<bool> TryRefreshPageAccessTokenAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var appId = _config.FacebookAppId;
            var appSecret = _config.FacebookAppSecret;
            var pageId = GetPageId();
            var userToken = GetUserAccessToken();

            if (string.IsNullOrWhiteSpace(appId) ||
                string.IsNullOrWhiteSpace(appSecret) ||
                string.IsNullOrWhiteSpace(pageId) ||
                string.IsNullOrWhiteSpace(userToken))
            {
                _log.LogWarning("Facebook token refresh skipped because app credentials, page id, or user token are missing.");
                return false;
            }

            var refreshUrl = TokenUrl + "?" + string.Join("&", new Dictionary<string, string>
            {
                ["grant_type"] = "fb_exchange_token",
                ["client_id"] = appId,
                ["client_secret"] = appSecret,
                ["fb_exchange_token"] = userToken
            }.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            var tokenRes = await _http.GetAsync(refreshUrl);
            var tokenJson = await tokenRes.Content.ReadAsStringAsync();
            if (!tokenRes.IsSuccessStatusCode)
            {
                _log.LogError("Facebook long-lived token refresh failed: {StatusCode} {Body}", tokenRes.StatusCode, tokenJson);
                return false;
            }

            string? refreshedUserToken = null;
            try
            {
                using var doc = JsonDocument.Parse(tokenJson);
                refreshedUserToken = doc.RootElement.TryGetProperty("access_token", out var accessEl)
                    ? accessEl.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Facebook long-lived token refresh parse failed: {Json}", tokenJson);
                return false;
            }

            if (string.IsNullOrWhiteSpace(refreshedUserToken))
            {
                _log.LogError("Facebook long-lived token refresh returned no access token.");
                return false;
            }

            var pagesUrl = $"{GraphApiBaseUrl}/me/accounts?fields=id,name,access_token&access_token={Uri.EscapeDataString(refreshedUserToken)}";
            var pagesRes = await _http.GetAsync(pagesUrl);
            var pagesJson = await pagesRes.Content.ReadAsStringAsync();
            if (!pagesRes.IsSuccessStatusCode)
            {
                _log.LogError("Facebook page token refresh failed: {StatusCode} {Body}", pagesRes.StatusCode, pagesJson);
                return false;
            }

            string? refreshedPageToken = null;
            try
            {
                using var doc = JsonDocument.Parse(pagesJson);
                if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataEl.EnumerateArray())
                    {
                        var itemPageId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (!string.Equals(itemPageId, pageId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        refreshedPageToken = item.TryGetProperty("access_token", out var tokenEl) ? tokenEl.GetString() : null;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Facebook page token refresh parse failed: {Json}", pagesJson);
                return false;
            }

            if (string.IsNullOrWhiteSpace(refreshedPageToken))
            {
                _log.LogError("Facebook page token refresh did not return an access token for page {PageId}.", pageId);
                return false;
            }

            lock (_tokenLock)
            {
                _userAccessToken = refreshedUserToken;
                _pageAccessToken = refreshedPageToken;
                _config.FacebookUserAccessToken = refreshedUserToken;
                _config.FacebookPageAccessToken = refreshedPageToken;
            }

            try
            {
                await _config.SaveAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save refreshed Facebook tokens to config.");
            }

            _log.LogInformation("Facebook tokens refreshed successfully.");
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool ShouldRefreshToken(HttpStatusCode statusCode, string? responseBody)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
            return true;

        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        return responseBody.Contains("Invalid OAuth access token", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("Error validating access token", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("Session has expired", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("OAuthException", StringComparison.OrdinalIgnoreCase);
    }
}
