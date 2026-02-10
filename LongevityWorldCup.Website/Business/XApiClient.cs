using System.Diagnostics;
using System.Net;
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

    public async Task SendAsync(string text, IReadOnlyList<string>? mediaIds = null)
    {
        await SendTweetAsync(text, mediaIds, null);
    }

    public async Task<string?> UploadMediaAsync(Stream content, string contentType)
    {
        if (_env.IsDevelopment())
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "LWC_XPreview");
                Directory.CreateDirectory(root);
                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "bin";
                var fileName = $"x_media_{ts}.{ext}";
                var fullPath = Path.Combine(root, fileName);
                await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                await content.CopyToAsync(fs);
                _log.LogInformation("X (Development): wrote preview media {Path}", fullPath);
                return "local:" + fullPath;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "X (Development): failed to write preview media.");
                return null;
            }
        }

        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogInformation("X credentials not configured. Would have uploaded media with contentType {ContentType}", contentType);
            return null;
        }

        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "media", "media");

        using var req = new HttpRequestMessage(HttpMethod.Post, MediaUploadEndpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = form;

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("X media upload failed: {StatusCode} {Body}", res.StatusCode, json);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("media_id_string", out var idEl))
                return idEl.GetString();
            if (root.TryGetProperty("media_id", out var idElNum))
                return idElNum.GetRawText();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "X media upload response parse failed: {Json}", json);
        }

        return null;
    }

    public async Task<string?> SendTweetAsync(string text, IReadOnlyList<string>? mediaIds = null, string? inReplyToTweetId = null)
    {
        if (_env.IsDevelopment())
        {
            await WriteDevPreviewAsync(text, mediaIds, inReplyToTweetId);
            return null;
        }

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
        var maxAttempts = 3;
        var delayMs = 1000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TweetsEndpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var res = await _http.SendAsync(req);

            if (res.IsSuccessStatusCode)
            {
                try
                {
                    var json = await res.Content.ReadAsStringAsync();
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
                _log.LogWarning("X API error {StatusCode}, retrying in {Delay}ms (attempt {Attempt}).", res.StatusCode, delayMs, attempt);
                await Task.Delay(delayMs);
                continue;
            }

            res.EnsureSuccessStatusCode();
        }

        return null;
    }

    private async Task WriteDevPreviewAsync(string text, IReadOnlyList<string>? mediaIds, string? inReplyToTweetId)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "LWC_XPreview");
            Directory.CreateDirectory(root);

            var nowUtc = DateTime.UtcNow;
            foreach (var file in Directory.EnumerateFiles(root, "*.html"))
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    if (nowUtc - lastWrite > TimeSpan.FromDays(1))
                        File.Delete(file);
                }
                catch
                {
                }
            }

            var ts = nowUtc.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"x_{ts}.html";
            var fullPath = Path.Combine(root, fileName);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>LWC X Preview</title>");
            sb.Append("<style>body{font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;padding:24px;background:#020209;color:#eee;}pre{white-space:pre-wrap;word-wrap:break-word;background:#050516;padding:16px;border-radius:8px;}small{color:#aaa;}</style>");
            sb.Append("</head><body>");
            sb.Append("<h2>LWC X Preview</h2>");
            sb.Append("<small>");
            sb.Append(WebUtility.HtmlEncode(nowUtc.ToString("u")));
            sb.Append("</small>");
            sb.Append("<pre>");
            sb.Append(WebUtility.HtmlEncode(text ?? ""));
            sb.Append("</pre>");
            if (!string.IsNullOrWhiteSpace(inReplyToTweetId))
            {
                sb.Append("<p><strong>reply to tweet id:</strong> ");
                sb.Append(WebUtility.HtmlEncode(inReplyToTweetId));
                sb.Append("</p>");
            }
            if (mediaIds is { Count: > 0 })
            {
                var locals = new List<string>();
                var other = new List<string>();
                foreach (var id in mediaIds)
                {
                    if (!string.IsNullOrWhiteSpace(id) && id.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
                        locals.Add(id["local:".Length..]);
                    else if (!string.IsNullOrWhiteSpace(id))
                        other.Add(id);
                }

                if (other.Count > 0)
                {
                    sb.Append("<p><strong>media_ids:</strong> ");
                    sb.Append(WebUtility.HtmlEncode(string.Join(", ", other)));
                    sb.Append("</p>");
                }

                foreach (var path in locals)
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(path);
                        var b64 = Convert.ToBase64String(bytes);
                        sb.Append("<img style=\"max-width:100%;margin-top:12px;border-radius:8px;\" src=\"data:image/png;base64,");
                        sb.Append(b64);
                        sb.Append("\" />");
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to embed preview media {Path}", path);
                    }
                }
            }
            sb.Append("</body></html>");

            await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to open X preview HTML in browser: {Path}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to write X dev preview.");
            if (mediaIds is { Count: > 0 })
                _log.LogInformation("X (Development): would have posted: {Content} with mediaIds: {MediaIds}", text, string.Join(", ", mediaIds));
            else
                _log.LogInformation("X (Development): would have posted: {Content}", text);
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
