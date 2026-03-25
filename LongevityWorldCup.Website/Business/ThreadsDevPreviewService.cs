using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Business;

public class ThreadsDevPreviewService
{
    private readonly ILogger<ThreadsDevPreviewService> _log;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _lockObj = new();
    private long _seq;
    private readonly Dictionary<string, DevPreviewThread> _threadsByRoot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rootByPostId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openedRoots = new(StringComparer.Ordinal);

    private sealed class DevPreviewPost
    {
        public required string PostId { get; init; }
        public required string Text { get; init; }
        public required DateTime PostedAtUtc { get; init; }
        public string? InReplyToPostId { get; init; }
        public List<string> LocalMediaPaths { get; init; } = new();
        public List<string> OtherMediaIds { get; init; } = new();
    }

    private sealed class DevPreviewThread
    {
        public required string RootPostId { get; init; }
        public required string HtmlPath { get; init; }
        public List<DevPreviewPost> Posts { get; } = new();
    }

    private sealed class RenderedPreviewText
    {
        public required string Html { get; init; }
        public required IReadOnlyList<string> Urls { get; init; }
    }

    private sealed class LinkPreviewCard
    {
        public required string Url { get; init; }
        public required string Host { get; init; }
        public required string DisplayUrl { get; init; }
        public string? SiteName { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? ImageUrl { get; init; }
    }

    private static readonly Regex PreviewTokenRegex = new(
        @"(?<url>https?://[^\s]+)|(?<mention>@[A-Za-z0-9_]+)|(?<hashtag>#[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BoldMarkdownRegex = new(
        @"\*\*(.+?)\*\*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MetaTagRegex = new(
        @"<meta\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlAttributeRegex = new(
        @"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'<>`]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TitleRegex = new(
        @"<title\b[^>]*>(?<value>.*?)</title>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public ThreadsDevPreviewService(ILogger<ThreadsDevPreviewService> log, IHttpClientFactory httpClientFactory)
    {
        _log = log;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string?> UploadMediaPreviewAsync(Stream content, string contentType)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "LWC_ThreadsPreview");
            Directory.CreateDirectory(root);
            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var ext = contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "bin";
            var fileName = $"threads_media_{ts}.{ext}";
            var fullPath = Path.Combine(root, fileName);
            await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await content.CopyToAsync(fs);
            _log.LogInformation("Threads (Development): wrote preview media {Path}", fullPath);
            return "local:" + fullPath;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Threads (Development): failed to write preview media.");
            return null;
        }
    }

    public async Task<string> WritePostPreviewAsync(string text, IReadOnlyList<string>? mediaIds = null, string? inReplyToPostId = null, bool openInBrowser = true)
    {
        var seq = Interlocked.Increment(ref _seq);
        var localPostId = $"localdev_threads_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{seq}";
        await WritePreviewHtmlAsync(localPostId, text, mediaIds, inReplyToPostId, openInBrowser);
        return localPostId;
    }

    private async Task WritePreviewHtmlAsync(string postId, string text, IReadOnlyList<string>? mediaIds, string? inReplyToPostId, bool openInBrowser)
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "LWC_ThreadsPreview");
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
                PostId = postId,
                Text = text ?? "",
                PostedAtUtc = nowUtc,
                InReplyToPostId = string.IsNullOrWhiteSpace(inReplyToPostId) ? null : inReplyToPostId
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

            string rootPostId;
            string fullPath;
            List<DevPreviewPost> postsSnapshot;
            bool openBrowser;
            lock (_lockObj)
            {
                if (!string.IsNullOrWhiteSpace(inReplyToPostId) && _rootByPostId.TryGetValue(inReplyToPostId, out var knownRoot))
                    rootPostId = knownRoot;
                else if (!string.IsNullOrWhiteSpace(inReplyToPostId))
                    rootPostId = inReplyToPostId!;
                else
                    rootPostId = postId;

                if (!_threadsByRoot.TryGetValue(rootPostId, out var thread))
                {
                    var ts = nowUtc.ToString("yyyyMMdd_HHmmss_fff");
                    var safeRoot = rootPostId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
                    var fileName = $"threads_post_{ts}_{safeRoot}.html";
                    fullPath = Path.Combine(root, fileName);
                    thread = new DevPreviewThread { RootPostId = rootPostId, HtmlPath = fullPath };
                    _threadsByRoot[rootPostId] = thread;
                }
                else
                {
                    fullPath = thread.HtmlPath;
                }

                thread.Posts.Add(post);
                _rootByPostId[postId] = rootPostId;
                if (!string.IsNullOrWhiteSpace(inReplyToPostId) && !_rootByPostId.ContainsKey(inReplyToPostId))
                    _rootByPostId[inReplyToPostId] = rootPostId;

                postsSnapshot = thread.Posts.ToList();
                openBrowser = openInBrowser && _openedRoots.Add(rootPostId);
            }

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>LWC Threads Preview</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Helvetica,Arial,sans-serif;padding:32px;background:#101010;color:#f5f5f5;min-height:100vh;margin:0;}");
            sb.Append(".preview-shell{max-width:680px;margin:0 auto;background:#0f0f10;border:1px solid #232323;border-radius:24px;box-shadow:0 30px 80px rgba(0,0,0,.45);overflow:hidden;}");
            sb.Append(".preview-header{display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-bottom:1px solid #232323;font-size:.8rem;letter-spacing:.02em;color:#a1a1aa;background:#111111;}");
            sb.Append(".preview-header strong{font-size:.92rem;color:#fafafa;font-weight:700;}");
            sb.Append(".preview-header small{font-size:.72rem;opacity:.92;}");
            sb.Append(".timeline{display:block;padding:4px 0;}");
            sb.Append(".thread-item{display:flex;gap:12px;padding:16px 18px 12px 18px;background:#0f0f10;}");
            sb.Append(".thread-item.is-last{padding-bottom:16px;}");
            sb.Append(".avatar-col{position:relative;width:40px;flex:0 0 40px;}");
            sb.Append(".avatar{width:40px;height:40px;border-radius:999px;background:linear-gradient(180deg,#f5f5f5,#c7c7c7);color:#111;font-weight:800;display:flex;align-items:center;justify-content:center;font-size:.88rem;letter-spacing:.02em;}");
            sb.Append(".thread-line{position:absolute;left:19px;top:46px;bottom:-16px;width:2px;background:#2a2a2a;border-radius:2px;}");
            sb.Append(".thread-main{min-width:0;flex:1;}");
            sb.Append(".thread-head{display:flex;align-items:center;gap:6px;flex-wrap:wrap;font-size:.93rem;line-height:1.2;}");
            sb.Append(".display-name{font-weight:700;color:#fafafa;}");
            sb.Append(".handle,.meta{color:#9f9fa8;}");
            sb.Append(".meta-dot{color:#5b5b61;}");
            sb.Append(".more{margin-left:auto;color:#b9b9c0;font-size:1.1rem;line-height:1;}");
            sb.Append(".char-count{margin-left:auto;color:#9f9fa8;font-size:.77rem;padding:2px 8px;border:1px solid #2a2a2a;border-radius:999px;background:#151516;}");
            sb.Append(".char-count.is-over{color:#ff8b8b;border-color:#6b2a2a;background:#251111;}");
            sb.Append(".thread-body{margin-top:6px;font-size:1rem;line-height:1.48;white-space:pre-wrap;color:#f5f5f5;word-break:break-word;}");
            sb.Append(".thread-body a{color:#f5f5f5;text-decoration:none;font-weight:500;}");
            sb.Append(".thread-body a:hover{text-decoration:underline;}");
            sb.Append(".link-preview{margin-top:12px;border:1px solid #2a2a2a;border-radius:20px;overflow:hidden;background:#141415;text-decoration:none;display:block;}");
            sb.Append(".link-preview:hover{background:#18181a;}");
            sb.Append(".link-preview-thumb{height:176px;background:linear-gradient(135deg,#171717,#242428);display:flex;align-items:flex-end;justify-content:flex-start;padding:12px 14px;color:#d4d4d8;font-size:.8rem;letter-spacing:.01em;position:relative;}");
            sb.Append(".link-preview-thumb.has-image{padding:0;align-items:stretch;justify-content:stretch;background:#111;}");
            sb.Append(".link-preview-thumb img{display:block;width:100%;height:100%;object-fit:cover;background:#111;}");
            sb.Append(".link-preview-overlay-title{position:absolute;left:12px;bottom:12px;max-width:82%;background:rgba(0,0,0,.7);color:#fff;padding:5px 9px;border-radius:10px;font-size:.8rem;line-height:1.25;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;}");
            sb.Append(".link-preview-meta{padding:12px 14px 14px 14px;background:#141415;}");
            sb.Append(".link-preview-domain{color:#a1a1aa;font-size:.78rem;line-height:1.2;}");
            sb.Append(".link-preview-source{color:#8f8f97;font-size:.77rem;line-height:1.2;padding:8px 4px 0 4px;}");
            sb.Append(".link-preview-title{color:#fafafa;font-size:.95rem;font-weight:600;line-height:1.25;margin-top:5px;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;}");
            sb.Append(".link-preview-desc{color:#a1a1aa;font-size:.84rem;line-height:1.28;margin-top:5px;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;}");
            sb.Append(".link-preview-url{color:#d4d4d8;font-size:.82rem;line-height:1.3;margin-top:8px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;opacity:.95;}");
            sb.Append(".preview-media{margin-top:12px;border:none;border-radius:20px;overflow:hidden;background:#171717;}");
            sb.Append(".preview-media img{display:block;width:100%;height:auto;max-height:460px;object-fit:cover;}");
            sb.Append(".media-meta{margin-top:8px;padding:8px 11px;border:1px solid #2a2a2a;border-radius:999px;color:#9f9fa8;font-size:.76rem;background:#151516;display:inline-block;}");
            sb.Append(".thread-actions{margin-top:10px;display:flex;gap:18px;color:#b5b5bc;font-size:.9rem;align-items:center;}");
            sb.Append(".thread-action{display:flex;align-items:center;gap:7px;}");
            sb.Append(".threads-token{color:#f5f5f5;font-weight:500;}");
            sb.Append("</style>");
            sb.Append("</head><body>");
            sb.Append("<div class=\"preview-shell\">");
            sb.Append("<div class=\"preview-header\">");
            sb.Append("<strong>Threads Preview</strong>");
            sb.Append("<small>");
            sb.Append(WebUtility.HtmlEncode(nowUtc.ToString("u")));
            sb.Append("</small>");
            sb.Append("</div>");
            sb.Append("<div class=\"timeline\">");
            for (var idx = 0; idx < postsSnapshot.Count; idx++)
            {
                var p = postsSnapshot[idx];
                var isLast = idx == postsSnapshot.Count - 1;
                var isReply = !string.IsNullOrWhiteSpace(p.InReplyToPostId);
                var posted = p.PostedAtUtc.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
                var charCount = (p.Text ?? string.Empty).Length;
                sb.Append("<article class=\"thread-item");
                if (isLast) sb.Append(" is-last");
                sb.Append("\">");
                sb.Append("<div class=\"avatar-col\">");
                sb.Append("<div class=\"avatar\">LW</div>");
                if (!isLast) sb.Append("<div class=\"thread-line\"></div>");
                sb.Append("</div>");
                sb.Append("<div class=\"thread-main\">");
                sb.Append("<div class=\"thread-head\">");
                sb.Append("<span class=\"display-name\">Longevity World Cup</span>");
                sb.Append("<span class=\"handle\">@longevitywc</span>");
                sb.Append("<span class=\"meta-dot\">·</span>");
                sb.Append("<span class=\"meta\">");
                sb.Append(WebUtility.HtmlEncode(posted));
                sb.Append("</span>");
                if (isReply) sb.Append("<span class=\"meta\">reply</span>");
                sb.Append("<span class=\"more\">&middot;&middot;&middot;</span>");
                sb.Append("<span class=\"char-count");
                if (charCount > 500) sb.Append(" is-over");
                sb.Append("\">");
                sb.Append(charCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("/500");
                sb.Append("</span>");
                sb.Append("</div>");
                sb.Append("<div class=\"thread-body\">");
                var renderedText = RenderPreviewText(p.Text);
                sb.Append(renderedText.Html);
                sb.Append("</div>");
                var linkCards = await BuildLinkPreviewCardsAsync(renderedText.Urls);
                foreach (var card in linkCards)
                    AppendLinkPreviewCard(sb, card);
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
                        sb.Append("<div class=\"preview-media\"><img alt=\"threads media\" src=\"data:image/png;base64,");
                        sb.Append(b64);
                        sb.Append("\" /></div>");
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to embed preview media {Path}", path);
                    }
                }
                sb.Append("<div class=\"thread-actions\"><span class=\"thread-action\">&#9825; <span>Like</span></span><span class=\"thread-action\">&#128172; <span>Reply</span></span><span class=\"thread-action\">&#128257; <span>Repost</span></span><span class=\"thread-action\">&#10150; <span>Share</span></span></div>");
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
                    _log.LogWarning(ex, "Failed to open Threads preview HTML in browser: {Path}", fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to write Threads dev preview.");
            if (mediaIds is { Count: > 0 })
                _log.LogInformation("Threads (Development): would have posted: {Content} with mediaIds: {MediaIds}", text, string.Join(", ", mediaIds));
            else
                _log.LogInformation("Threads (Development): would have posted: {Content}", text);
        }
    }

    private static RenderedPreviewText RenderPreviewText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new RenderedPreviewText
            {
                Html = string.Empty,
                Urls = Array.Empty<string>()
            };
        }

        var builder = new StringBuilder();
        var urls = new List<string>();
        var lastIndex = 0;
        foreach (Match bold in BoldMarkdownRegex.Matches(text))
        {
            if (bold.Index > lastIndex)
                AppendWithTokens(builder, text.Substring(lastIndex, bold.Index - lastIndex), urls);

            var inner = bold.Groups[1].Value;
            builder.Append("<strong>");
            AppendWithTokens(builder, inner, urls);
            builder.Append("</strong>");

            lastIndex = bold.Index + bold.Length;
        }

        if (lastIndex < text.Length)
            AppendWithTokens(builder, text.Substring(lastIndex), urls);

        var html = builder.ToString();
        html = html.Replace("\r\n", "\n").Replace("\n", "<br/>");
        html = Regex.Replace(html, @"[ \t]{2,}", " ");
        html = Regex.Replace(html, @"(?:<br/>[ \t]*){3,}", "<br/><br/>");
        html = Regex.Replace(html, @"(?:[ \t]|&nbsp;)+(?:<br/>)", "<br/>");

        return new RenderedPreviewText
        {
            Html = html,
            Urls = urls
        };
    }

    private static void AppendWithTokens(StringBuilder builder, string text, List<string> urls)
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
                urls.Add(url);
            }
            else if (match.Groups["mention"].Success)
            {
                var mention = match.Value;
                var handle = mention[1..];
                var href = $"https://www.threads.net/@{handle}";
                builder.Append($"<a class=\"threads-token threads-mention\" href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(mention)}</a>");
            }
            else if (match.Groups["hashtag"].Success)
            {
                var hashtag = match.Value;
                var tag = hashtag[1..];
                if (tag.All(char.IsDigit))
                {
                    builder.Append(WebUtility.HtmlEncode(hashtag));
                }
                else
                {
                    var href = $"https://www.threads.net/search?q=%23{Uri.EscapeDataString(tag)}";
                    builder.Append($"<a class=\"threads-token threads-hashtag\" href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(hashtag)}</a>");
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            builder.Append(WebUtility.HtmlEncode(text.Substring(lastIndex)));
    }

    private async Task<IReadOnlyList<LinkPreviewCard>> BuildLinkPreviewCardsAsync(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0)
            return Array.Empty<LinkPreviewCard>();

        var cards = new List<LinkPreviewCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawUrl in urls)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
                continue;

            var url = rawUrl.Trim();
            if (!seen.Add(url))
                continue;

            cards.Add(await BuildLinkPreviewCardAsync(url));
        }

        return cards;
    }

    private async Task<LinkPreviewCard> BuildLinkPreviewCardAsync(string url)
    {
        var fallback = BuildFallbackLinkPreviewCard(url);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");

            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return fallback;

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return fallback;

            var html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html))
                return fallback;

            if (html.Length > 300_000)
                html = html[..300_000];

            var finalUri = response.RequestMessage?.RequestUri;
            return BuildLinkPreviewCardFromHtml(url, finalUri, html, fallback);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Threads dev preview metadata fetch failed for {Url}", url);
            return fallback;
        }
    }

    private static LinkPreviewCard BuildLinkPreviewCardFromHtml(string originalUrl, Uri? finalUri, string html, LinkPreviewCard fallback)
    {
        var meta = ParseMetaTags(html);

        var title = FirstNonEmpty(
            GetMeta(meta, "twitter:title"),
            GetMeta(meta, "og:title"),
            ExtractHtmlTitle(html));

        var description = FirstNonEmpty(
            GetMeta(meta, "twitter:description"),
            GetMeta(meta, "og:description"),
            GetMeta(meta, "description"));

        var siteName = FirstNonEmpty(
            GetMeta(meta, "og:site_name"),
            GetMeta(meta, "twitter:site"));

        var imageUrl = FirstNonEmpty(
            GetMeta(meta, "twitter:image"),
            GetMeta(meta, "twitter:image:src"),
            GetMeta(meta, "og:image"));

        if (!string.IsNullOrWhiteSpace(imageUrl) && finalUri is not null && Uri.TryCreate(finalUri, imageUrl, out var resolvedImageUri))
            imageUrl = resolvedImageUri.ToString();

        var effectiveUrl = finalUri?.ToString() ?? originalUrl;

        return new LinkPreviewCard
        {
            Url = fallback.Url,
            Host = BuildHost(effectiveUrl) ?? fallback.Host,
            DisplayUrl = BuildDisplayUrl(effectiveUrl) ?? fallback.DisplayUrl,
            SiteName = NormalizePreviewField(siteName),
            Title = NormalizePreviewField(title),
            Description = NormalizePreviewField(description),
            ImageUrl = NormalizePreviewField(imageUrl)
        };
    }

    private static LinkPreviewCard BuildFallbackLinkPreviewCard(string url)
    {
        var safeUrl = (url ?? "").Trim();
        var host = BuildHost(safeUrl) ?? safeUrl;
        var displayUrl = BuildDisplayUrl(safeUrl) ?? safeUrl;

        return new LinkPreviewCard
        {
            Url = safeUrl,
            Host = host,
            DisplayUrl = displayUrl
        };
    }

    private static void AppendLinkPreviewCard(StringBuilder sb, LinkPreviewCard card)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Url))
            return;

        var hasImage = !string.IsNullOrWhiteSpace(card.ImageUrl);
        var displaySource = string.IsNullOrWhiteSpace(card.Host) ? card.DisplayUrl : card.Host;

        sb.Append("<a class=\"link-preview\" href=\"");
        sb.Append(WebUtility.HtmlEncode(card.Url));
        sb.Append("\" target=\"_blank\" rel=\"noopener noreferrer\">");
        sb.Append("<div class=\"link-preview-thumb");
        if (hasImage)
            sb.Append(" has-image");
        sb.Append("\">");
        if (hasImage)
        {
            sb.Append("<img alt=\"link preview image\" loading=\"lazy\" referrerpolicy=\"no-referrer\" src=\"");
            sb.Append(WebUtility.HtmlEncode(card.ImageUrl));
            sb.Append("\" />");
            if (!string.IsNullOrWhiteSpace(card.Title))
            {
                sb.Append("<div class=\"link-preview-overlay-title\">");
                sb.Append(WebUtility.HtmlEncode(card.Title));
                sb.Append("</div>");
            }
        }
        else
        {
            sb.Append(WebUtility.HtmlEncode(card.Host));
        }
        sb.Append("</div>");
        if (hasImage)
        {
            sb.Append("</a>");
            sb.Append("<div class=\"link-preview-source\">From ");
            sb.Append(WebUtility.HtmlEncode(displaySource));
            sb.Append("</div>");
            return;
        }

        sb.Append("<div class=\"link-preview-meta\">");
        sb.Append("<div class=\"link-preview-domain\">");
        sb.Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(card.SiteName) ? card.Host : card.SiteName));
        sb.Append("</div>");
        if (!string.IsNullOrWhiteSpace(card.Title))
        {
            sb.Append("<div class=\"link-preview-title\">");
            sb.Append(WebUtility.HtmlEncode(card.Title));
            sb.Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            sb.Append("<div class=\"link-preview-desc\">");
            sb.Append(WebUtility.HtmlEncode(card.Description));
            sb.Append("</div>");
        }
        sb.Append("<div class=\"link-preview-url\">");
        sb.Append(WebUtility.HtmlEncode(card.DisplayUrl));
        sb.Append("</div>");
        sb.Append("</div>");
        sb.Append("</a>");
    }

    private static string? BuildHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var uri = new Uri(url);
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildDisplayUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            var path = string.IsNullOrWhiteSpace(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            return $"{host}{path}";
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseMetaTags(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html))
            return result;

        foreach (Match tagMatch in MetaTagRegex.Matches(html))
        {
            var attrs = ParseAttributes(tagMatch.Value);
            if (attrs.Count == 0)
                continue;

            if (!attrs.TryGetValue("content", out var content) || string.IsNullOrWhiteSpace(content))
                continue;

            if (attrs.TryGetValue("property", out var prop) && !string.IsNullOrWhiteSpace(prop))
            {
                var key = prop.Trim().ToLowerInvariant();
                if (!result.ContainsKey(key))
                    result[key] = WebUtility.HtmlDecode(content.Trim());
            }

            if (attrs.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                var key = name.Trim().ToLowerInvariant();
                if (!result.ContainsKey(key))
                    result[key] = WebUtility.HtmlDecode(content.Trim());
            }
        }

        return result;
    }

    private static Dictionary<string, string> ParseAttributes(string tagHtml)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tagHtml))
            return attrs;

        foreach (Match m in HtmlAttributeRegex.Matches(tagHtml))
        {
            var key = m.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(key))
                continue;
            attrs[key] = m.Groups["value"].Value;
        }

        return attrs;
    }

    private static string? ExtractHtmlTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var m = TitleRegex.Match(html);
        if (!m.Success)
            return null;

        return WebUtility.HtmlDecode(m.Groups["value"].Value);
    }

    private static string? GetMeta(IReadOnlyDictionary<string, string> meta, string key)
    {
        return meta.TryGetValue(key, out var value) ? value : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? NormalizePreviewField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        normalized = Regex.Replace(normalized, @"\s+", " ");
        if (normalized.Length > 280)
            normalized = normalized[..280];
        return normalized;
    }
}

