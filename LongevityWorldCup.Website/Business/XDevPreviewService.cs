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
    private static readonly Regex BoldMarkdownRegex = new(
        @"\*\*(.+?)\*\*",
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

    public async Task<string> WriteTweetPreviewAsync(string text, IReadOnlyList<string>? mediaIds, string? inReplyToTweetId, bool openInBrowser = true)
    {
        var seq = Interlocked.Increment(ref _seq);
        var localTweetId = $"localdev_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{seq}";
        await WritePreviewHtmlAsync(localTweetId, text, mediaIds, inReplyToTweetId, openInBrowser);
        return localTweetId;
    }

    private async Task WritePreviewHtmlAsync(string tweetId, string text, IReadOnlyList<string>? mediaIds, string? inReplyToTweetId, bool openInBrowser)
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
                openBrowser = openInBrowser && _openedRoots.Add(rootTweetId);
            }

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>LWC X Preview</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif;padding:24px;background:#000;color:#e7e9ea;min-height:100vh;margin:0;}");
            sb.Append(".preview-shell{max-width:640px;margin:0 auto;border:1px solid #2f3336;border-radius:18px;background:#000;overflow:hidden;}");
            sb.Append(".preview-header{display:flex;align-items:center;justify-content:space-between;padding:12px 16px;border-bottom:1px solid #2f3336;font-size:.78rem;letter-spacing:.3px;color:#8899a6;background:#000;}");
            sb.Append(".preview-header small{font-size:.72rem;opacity:.95;}");
            sb.Append(".timeline{display:block;}");
            sb.Append(".tweet-item{display:flex;gap:10px;padding:12px 16px 10px 16px;border-bottom:1px solid #2f3336;background:#000;}");
            sb.Append(".tweet-item.is-last{border-bottom:none;}");
            sb.Append(".avatar-col{position:relative;width:40px;flex:0 0 40px;}");
            sb.Append(".avatar{width:40px;height:40px;border-radius:999px;background:#1d9bf0;color:#fff;font-weight:700;display:flex;align-items:center;justify-content:center;font-size:.9rem;}");
            sb.Append(".thread-line{position:absolute;left:19px;top:44px;bottom:-10px;width:2px;background:#2f3336;border-radius:2px;}");
            sb.Append(".tweet-main{min-width:0;flex:1;}");
            sb.Append(".tweet-head{display:flex;align-items:center;gap:6px;flex-wrap:wrap;font-size:.92rem;line-height:1.25;}");
            sb.Append(".display-name{font-weight:700;color:#e7e9ea;}");
            sb.Append(".handle,.meta{color:#71767b;}");
            sb.Append(".char-count{margin-left:auto;color:#71767b;font-size:.78rem;padding:2px 8px;border:1px solid #2f3336;border-radius:999px;background:#0b0f14;}");
            sb.Append(".char-count.is-over{color:#ff6b6b;border-color:#5a2222;background:#1a0d0d;}");
            sb.Append(".tweet-body{margin-top:4px;font-size:1rem;line-height:1.5;white-space:pre-wrap;color:#e7e9ea;word-break:break-word;}");
            sb.Append(".tweet-body a{color:#1d9bf0;text-decoration:none;font-weight:500;}");
            sb.Append(".tweet-body a:hover{text-decoration:underline;}");
            sb.Append(".preview-media{margin-top:10px;border:none;border-radius:16px;overflow:hidden;background:#0f1419;}");
            sb.Append(".preview-media img{display:block;width:100%;height:auto;max-height:430px;object-fit:cover;}");
            sb.Append(".media-meta{margin-top:8px;padding:8px 10px;border:1px solid #2f3336;border-radius:10px;color:#71767b;font-size:.78rem;background:#0b0f14;}");
            sb.Append(".tweet-actions{margin-top:8px;display:flex;justify-content:space-between;max-width:420px;color:#71767b;font-size:.83rem;}");
            sb.Append(".x-token{color:#1d9bf0;font-weight:500;}");
            sb.Append("</style>");
            sb.Append("</head><body>");
            sb.Append("<div class=\"preview-shell\">");
            sb.Append("<div class=\"preview-header\">");
            sb.Append("<span>X Preview</span>");
            sb.Append("<small>");
            sb.Append(WebUtility.HtmlEncode(nowUtc.ToString("u")));
            sb.Append("</small>");
            sb.Append("</div>");
            sb.Append("<div class=\"timeline\">");
            for (var idx = 0; idx < postsSnapshot.Count; idx++)
            {
                var p = postsSnapshot[idx];
                var isLast = idx == postsSnapshot.Count - 1;
                var isReply = !string.IsNullOrWhiteSpace(p.InReplyToTweetId);
                var posted = p.PostedAtUtc.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
                var charCount = (p.Text ?? string.Empty).Length;
                sb.Append("<article class=\"tweet-item");
                if (isLast) sb.Append(" is-last");
                sb.Append("\">");
                sb.Append("<div class=\"avatar-col\">");
                sb.Append("<div class=\"avatar\">LW</div>");
                if (!isLast) sb.Append("<div class=\"thread-line\"></div>");
                sb.Append("</div>");
                sb.Append("<div class=\"tweet-main\">");
                sb.Append("<div class=\"tweet-head\">");
                sb.Append("<span class=\"display-name\">Longevity World Cup</span>");
                sb.Append("<span class=\"handle\">@longevitywc</span>");
                sb.Append("<span class=\"meta\">");
                sb.Append(WebUtility.HtmlEncode(posted));
                sb.Append("</span>");
                if (isReply) sb.Append("<span class=\"meta\">reply</span>");
                sb.Append("<span class=\"char-count");
                if (charCount > 280) sb.Append(" is-over");
                sb.Append("\">");
                sb.Append(charCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("/280");
                sb.Append("</span>");
                sb.Append("</div>");
                sb.Append("<div class=\"tweet-body\">");
                sb.Append(RenderPreviewText(p.Text));
                sb.Append("</div>");
                if (p.OtherMediaIds.Count > 0)
                {
                    sb.Append("<div class=\"media-meta\">media_ids: ");
                    sb.Append(WebUtility.HtmlEncode(string.Join(", ", p.OtherMediaIds)));
                    sb.Append("</div>");
                }
                foreach (var path in p.LocalMediaPaths)
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(path);
                        var b64 = Convert.ToBase64String(bytes);
                        sb.Append("<div class=\"preview-media\"><img alt=\"tweet media\" src=\"data:image/png;base64,");
                        sb.Append(b64);
                        sb.Append("\" /></div>");
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to embed preview media {Path}", path);
                    }
                }
                sb.Append("<div class=\"tweet-actions\"><span>Reply</span><span>Repost</span><span>Like</span><span>View</span></div>");
                sb.Append("</div>");
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
        foreach (Match bold in BoldMarkdownRegex.Matches(text))
        {
            if (bold.Index > lastIndex)
                AppendWithTokens(builder, text.Substring(lastIndex, bold.Index - lastIndex));

            var inner = bold.Groups[1].Value;
            builder.Append("<strong>");
            AppendWithTokens(builder, inner);
            builder.Append("</strong>");

            lastIndex = bold.Index + bold.Length;
        }

        if (lastIndex < text.Length)
            AppendWithTokens(builder, text.Substring(lastIndex));

        var html = builder.ToString();
        html = html.Replace("\r\n", "\n").Replace("\n", "<br/>");
        return html;
    }

    private static void AppendWithTokens(StringBuilder builder, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

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
    }
}

