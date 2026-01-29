using System.Net;
using System.Text;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public class XApiClient
{
    private const string TweetsEndpoint = "https://api.twitter.com/2/tweets";
    private readonly HttpClient _http;
    private readonly string? _accessToken;
    private readonly ILogger<XApiClient> _log;

    public XApiClient(HttpClient http, Config config, ILogger<XApiClient> log)
    {
        _http = http;
        _accessToken = config.XAccessToken;
        _log = log;
    }

    public async Task SendAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            _log.LogWarning("X access token is not configured. Skipping tweet.");
            return;
        }

        var payload = JsonSerializer.Serialize(new { text });
        var maxAttempts = 3;
        var delayMs = 1000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TweetsEndpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);

            if (res.IsSuccessStatusCode)
                return;

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
}
