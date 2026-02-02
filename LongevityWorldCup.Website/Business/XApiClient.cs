using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace LongevityWorldCup.Website.Business;

public class XApiClient
{
    private const string TweetsEndpoint = "https://api.twitter.com/2/tweets";
    private const string TokenEndpoint = "https://api.twitter.com/2/oauth2/token";
    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly ILogger<XApiClient> _log;
    private readonly IWebHostEnvironment _env;
    private readonly object _tokenLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;

    public XApiClient(HttpClient http, Config config, IWebHostEnvironment env, ILogger<XApiClient> log)
    {
        _http = http;
        _config = config;
        _env = env;
        _log = log;
        _accessToken = config.XAccessToken;
    }

    public async Task SendAsync(string text)
    {
        if (_env.IsDevelopment())
        {
            _log.LogInformation("X (Development): would have posted: {Content}", text);
            return;
        }

        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogInformation("X credentials not configured. Would have posted: {Content}", text);
            return;
        }

        var payload = JsonSerializer.Serialize(new { text });
        var maxAttempts = 3;
        var delayMs = 1000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TweetsEndpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);

            if (res.IsSuccessStatusCode)
                return;

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
                _log.LogWarning("X API error {StatusCode}, retrying in {Delay}ms (attempt {Attempt}).", res.StatusCode, delayMs, attempt);
                await Task.Delay(delayMs);
                continue;
            }

            res.EnsureSuccessStatusCode();
        }
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
}
