using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace LongevityWorldCup.Website.Business;

public class ThreadsApiClient
{
    private const string CreateThreadEndpoint = "https://graph.threads.net/me/threads";
    private const string PublishThreadEndpoint = "https://graph.threads.net/me/threads_publish";
    private const int MaxTextLength = 500;

    private readonly HttpClient _http;
    private readonly Config _config;
    private readonly ILogger<ThreadsApiClient> _log;
    private readonly IWebHostEnvironment _env;
    private readonly ThreadsDevPreviewService _preview;

    public ThreadsApiClient(HttpClient http, Config config, IWebHostEnvironment env, ILogger<ThreadsApiClient> log, ThreadsDevPreviewService preview)
    {
        _http = http;
        _config = config;
        _env = env;
        _log = log;
        _preview = preview;
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
            _log.LogWarning("Threads send skipped because text was empty.");
            return null;
        }

        if (text.Length > MaxTextLength)
        {
            _log.LogWarning("Threads send skipped because text length {Length} exceeds limit {Limit}.", text.Length, MaxTextLength);
            return null;
        }

        if (_env.IsDevelopment())
        {
            return await _preview.WritePostPreviewAsync(text);
        }

        var token = _config.ThreadsAccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogInformation("Threads credentials not configured. Would have posted: {Content}", text);
            return null;
        }

        var creationId = await CreateTextContainerAsync(text, token);
        if (string.IsNullOrWhiteSpace(creationId))
            return null;

        return await PublishContainerAsync(creationId, token);
    }

    private async Task<string?> CreateTextContainerAsync(string text, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, CreateThreadEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("media_type", "TEXT"),
            new KeyValuePair<string, string>("text", text)
        });

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Threads create container failed: {StatusCode} {Body}", res.StatusCode, json);
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
            _log.LogError(ex, "Threads create container response parse failed: {Json}", json);
            return null;
        }

        _log.LogWarning("Threads create container returned no id.");
        return null;
    }

    private async Task<string?> PublishContainerAsync(string creationId, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, PublishThreadEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("creation_id", creationId)
        });

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Threads publish failed: {StatusCode} {Body}", res.StatusCode, json);
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
            _log.LogError(ex, "Threads publish response parse failed: {Json}", json);
            return null;
        }

        _log.LogWarning("Threads publish returned no id.");
        return null;
    }
}
