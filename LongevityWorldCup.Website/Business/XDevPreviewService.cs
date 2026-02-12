using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Business;

public class XDevPreviewService
{
    private readonly ILogger<XDevPreviewService> _log;
    private readonly object _lockObj = new();
    private long _seq;
    private readonly Dictionary<string, DevPreviewThread> _threadsByRoot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rootByTweetId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openedRoots = new(StringComparer.Ordinal);

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

    private static readonly Regex PreviewTokenRegex = new(
        @"(?<url>https?://[^\s]+)|(?<mention>@[A-Za-z0-9_]+)|(?<hashtag>#[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public XDevPreviewService(ILogger<XDevPreviewService> log)
    {
        _log = log;
    }

    public async Task<string?> UploadMediaPreviewAsync(Stream content, string contentType)
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

    public async Task<string> WriteTweetPreviewAsync(string text, IReadOnlyList<string>? mediaIds, string? inReplyToTweetId)
    {
        var seq = Interlocked.Increment(ref _seq);
        var localTweetId = $"localdev_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{seq}";
        await WritePreviewHtmlAsync(localTweetId, text, mediaIds, inReplyToTweetId);
        return localTweetId;
    }

    private async Task WritePreviewHtmlAsync(string tweetId, string text, IReadOnlyList<string>? mediaIds, string? inReplyToTweetId)
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
            bool openBrowser;
            lock (_lockObj)
            {
                if (!string.IsNullOrWhiteSpace(inReplyToTweetId) && _rootByTweetId.TryGetValue(inReplyToTweetId, out var knownRoot))
                    rootTweetId = knownRoot;
                else if (!string.IsNullOrWhiteSpace(inReplyToTweetId))
                    rootTweetId = inReplyToTweetId!;
                else
                    rootTweetId = tweetId;

                if (!_threadsByRoot.TryGetValue(rootTweetId, out var thread))
                {
                    var ts = nowUtc.ToString("yyyyMMdd_HHmmss_fff");
                    var safeRoot = rootTweetId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
                    var fileName = $"x_thread_{ts}_{safeRoot}.html";
                    fullPath = Path.Combine(root, fileName);
                    thread = new DevPreviewThread { RootTweetId = rootTweetId, HtmlPath = fullPath };
                    _threadsByRoot[rootTweetId] = thread;
                }
                else
                {
                    fullPath = thread.HtmlPath;
                }

                thread.Posts.Add(post);
                _rootByTweetId[tweetId] = rootTweetId;
                if (!string.IsNullOrWhiteSpace(inReplyToTweetId) && !_rootByTweetId.ContainsKey(inReplyToTweetId))
                    _rootByTweetId[inReplyToTweetId] = rootTweetId;

                postsSnapshot = thread.Posts.ToList();
                openBrowser = _openedRoots.Add(rootTweetId);
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
}
