using System.Text;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public class SlackWebhookClient
{
    private readonly HttpClient _http;
    private readonly string? _webhookUrl;
    private readonly ILogger<SlackWebhookClient> _log;

    public SlackWebhookClient(HttpClient http, Config config, ILogger<SlackWebhookClient> log)
    {
        _http = http;
        _webhookUrl = config.SlackWebhookUrl;
        _log = log;
    }

    public async Task SendAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _log.LogError("Slack webhook URL is not configured. Skipping message: {Text}", text);
            return;
        }

        var payload = JsonSerializer.Serialize(new { text });
        using var req = new HttpRequestMessage(HttpMethod.Post, _webhookUrl);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    public async Task SendBlocksAsync(object[] blocks, string? fallbackText = null)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _log.LogError("Slack webhook URL is not configured. Skipping blocks message.");
            return;
        }

        var payload = JsonSerializer.Serialize(new { text = fallbackText, blocks });
        using var req = new HttpRequestMessage(HttpMethod.Post, _webhookUrl);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }
}