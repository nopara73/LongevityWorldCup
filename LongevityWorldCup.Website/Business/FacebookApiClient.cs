using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public class FacebookApiClient
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v23.0";
    private const int MaxTextLength = 63206;

    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly ILogger<FacebookApiClient> _log;

    public FacebookApiClient(HttpClient http, Config config, ILogger<FacebookApiClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

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

        var pageId = _config.FacebookPageId;
        var accessToken = _config.FacebookPageAccessToken;

        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(accessToken))
        {
            _log.LogInformation("Facebook credentials not configured. Would have posted: {Content}", text);
            return null;
        }

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
}
