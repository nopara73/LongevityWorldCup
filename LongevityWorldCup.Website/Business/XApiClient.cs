using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly object _devPreviewLock = new();
    private long _devPreviewSeq;
    private readonly Dictionary<string, DevPreviewThread> _devThreadsByRoot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _devRootByTweetId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _devOpenedRoots = new(StringComparer.Ordinal);

    private sealed class DevPreviewPost
    {
        public required string TweetId { get; init; }
        public required string Text { get; init; }
        public required DateTime PostedAtUtc { get; init; }
        public string? InReplyToTweetId { get; init; }
        public List<string> LocalMediaPaths { get; init; } = new();
        public List<string> OtherMediaIds { get; init; } = new();
    }

    private sealed class DevPreviewThread
    {
        public required string RootTweetId { get; init; }
        public required string HtmlPath { get; init; }
        public List<DevPreviewPost> Posts { get; } = new();
    }

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
            var seq = Interlocked.Increment(ref _devPreviewSeq);
            var localTweetId = $"localdev_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{seq}";
            await WriteDevPreviewAsync(localTweetId, text, mediaIds, inReplyToTweetId);
            return localTweetId;
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

    private async Task WriteDevPreviewAsync(string tweetId, string text, IReadOnlyList<string>? mediaIds, string? inReplyToTweetId)
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

            var post = new DevPreviewPost
            {
                TweetId = tweetId,
                Text = text ?? "",
                PostedAtUtc = nowUtc,
                InReplyToTweetId = string.IsNullOrWhiteSpace(inReplyToTweetId) ? null : inReplyToTweetId
            };
            if (mediaIds is { Count: > 0 })
            {
                foreach (var id in mediaIds)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (id.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
                        post.LocalMediaPaths.Add(id["local:".Length..]);
                    else
                        post.OtherMediaIds.Add(id);
                }
            }

            string rootTweetId;
            string fullPath;
            List<DevPreviewPost> postsSnapshot;
            var openBrowser = false;
            lock (_devPreviewLock)
            {
                if (!string.IsNullOrWhiteSpace(inReplyToTweetId) && _devRootByTweetId.TryGetValue(inReplyToTweetId, out var knownRoot))
                    rootTweetId = knownRoot;
                else if (!string.IsNullOrWhiteSpace(inReplyToTweetId))
                    rootTweetId = inReplyToTweetId!;
                else
                    rootTweetId = tweetId;

                if (!_devThreadsByRoot.TryGetValue(rootTweetId, out var thread))
                {
                    var ts = nowUtc.ToString("yyyyMMdd_HHmmss_fff");
                    var safeRoot = rootTweetId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
                    var fileName = $"x_thread_{ts}_{safeRoot}.html";
                    fullPath = Path.Combine(root, fileName);
                    thread = new DevPreviewThread { RootTweetId = rootTweetId, HtmlPath = fullPath };
                    _devThreadsByRoot[rootTweetId] = thread;
                }
                else
                {
                    fullPath = thread.HtmlPath;
                }

                thread.Posts.Add(post);
                _devRootByTweetId[tweetId] = rootTweetId;
                if (!string.IsNullOrWhiteSpace(inReplyToTweetId) && !_devRootByTweetId.ContainsKey(inReplyToTweetId))
                    _devRootByTweetId[inReplyToTweetId] = rootTweetId;

                postsSnapshot = thread.Posts.ToList();
                openBrowser = _devOpenedRoots.Add(rootTweetId);
            }

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>LWC X Preview</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif;padding:24px;background:#010409;color:#e7e9ea;min-height:100vh;margin:0;}");
            sb.Append(".preview-card{max-width:720px;margin:0 auto;border-radius:24px;background:#0f1419;border:1px solid rgba(255,255,255,.08);box-shadow:0 20px 45px rgba(0,0,0,.65);}");
            sb.Append(".preview-header{display:flex;align-items:center;justify-content:space-between;padding:16px 24px;border-bottom:1px solid rgba(255,255,255,.08);font-size:.85rem;letter-spacing:1px;text-transform:uppercase;color:#8899a6;}");
            sb.Append(".preview-header small{font-size:.7rem;opacity:.9;}");
            sb.Append(".preview-content{padding:24px;font-size:1.15rem;line-height:1.6;white-space:pre-line;color:#e7e9ea;}");
            sb.Append(".preview-content a{color:#1d9bf0;text-decoration:none;font-weight:600;}");
            sb.Append(".preview-content a:hover{text-decoration:underline;color:#62b9ff;}");
            sb.Append(".preview-media{padding:0 24px 24px;}");
            sb.Append(".preview-media img{display:block;width:100%;height:auto;max-height:430px;object-fit:cover;border-radius:16px;border:1px solid rgba(255,255,255,.08);background:#0b0f14;}");
            sb.Append(".x-token{color:#1d9bf0;font-weight:600;}");
            sb.Append(".thread{padding:16px;display:flex;flex-direction:column;gap:16px;}");
            sb.Append(".tweet-card{background:#0b1016;border:1px solid rgba(255,255,255,.08);border-radius:16px;overflow:hidden;}");
            sb.Append(".tweet-meta{padding:10px 14px;border-bottom:1px solid rgba(255,255,255,.06);font-size:.78rem;color:#93a4b8;display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap;}");
            sb.Append(".tweet-body{padding:14px 14px 10px;font-size:1.05rem;line-height:1.55;white-space:pre-line;color:#e7e9ea;}");
            sb.Append(".tweet-body a{color:#1d9bf0;text-decoration:none;font-weight:600;}");
            sb.Append(".tweet-body a:hover{text-decoration:underline;color:#62b9ff;}");
            sb.Append("small{color:#7c8aa9;}");
            sb.Append("</style>");
            sb.Append("</head><body>");
            sb.Append("<div class=\"preview-card\">");
            sb.Append("<div class=\"preview-header\">");
            sb.Append("<span>LWC X Preview</span>");
            sb.Append("<small>");
            sb.Append(WebUtility.HtmlEncode(nowUtc.ToString("u")));
            sb.Append("</small>");
            sb.Append("</div>");
            sb.Append("<div class=\"thread\">");
            for (var idx = 0; idx < postsSnapshot.Count; idx++)
            {
                var p = postsSnapshot[idx];
                sb.Append("<article class=\"tweet-card\">");
                sb.Append("<div class=\"tweet-meta\">");
                sb.Append("<span>#");
                sb.Append(idx + 1);
                sb.Append(" · ");
                sb.Append(WebUtility.HtmlEncode(p.PostedAtUtc.ToString("u")));
                sb.Append("</span>");
                sb.Append("<span>tweet_id: ");
                sb.Append(WebUtility.HtmlEncode(p.TweetId));
                if (!string.IsNullOrWhiteSpace(p.InReplyToTweetId))
                {
                    sb.Append(" · reply_to: ");
                    sb.Append(WebUtility.HtmlEncode(p.InReplyToTweetId));
                }
                sb.Append("</span>");
                sb.Append("</div>");
                sb.Append("<div class=\"tweet-body\">");
                sb.Append(RenderPreviewText(p.Text));
                sb.Append("</div>");

                if (p.OtherMediaIds.Count > 0)
                {
                    sb.Append("<div class=\"preview-content\"><small>media_ids: ");
                    sb.Append(WebUtility.HtmlEncode(string.Join(", ", p.OtherMediaIds)));
                    sb.Append("</small></div>");
                }

                foreach (var path in p.LocalMediaPaths)
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(path);
                        var b64 = Convert.ToBase64String(bytes);
                        sb.Append("<div class=\"preview-media\"><img src=\"data:image/png;base64,");
                        sb.Append(b64);
                        sb.Append("\" /></div>");
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to embed preview media {Path}", path);
                    }
                }
                sb.Append("</article>");
            }
            sb.Append("</div>");
            sb.Append("</div>");
            sb.Append("</body></html>");

            await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8);

            if (openBrowser)
            {
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

    private static readonly Regex PreviewTokenRegex = new(
        @"(?<url>https?://[^\s]+)|(?<mention>@[A-Za-z0-9_]+)|(?<hashtag>#[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string RenderPreviewText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder();
        var lastIndex = 0;
        foreach (Match match in PreviewTokenRegex.Matches(text))
        {
            if (match.Index > lastIndex)
                builder.Append(WebUtility.HtmlEncode(text.Substring(lastIndex, match.Index - lastIndex)));

            if (match.Groups["url"].Success)
            {
                var url = match.Value;
                var escaped = WebUtility.HtmlEncode(url);
                builder.Append($"<a href=\"{escaped}\" target=\"_blank\" rel=\"noopener noreferrer\">{escaped}</a>");
            }
            else if (match.Groups["mention"].Success)
            {
                var mention = match.Value;
                var handle = mention[1..];
                var href = $"https://x.com/{handle}";
                builder.Append($"<a class=\"x-token x-mention\" href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(mention)}</a>");
            }
            else if (match.Groups["hashtag"].Success)
            {
                var hashtag = match.Value;
                var tag = hashtag[1..];
                var href = $"https://x.com/hashtag/{Uri.EscapeDataString(tag)}";
                builder.Append($"<a class=\"x-token x-hashtag\" href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(hashtag)}</a>");
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            builder.Append(WebUtility.HtmlEncode(text.Substring(lastIndex)));

        var html = builder.ToString();
        html = html.Replace("\r\n", "\n").Replace("\n", "<br/>");
        return html;
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
