using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

var repoRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Directory.GetCurrentDirectory();
var websiteRoot = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(repoRoot, "LongevityWorldCup.Website");

ScopedStylesheetGenerator.Generate(websiteRoot);

var pages = new[]
{
    new DocumentationPage(
        Source: Path.Combine(repoRoot, "LongevityWorldCup.Documentation", "About.md"),
        Output: Path.Combine(websiteRoot, "wwwroot", "misc-pages", "about.html"),
        CanonicalPath: "/about",
        SourceUrl: "https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/About.md"),
    new DocumentationPage(
        Source: Path.Combine(repoRoot, "LongevityWorldCup.Documentation", "LongevitySportHistory.md"),
        Output: Path.Combine(websiteRoot, "wwwroot", "misc-pages", "history.html"),
        CanonicalPath: "/history",
        SourceUrl: "https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/LongevitySportHistory.md"),
    new DocumentationPage(
        Source: Path.Combine(repoRoot, "LongevityWorldCup.Documentation", "Ruleset.md"),
        Output: Path.Combine(websiteRoot, "wwwroot", "misc-pages", "ruleset.html"),
        CanonicalPath: "/ruleset",
        SourceUrl: "https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/Ruleset.md",
        Reference: true)
};

var pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
    .DisableHtml()
    .Build();

foreach (var page in pages)
{
    var markdown = await File.ReadAllTextAsync(page.Source);
    var title = ExtractTitle(markdown);
    var documentHtml = AddTableCellLabels(OpenAbsoluteLinksInNewTabs(RewriteWebsiteAssetImageSources(Markdown.ToHtml(markdown, pipeline))));
    var contentsHtml = BuildContentsNav(documentHtml, page.SourceUrl);
    documentHtml = BuildTimeline(documentHtml);
    if (page.Reference)
    {
        documentHtml = ReferencePage.BuildDocument(documentHtml);
        contentsHtml = ReferencePage.BuildContents(contentsHtml, documentHtml);
    }

    var pageHtml = NormalizeGeneratedHtml(RenderPage(title, documentHtml, contentsHtml, page));

    Directory.CreateDirectory(Path.GetDirectoryName(page.Output)!);
    await File.WriteAllTextAsync(page.Output, pageHtml);
    Console.WriteLine($"Generated {Path.GetRelativePath(repoRoot, page.Output)} from {Path.GetRelativePath(repoRoot, page.Source)}");
}

static string ExtractTitle(string markdown)
{
    using var reader = new StringReader(markdown);
    while (reader.ReadLine() is { } line)
    {
        if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            return line[2..].Trim();
        }
    }

    return "Documentation";
}

static string BuildContentsNav(string documentHtml, string sourceUrl)
{
    var matches = Regex.Matches(
        documentHtml,
        "<h(?<level>[23]) id=\"(?<id>[^\"]+)\">(?<text>.*?)</h[23]>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    var links = matches
        .Select(match =>
        {
            var id = WebUtility.HtmlEncode(match.Groups["id"].Value);
            var level = WebUtility.HtmlEncode(match.Groups["level"].Value);
            var plainText = StripTags(WebUtility.HtmlDecode(match.Groups["text"].Value));
            if (level == "3" && TrySplitTimelineHeading(plainText, out var date, out var separator, out var title))
            {
                return $"            <a class=\"documentation-nav-level-3 documentation-nav-timeline\" href=\"#{id}\">{TimelineLabelHtml("documentation-nav-entry", date, separator, title)}</a>";
            }

            return $"            <a class=\"documentation-nav-level-{level}\" href=\"#{id}\">{WebUtility.HtmlEncode(plainText)}</a>";
        })
        .ToList();

    var source = WebUtility.HtmlEncode(sourceUrl);
    if (links.Count == 0)
    {
        return $"""
        <nav class="documentation-nav" aria-label="Document navigation">
            <button type="button" class="documentation-nav-toggle" aria-expanded="false" aria-controls="documentation-nav-links">
                <span>On this page</span><span class="documentation-nav-toggle-icon" aria-hidden="true">+</span>
            </button>
            <div class="documentation-nav-links" id="documentation-nav-links">
                <a class="documentation-source-link" href="{source}" target="_blank" rel="noopener">Source Markdown</a>
            </div>
        </nav>
""";
    }

    return $$"""
        <nav class="documentation-nav" aria-label="Document navigation">
            <button type="button" class="documentation-nav-toggle" aria-expanded="false" aria-controls="documentation-nav-links">
                <span>On this page</span><span class="documentation-nav-toggle-icon" aria-hidden="true">+</span>
            </button>
            <div class="documentation-nav-links" id="documentation-nav-links">
                <div class="documentation-nav-title">Contents</div>
{{string.Join(Environment.NewLine, links)}}
                <a class="documentation-source-link" href="{{source}}" target="_blank" rel="noopener">Source Markdown</a>
            </div>
        </nav>
""";
}

// A level-3 heading that starts with a year ("2026 January 15 - Season ended") is a
// timeline entry. Its date and title are set apart; the separator stays in the text so
// the heading reads, copies and links exactly as written.
static bool TrySplitTimelineHeading(string text, out string date, out string separator, out string title)
{
    var match = Regex.Match(text, "^(?<date>\\d{4}\\b[^-–]*?)(?<separator>\\s+[-–]\\s+)(?<title>\\S.*)$", RegexOptions.Singleline);
    date = match.Groups["date"].Value.Trim();
    separator = match.Groups["separator"].Value;
    title = match.Groups["title"].Value.Trim();
    return match.Success;
}

static string TimelineLabelHtml(string block, string date, string separator, string title)
{
    return $"<span class=\"{block}-date\">{WebUtility.HtmlEncode(date)}</span>"
        + $"<span class=\"documentation-timeline-separator\">{WebUtility.HtmlEncode(separator)}</span>"
        + $"<span class=\"{block}-title\">{WebUtility.HtmlEncode(title)}</span>";
}

// Each dated entry, with everything up to the next heading, becomes one section, and a
// run of them becomes one timeline. Pages without dated headings are left untouched.
static string BuildTimeline(string documentHtml)
{
    var headings = Regex.Matches(documentHtml, "<h(?<level>[23]) id=\"(?<id>[^\"]+)\">(?<text>.*?)</h[23]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    if (headings.Count == 0)
    {
        return documentHtml;
    }

    var output = new StringBuilder();
    var position = 0;
    var inTimeline = false;
    for (var i = 0; i < headings.Count; i++)
    {
        var heading = headings[i];
        var end = i + 1 < headings.Count ? headings[i + 1].Index : documentHtml.Length;
        output.Append(documentHtml, position, heading.Index - position);

        var text = StripTags(WebUtility.HtmlDecode(heading.Groups["text"].Value));
        if (heading.Groups["level"].Value == "3" && !heading.Groups["text"].Value.Contains('<') && TrySplitTimelineHeading(text, out var date, out var separator, out var title))
        {
            if (!inTimeline)
            {
                output.Append("<div class=\"documentation-timeline\">\n");
                inTimeline = true;
            }

            var announced = date.Contains("announced", StringComparison.OrdinalIgnoreCase) ? " is-announced" : string.Empty;
            output.Append($"<section class=\"documentation-timeline-entry{announced}\">\n");
            output.Append($"<h3 id=\"{heading.Groups["id"].Value}\" class=\"documentation-timeline-heading\">{TimelineLabelHtml("documentation-timeline", date, separator, title)}</h3>");
            output.Append(documentHtml, heading.Index + heading.Length, end - heading.Index - heading.Length);
            output.Append("</section>\n");
        }
        else
        {
            if (inTimeline)
            {
                output.Append("</div>\n");
                inTimeline = false;
            }

            output.Append(documentHtml, heading.Index, end - heading.Index);
        }

        position = end;
    }

    if (inTimeline)
    {
        output.Append("</div>\n");
    }

    output.Append(documentHtml, position, documentHtml.Length - position);
    return output.ToString();
}

static string StripTags(string value)
{
    return Regex.Replace(value, "<.*?>", string.Empty).Trim();
}

static string AddTableCellLabels(string html)
{
    return Regex.Replace(
        html,
        "<table>\\s*<thead>\\s*<tr>(?<header>.*?)</tr>\\s*</thead>\\s*<tbody>(?<body>.*?)</tbody>\\s*</table>",
        match =>
        {
            var headers = Regex.Matches(
                    match.Groups["header"].Value,
                    "<th>(?<text>.*?)</th>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(header => StripTags(WebUtility.HtmlDecode(header.Groups["text"].Value)))
                .ToArray();

            if (headers.Length == 0)
            {
                return match.Value;
            }

            var body = Regex.Replace(
                match.Groups["body"].Value,
                "<tr>(?<row>.*?)</tr>",
                rowMatch =>
                {
                    var index = 0;
                    var rowHtml = Regex.Replace(
                        rowMatch.Groups["row"].Value,
                        "<td(?<attrs>[^>]*)>",
                        cellMatch =>
                        {
                            var attrs = cellMatch.Groups["attrs"].Value;
                            if (Regex.IsMatch(attrs, "\\sdata-label=", RegexOptions.IgnoreCase) || index >= headers.Length)
                            {
                                index++;
                                return cellMatch.Value;
                            }

                            var label = WebUtility.HtmlEncode(headers[index++]);
                            return $"<td{attrs} data-label=\"{label}\">";
                        },
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    return $"<tr>{rowHtml}</tr>";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return $"<table>{Environment.NewLine}<thead>{Environment.NewLine}<tr>{match.Groups["header"].Value}</tr>{Environment.NewLine}</thead>{Environment.NewLine}<tbody>{body}</tbody>{Environment.NewLine}</table>";
        },
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
}

static string OpenAbsoluteLinksInNewTabs(string html)
{
    return Regex.Replace(
        html,
        "<a href=\"(?<href>https?://[^\"]+)\">",
        match => $"<a href=\"{match.Groups["href"].Value}\" target=\"_blank\" rel=\"noopener noreferrer\">",
        RegexOptions.IgnoreCase);
}

static string RewriteWebsiteAssetImageSources(string html)
{
    return html.Replace(
        "src=\"../LongevityWorldCup.Website/wwwroot/",
        "src=\"/",
        StringComparison.Ordinal);
}

static string NormalizeGeneratedHtml(string html)
{
    var normalized = html.ReplaceLineEndings("\n");
    return normalized.EndsWith('\n') ? normalized : normalized + "\n";
}

static string RenderPage(string title, string documentHtml, string contentsHtml, DocumentationPage page)
{
    var encodedTitle = WebUtility.HtmlEncode(title);
    var canonicalPath = WebUtility.HtmlEncode(page.CanonicalPath);
    var sourceUrl = WebUtility.HtmlEncode(page.SourceUrl);
    var pageClass = WebUtility.HtmlEncode("documentation-page-" + page.CanonicalPath.Trim('/'));

    return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <title>{{encodedTitle}} | Longevity World Cup</title>
    <!--HEAD-->
    <link rel="alternate" type="text/markdown" title="Source Markdown" href="{{sourceUrl}}" />
    <style>
        .documentation-page {
            box-sizing: border-box;
            display: grid;
            grid-template-columns: minmax(11.5rem, 14rem) minmax(0, 50rem);
            gap: clamp(1.4rem, 3vw, 2.6rem);
            align-items: start;
            max-width: 74rem;
            margin: 2.5rem auto 3.5rem;
            padding: 0 clamp(1rem, 3vw, 1.5rem);
        }

        .documentation-nav {
            --documentation-nav-offset: 4.25rem;
            position: sticky;
            top: var(--documentation-nav-offset);
            display: grid;
            gap: 0.2rem;
            padding: 0.85rem 0.7rem 0.95rem;
            border-left: 3px solid var(--lwc-accent, var(--primary-color));
            border-radius: 0 8px 8px 0;
            background: var(--lwc-surface-muted, #eef2f5);
            box-shadow: none;
        }

        @media (min-width: 901px) {
            .documentation-nav {
                box-sizing: border-box;
                max-height: calc(100dvh - var(--documentation-nav-offset) - 1rem);
                overflow-y: auto;
                overscroll-behavior: contain;
                scroll-padding-block: var(--lwc-space-2);
                scrollbar-width: thin;
                scrollbar-color: var(--lwc-border-strong) transparent;
            }
        }

        .documentation-nav-title {
            margin: 0 0 0.35rem;
            padding: 0 0.45rem;
            color: var(--lwc-muted, #526373);
            font-size: var(--lwc-type-sm);
            font-weight: 700;
            text-transform: none;
            line-height: 1.5;
        }

        .documentation-nav-toggle {
            display: none;
        }

        .documentation-nav-links {
            display: grid;
            gap: 0.2rem;
            min-width: 0;
        }

        .documentation-nav a {
            display: flex;
            padding: 0.38rem 0.45rem;
            border-radius: var(--lwc-radius-sm);
            color: var(--lwc-ink, #334155);
            font-size: var(--lwc-type-sm);
            line-height: 1.5;
            text-decoration: none;
            min-height: 44px;
            box-sizing: border-box;
            align-items: center;
        }

        .documentation-nav .documentation-nav-level-3 {
            margin-left: 0.55rem;
            padding-top: 0.28rem;
            padding-bottom: 0.28rem;
            color: var(--lwc-muted, #506176);
            font-size: var(--lwc-type-sm);
            line-height: 1.5;
        }

        .documentation-nav a:hover,
        .documentation-nav a:focus-visible,
        .documentation-nav a.is-active {
            background: var(--lwc-accent-soft, rgba(0, 188, 212, 0.12));
            color: var(--lwc-accent-hover, #047888);
            outline: none;
        }

        .documentation-nav a.is-active {
            font-weight: 700;
            box-shadow: inset 3px 0 0 var(--lwc-accent);
        }

        .documentation-nav .documentation-source-link {
            margin-top: 0.65rem;
            padding-top: 0.75rem;
            border-top: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.1));
            color: var(--lwc-accent, var(--primary-color));
            font-weight: 700;
        }

        .documentation-document {
            box-sizing: border-box;
            padding: clamp(1.35rem, 3vw, 2.2rem);
            border: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.08));
            border-radius: 8px;
            background: var(--lwc-surface, #ffffff);
            color: var(--lwc-ink, #233142);
            font-size: 1.04rem;
            line-height: 1.7;
            overflow-wrap: break-word;
        }

        .documentation-document h1,
        .documentation-document h2,
        .documentation-document h3,
        .documentation-document h4 {
            color: var(--lwc-ink, #1f2b3a);
            line-height: 1.16;
            overflow-wrap: anywhere;
        }

        .documentation-document h1 {
            margin: 0 0 1.25rem;
            padding: 0 0 1rem;
            border-bottom: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.1));
            text-align: left;
            font-size: clamp(2.1rem, 4vw, 2.85rem);
            letter-spacing: 0;
            text-shadow: none;
        }

        .documentation-document h2 {
            margin: 2.45rem 0 0.85rem;
            padding-top: 1.05rem;
            border-top: 1px solid var(--lwc-border, rgba(0, 0, 0, 0.08));
            text-align: left;
            font-size: clamp(1.55rem, 3vw, 2rem);
            text-shadow: none;
        }

        .documentation-document h3 {
            margin: 1.8rem 0 0.6rem;
            font-size: clamp(1.22rem, 2.2vw, 1.45rem);
            text-shadow: none;
        }

        .documentation-document h4 {
            margin: 1.35rem 0 0.35rem;
            font-size: 1.08rem;
        }

        .documentation-document p,
        .documentation-document ul,
        .documentation-document ol {
            margin: 0 0 1rem;
        }

        .documentation-document ul,
        .documentation-document ol {
            padding-left: 1.5rem;
        }

        .documentation-document li {
            margin: 0.44rem 0;
            padding-left: 0.1rem;
        }

        .documentation-document a {
            color: var(--lwc-accent, var(--primary-color));
            font-weight: 700;
            text-decoration-thickness: 0.08em;
            text-underline-offset: 0.14em;
        }

        .documentation-document a:hover,
        .documentation-document a:focus-visible {
            color: var(--lwc-accent-hover, #008fa1);
        }

        .documentation-document img {
            box-sizing: border-box;
            display: block;
            max-width: 100%;
            max-width: min(100%, 48rem);
            max-height: 560px;
            max-height: min(560px, calc(100vh - 7rem));
            max-height: min(560px, calc(100svh - 7rem));
            width: auto;
            height: auto;
            object-fit: contain;
            margin: 1.45rem auto 1.8rem;
            border: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.12));
            border-radius: 8px;
            background: var(--lwc-surface-muted, rgba(248, 250, 252, 0.82));
            box-shadow: none;
        }

        .documentation-document table {
            display: block;
            width: 100%;
            margin: 1.25rem 0 1.5rem;
            overflow-x: auto;
            border-collapse: collapse;
            background: var(--lwc-surface, #ffffff);
            border-radius: 8px;
            font-variant-numeric: tabular-nums;
        }

        .documentation-document th,
        .documentation-document td {
            padding: 0.62rem 0.75rem;
            border-bottom: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.1));
            text-align: left;
            vertical-align: top;
            white-space: nowrap;
        }

        .documentation-document th {
            color: var(--lwc-ink, #111827);
            background: var(--lwc-surface-muted);
            font-weight: 700;
        }

        .documentation-source {
            margin: 2.25rem 0 0;
            padding-top: 1rem;
            border-top: 1px solid var(--lwc-border, rgba(0, 0, 0, 0.08));
            color: var(--lwc-muted, #506176);
            font-size: var(--lwc-type-sm);
            text-align: left;
            line-height: 1.5;
        }

        @media (max-width: 900px) {
            .documentation-page {
                display: block;
                margin: 1rem auto 3rem;
                padding: 0 1rem;
            }

            .documentation-nav {
                position: static;
                display: block;
                margin: 0 0 0.75rem;
                padding: 0.35rem;
                border-left: 0;
                border-top: 3px solid var(--lwc-accent, var(--primary-color));
                border-radius: 0 0 8px 8px;
            }

            .documentation-nav.is-enhanced .documentation-nav-toggle {
                box-sizing: border-box;
                display: flex;
                align-items: center;
                justify-content: space-between;
                width: 100%;
                min-height: 44px;
                padding: 0.6rem 0.75rem;
                border: 1px solid var(--lwc-border-strong, #71808d);
                border-radius: var(--lwc-radius-md);
                background: var(--lwc-surface, #ffffff);
                color: var(--lwc-ink, #334155);
                font: inherit;
                font-weight: 700;
                line-height: 1.25;
                text-align: left;
                cursor: pointer;
            }

            .documentation-nav-toggle:hover {
                border-color: var(--lwc-accent, var(--primary-color));
                background: var(--lwc-accent-soft, rgba(0, 188, 212, 0.12));
            }

            .documentation-nav-toggle:focus-visible {
                outline: 3px solid var(--lwc-accent-bright, #19c3d1);
                outline-offset: 2px;
            }

            .documentation-nav-toggle-icon {
                font-size: 1.25rem;
                line-height: 1;
                flex: 0 0 1.5rem;
                width: 1.5rem;
                text-align: center;
            }

            .documentation-nav-links {
                max-height: min(65svh, 30rem);
                margin-top: 0.35rem;
                overflow-y: auto;
                overscroll-behavior: contain;
            }

            .documentation-nav.is-enhanced:not(.is-open) .documentation-nav-links {
                display: none;
            }

            .documentation-nav-title {
                display: none;
            }

            .documentation-nav a {
                box-sizing: border-box;
                display: flex;
                align-items: center;
                width: 100%;
                max-width: 100%;
                min-height: 44px;
                padding: 0.6rem 0.75rem;
                background: var(--lwc-surface, #ffffff);
                white-space: normal;
                overflow-wrap: anywhere;
            }

            .documentation-nav .documentation-nav-level-3 {
                width: calc(100% - 0.75rem);
                margin-left: 0.75rem;
                border-left: 2px solid var(--lwc-border, #d7e0e6);
                font-size: 0.9rem;
            }

            .documentation-nav .documentation-source-link {
                margin-top: 0.35rem;
                padding-top: 0.6rem;
                border-top: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.1));
            }
        }

        @media (max-width: 600px) {
            .documentation-document {
                padding: 1.1rem;
                font-size: 0.98rem;
                line-height: 1.64;
            }

            .documentation-document h1 {
                font-size: 1.95rem;
            }

            .documentation-document h2 {
                font-size: 1.45rem;
            }

            .documentation-document img {
                margin: 1.15rem auto 1.45rem;
            }
        }

        @media (max-width: 720px) {
            .documentation-document table {
                display: block;
                border-collapse: separate;
                background: transparent;
                border-radius: 0;
                overflow: visible;
            }

            .documentation-document thead {
                display: none;
            }

            .documentation-document tbody {
                display: grid;
                gap: 0.55rem;
            }

            .documentation-document tbody tr {
                display: grid;
                grid-template-columns: auto minmax(0, 1fr);
                gap: 0.35rem 0.7rem;
                padding: 0.62rem 0.7rem;
                border: 1px solid var(--lwc-border, rgba(148, 163, 184, 0.28));
                border-radius: 8px;
                background: var(--lwc-surface-muted, rgba(248, 250, 252, 0.78));
            }

            .documentation-document tbody td {
                padding: 0;
                border-bottom: 0;
                white-space: normal;
                overflow-wrap: normal;
                word-break: normal;
            }

            .documentation-document tbody td {
                display: grid;
                grid-template-columns: auto minmax(0, 1fr);
                gap: 0.35rem;
                align-items: baseline;
                color: var(--lwc-ink, #334155);
                font-size: var(--lwc-type-sm);
                line-height: 1.5;
            }

            .documentation-document tbody td::before {
                content: attr(data-label);
                color: var(--lwc-muted, #526373);
                font-size: var(--lwc-type-xs);
                font-weight: 700;
                text-transform: none;
                letter-spacing: 0;
                line-height: 1.5;
            }

            .documentation-document tbody td:first-child {
                grid-column: 1;
                grid-template-columns: auto auto;
            }

            .documentation-document tbody td:nth-child(2) {
                grid-column: 1 / -1;
                grid-row: 2;
                font-size: 0.96rem;
                font-weight: 700;
                white-space: normal;
                overflow-wrap: anywhere;
            }

            .documentation-document tbody td:nth-child(2)::before {
                display: none;
            }

            .documentation-document tbody td:last-child {
                grid-column: 2;
                justify-self: end;
                width: 6.25rem;
                grid-template-columns: 1fr;
                justify-items: end;
                gap: 0.12rem;
                text-align: right;
            }

            .documentation-document tbody td:last-child::before {
                white-space: nowrap;
            }
        }
        .documentation-nav a:focus-visible {
            outline: 2px solid var(--lwc-accent);
            outline-offset: -2px;
        }

        /* About reads as a designed article: a cover with a lede and a full-width
           silhouette, numbered chapters, a contents rail that fills as the reader
           moves through them, and the closing links as destination tiles. */
        .documentation-page-about .documentation-document {
            --about-bleed: clamp(1.35rem, 3vw, 2.2rem);
            counter-reset: about-chapter;
            padding: var(--about-bleed);
            border-radius: var(--lwc-radius-lg, 8px);
            box-shadow: var(--lwc-shadow-sm, none);
            overflow: hidden;
        }

        .documentation-page-about .documentation-document h1 {
            margin-bottom: 1.4rem;
            padding-bottom: 0;
            border-bottom: 0;
            font-size: clamp(2.3rem, 5vw, 3.35rem);
            line-height: 1.06;
        }

        .documentation-page-about .documentation-document h1::after {
            content: "";
            display: block;
            width: 3.5rem;
            height: 4px;
            margin-top: 1.1rem;
            border-radius: 2px;
            background: var(--lwc-accent, var(--primary-color));
        }

        .documentation-page-about .documentation-document h1 ~ p:not(:has(> img), :has(> img) ~ p) {
            color: var(--lwc-ink, #233142);
            font-size: clamp(1.12rem, 1.6vw, 1.26rem);
            line-height: 1.6;
        }

        .documentation-page-about .documentation-document p:has(> img) {
            margin: 2rem calc(-1 * var(--about-bleed)) 0;
        }

        .documentation-page-about .documentation-document p > img {
            width: 100%;
            max-width: none;
            max-height: none;
            aspect-ratio: 16 / 9;
            margin: 0;
            border: 0;
            border-block: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.12));
            border-radius: 0;
            object-fit: cover;
            object-position: 50% 8%;
        }

        .documentation-page-about .documentation-document p:has(> img) + p {
            margin: 0.85rem 0 0;
            padding-left: 0.85rem;
            border-left: 3px solid var(--lwc-accent, var(--primary-color));
            color: var(--lwc-muted, #506176);
            font-size: var(--lwc-type-sm, 0.875rem);
            line-height: 1.5;
        }

        .documentation-page-about .documentation-document h2 {
            position: relative;
            counter-increment: about-chapter;
            margin-top: 3.4rem;
            padding-top: 1.45rem;
        }

        .documentation-page-about .documentation-document h2::before {
            content: counter(about-chapter, decimal-leading-zero);
            position: absolute;
            top: 0;
            left: 0;
            padding-right: 0.8rem;
            transform: translateY(-50%);
            background: var(--lwc-surface, #ffffff);
            color: var(--lwc-accent, var(--primary-color));
            font-variant-numeric: tabular-nums;
            font-size: 0.95rem;
            font-weight: 700;
            letter-spacing: 0.08em;
            line-height: 1;
        }

        .documentation-page-about #where-to-go-next + ul {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(min(100%, 13.5rem), 1fr));
            gap: 0.75rem;
            margin: 1.25rem 0 0;
            padding: 0;
            list-style: none;
        }

        .documentation-page-about #where-to-go-next + ul > li {
            position: relative;
            margin: 0;
            padding: 1rem 1.1rem 1.05rem;
            border: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.1));
            border-radius: var(--lwc-radius-lg, 8px);
            background: var(--lwc-surface-muted, #eef2f5);
            color: var(--lwc-muted, #506176);
            font-size: 0.97rem;
            line-height: 1.5;
            transition: border-color var(--lwc-duration-fast, 0.15s) var(--lwc-ease, ease),
                box-shadow var(--lwc-duration-fast, 0.15s) var(--lwc-ease, ease);
        }

        .documentation-page-about #where-to-go-next + ul > li > a {
            font-size: 1.06rem;
            text-decoration: none;
        }

        .documentation-page-about #where-to-go-next + ul > li > a::after {
            content: "";
            position: absolute;
            inset: -1px;
            border-radius: inherit;
        }

        .documentation-page-about #where-to-go-next + ul > li:hover,
        .documentation-page-about #where-to-go-next + ul > li:focus-within {
            border-color: var(--lwc-accent, var(--primary-color));
            box-shadow: var(--lwc-shadow-sm, none);
        }

        .documentation-page-about #where-to-go-next + ul > li > a:focus-visible {
            outline: none;
        }

        .documentation-page-about #where-to-go-next + ul > li > a:focus-visible::after {
            outline: 3px solid var(--lwc-accent-bright, #19c3d1);
            outline-offset: 2px;
        }

        /* The contents become a numbered rail; it fills with the accent up to the
           section being read. */
        .documentation-page-about .documentation-nav {
            border: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.1));
            border-radius: var(--lwc-radius-lg, 8px);
            counter-reset: about-contents;
        }

        .documentation-page-about .documentation-nav-links {
            gap: 0;
        }

        .documentation-page-about .documentation-nav a.documentation-nav-level-2 {
            counter-increment: about-contents;
            gap: 0.6rem;
            border-left: 3px solid var(--lwc-border, #d7e0e6);
            border-radius: 0 var(--lwc-radius-sm) var(--lwc-radius-sm) 0;
        }

        .documentation-page-about .documentation-nav a.documentation-nav-level-2::before {
            content: counter(about-contents, decimal-leading-zero);
            flex: 0 0 auto;
            min-width: 1.6rem;
            color: var(--lwc-muted, #526373);
            font-variant-numeric: tabular-nums;
            font-size: var(--lwc-type-xs, 0.75rem);
            font-weight: 700;
            letter-spacing: 0.04em;
        }

        .documentation-page-about .documentation-nav a.documentation-nav-level-2:has(~ a.is-active),
        .documentation-page-about .documentation-nav a.documentation-nav-level-2.is-active {
            border-left-color: var(--lwc-accent, var(--primary-color));
        }

        .documentation-page-about .documentation-nav a.documentation-nav-level-2:has(~ a.is-active)::before,
        .documentation-page-about .documentation-nav a.documentation-nav-level-2.is-active::before {
            color: var(--lwc-accent-hover, #047888);
        }

        .documentation-page-about .documentation-nav a.documentation-nav-level-2.is-active {
            box-shadow: none;
        }

        @media (max-width: 900px) {
            .documentation-page-about .documentation-nav {
                border-top: 3px solid var(--lwc-accent, var(--primary-color));
                border-radius: 0 0 var(--lwc-radius-lg, 8px) var(--lwc-radius-lg, 8px);
            }

            .documentation-page-about .documentation-nav-links {
                gap: 0.2rem;
            }

            .documentation-page-about .documentation-nav a.documentation-nav-level-2 {
                border-radius: 0 var(--lwc-radius-sm) var(--lwc-radius-sm) 0;
            }
        }

        @media (max-width: 600px) {
            .documentation-page-about .documentation-document {
                --about-bleed: 1.1rem;
            }

            .documentation-page-about .documentation-document p > img {
                aspect-ratio: 4 / 3;
            }

            .documentation-page-about #where-to-go-next + ul > li {
                padding: 0.8rem 0.85rem 0.85rem;
            }

            .documentation-page-about #where-to-go-next + ul > li > a {
                font-size: inherit;
            }

            .documentation-page-about .documentation-document h2 {
                margin-top: 2.8rem;
                padding-top: 1.3rem;
            }
        }

        @media (prefers-reduced-motion: reduce) {
            .documentation-page-about #where-to-go-next + ul > li {
                transition: none;
            }
        }

        /* Dated entries read as a timeline: a rail with one marker per entry, the
           date as a small line above a short title. Announced entries are hollow. */
        .documentation-timeline {
            position: relative;
            margin: 1.6rem 0 0;
            padding-left: 1.9rem;
        }

        .documentation-timeline::before {
            content: "";
            position: absolute;
            top: 0.55rem;
            bottom: 0.35rem;
            left: 0.4rem;
            width: 2px;
            border-radius: 1px;
            background: var(--lwc-border, #d7e0e6);
        }

        .documentation-timeline-entry {
            position: relative;
        }

        .documentation-timeline-entry + .documentation-timeline-entry {
            margin-top: 2.1rem;
        }

        .documentation-timeline-entry::before {
            content: "";
            position: absolute;
            top: 0.42rem;
            left: calc(-1.9rem + 0.4rem + 1px - 0.4rem);
            box-sizing: border-box;
            width: 0.8rem;
            height: 0.8rem;
            border: 2px solid var(--lwc-accent, var(--primary-color));
            border-radius: 50%;
            background: var(--lwc-accent, var(--primary-color));
            box-shadow: 0 0 0 4px var(--lwc-surface, #ffffff);
        }

        .documentation-timeline-entry.is-announced::before {
            background: var(--lwc-surface, #ffffff);
        }

        .documentation-document .documentation-timeline-heading {
            display: flex;
            flex-direction: column;
            gap: 0.3rem;
            margin: 0 0 0.65rem;
        }

        .documentation-timeline-date,
        .documentation-nav-entry-date {
            color: var(--lwc-accent, var(--primary-color));
            font-size: var(--lwc-type-sm, 0.875rem);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
            line-height: 1.3;
        }

        .documentation-timeline-separator {
            position: absolute;
            width: 1px;
            height: 1px;
            overflow: hidden;
            clip-path: inset(50%);
            white-space: nowrap;
        }

        .documentation-nav a.documentation-nav-timeline {
            display: flex;
            flex-direction: column;
            align-items: flex-start;
            justify-content: center;
            gap: 0.05rem;
        }

        .documentation-nav-entry-date {
            font-size: var(--lwc-type-xs, 0.75rem);
        }

        .documentation-nav a.is-active .documentation-nav-entry-date {
            color: inherit;
        }

        /* Phones: the rail keeps a fixed narrow gutter, so enlarged text keeps its width. */
        @media (max-width: 600px) {
            .documentation-timeline {
                padding-left: 22px;
            }

            .documentation-timeline::before {
                left: 5px;
            }

            .documentation-timeline-entry::before {
                left: -22px;
                width: 12px;
                height: 12px;
                box-shadow: 0 0 0 3px var(--lwc-surface, #ffffff);
            }

            .documentation-timeline-entry + .documentation-timeline-entry {
                margin-top: 1.75rem;
            }
        }
        @media (max-width: 600px) {
            .documentation-document {
                font-size: var(--lwc-type-body);
                line-height: 1.65;
            }
            .documentation-document :is(p,li) {
                line-height: 1.65;
            }
        }
    </style>{{(page.Reference ? ReferencePage.Style : string.Empty)}}
</head>
<body>
    <!--HEADER-->
    <main class="documentation-page {{pageClass}}">
{{contentsHtml}}
        <article class="documentation-document{{(page.Reference ? " ruleset-document" : string.Empty)}}">
{{documentHtml}}
        </article>
    </main>
    <!--FOOTER-->
    <script>
        history.replaceState({}, "", "{{canonicalPath}}" + (window.location.search || "") + (window.location.hash || ""));
        document.addEventListener("DOMContentLoaded", () => {
            const documentationNav = document.querySelector(".documentation-nav");
            const documentationNavToggle = documentationNav?.querySelector(".documentation-nav-toggle");
            const setDocumentationNavOpen = open => {
                if (!documentationNav || !documentationNavToggle) {
                    return;
                }

                documentationNav.classList.toggle("is-open", open);
                documentationNavToggle.setAttribute("aria-expanded", String(open));
                const icon = documentationNavToggle.querySelector(".documentation-nav-toggle-icon");
                if (icon) {
                    icon.textContent = open ? "−" : "+";
                }
            };

            if (documentationNav && documentationNavToggle) {
                documentationNav.classList.add("is-enhanced");
                setDocumentationNavOpen(false);
            }

            documentationNavToggle?.addEventListener("click", () => {
                setDocumentationNavOpen(documentationNavToggle.getAttribute("aria-expanded") !== "true");
            });

            const navLinks = Array.from(document.querySelectorAll(".documentation-nav a[href^='#']"));
            const headings = navLinks
                .map(link => document.getElementById(decodeURIComponent(link.getAttribute("href").slice(1))))
                .filter(Boolean);

            const getOffset = () => {
                const stickyHeader = document.getElementById("site-sticky-header");
                const measuredHeight = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--site-sticky-height"));
                return ((stickyHeader && stickyHeader.offsetHeight) || measuredHeight || 52) + 18;
            };

            let activeLink = null;
            let navigationDestination = null;
            const revealActiveLink = () => {
                const link = documentationNav?.querySelector("a:focus") || activeLink;
                if (!documentationNav || !link || !window.matchMedia("(min-width: 901px)").matches) {
                    return;
                }

                const pane = documentationNav.getBoundingClientRect();
                const target = link.getBoundingClientRect();
                const padding = parseFloat(getComputedStyle(documentationNav).scrollPaddingTop) || 0;
                if (target.top < pane.top + padding) {
                    documentationNav.scrollTop += target.top - pane.top - padding;
                } else if (target.bottom > pane.bottom - padding) {
                    documentationNav.scrollTop += target.bottom - pane.bottom + padding;
                }
            };

            const setActive = id => {
                const previousActiveLink = activeLink;
                activeLink = null;
                navLinks.forEach(link => {
                    const active = decodeURIComponent(link.getAttribute("href").slice(1)) === id;
                    link.classList.toggle("is-active", active);
                    if (active) {
                        activeLink = link;
                        link.setAttribute("aria-current", "location");
                    } else {
                        link.removeAttribute("aria-current");
                    }
                });
                if (activeLink !== previousActiveLink) {
                    revealActiveLink();
                }
            };

            navLinks.forEach(link => {
                link.addEventListener("click", event => {
                    const target = document.getElementById(decodeURIComponent(link.getAttribute("href").slice(1)));
                    if (!target) {
                        return;
                    }

                    event.preventDefault();
                    if (window.matchMedia("(max-width: 900px)").matches) {
                        setDocumentationNavOpen(false);
                    }
                    const top = target.getBoundingClientRect().top + window.scrollY - getOffset();
                    window.scrollTo({ top, behavior: "instant" });
                    history.pushState(null, "", link.getAttribute("href"));
                    navigationDestination = { target, scrollY: window.scrollY, hash: window.location.hash };
                    setActive(target.id);

                    target.setAttribute("tabindex", "-1");
                    target.focus({ preventScroll: true });
                });
            });

            const updateActiveFromScroll = () => {
                const offset = getOffset() + 6;
                // A section near the page end may be visible without reaching the offset.
                // Honor that destination until the reader moves away from its landing position.
                if (navigationDestination && window.scrollY === navigationDestination.scrollY &&
                    window.location.hash === navigationDestination.hash) {
                    const bounds = navigationDestination.target.getBoundingClientRect();
                    if (bounds.bottom > offset && bounds.top < window.innerHeight) {
                        setActive(navigationDestination.target.id);
                        return;
                    }
                }
                navigationDestination = null;
                let current = headings[0];

                for (const heading of headings) {
                    if (heading.getBoundingClientRect().top <= offset) {
                        current = heading;
                    } else {
                        break;
                    }
                }
                if (window.scrollY > 0 && window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 1) {
                    current = headings[headings.length - 1];
                }
                if (current) {
                    setActive(current.id);
                }
            };

            const synchronizeNavigationFromHash = () => {
                let id;
                try {
                    id = decodeURIComponent(window.location.hash.slice(1));
                } catch {
                    id = "";
                }
                const target = headings.find(heading => heading.id === id);
                navigationDestination = null;
                if (target) {
                    const bounds = target.getBoundingClientRect();
                    if (bounds.bottom > getOffset() && bounds.top < window.innerHeight) {
                        navigationDestination = { target, scrollY: window.scrollY, hash: window.location.hash };
                    }
                }
                updateActiveFromScroll();
            };

            let ticking = false;
            window.addEventListener("scroll", () => {
                if (ticking) {
                    return;
                }

                ticking = true;
                window.requestAnimationFrame(() => {
                    updateActiveFromScroll();
                    ticking = false;
                });
            }, { passive: true });
            window.addEventListener("resize", () => {
                updateActiveFromScroll();
                revealActiveLink();
            });
            window.addEventListener("hashchange", () => window.requestAnimationFrame(synchronizeNavigationFromHash));
            window.addEventListener("pageshow", event => {
                if (!event.persisted && window.location.hash) {
                    synchronizeNavigationFromHash();
                }
            });

            updateActiveFromScroll();
            if (window.location.hash) {
                window.requestAnimationFrame(synchronizeNavigationFromHash);
            }
        });
    </script>{{(page.Reference ? ReferencePage.Script : string.Empty)}}
</body>
</html>
""";
}

internal sealed record DocumentationPage(string Source, string Output, string CanonicalPath, string SourceUrl, bool Reference = false);
