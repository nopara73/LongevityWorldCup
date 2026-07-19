using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

var repoRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Directory.GetCurrentDirectory();
var websiteRoot = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(repoRoot, "LongevityWorldCup.Website");

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
        SourceUrl: "https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/Ruleset.md")
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
            var text = WebUtility.HtmlEncode(StripTags(WebUtility.HtmlDecode(match.Groups["text"].Value)));
            return $"            <a class=\"documentation-nav-level-{level}\" href=\"#{id}\">{text}</a>";
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
            position: sticky;
            top: 4.25rem;
            display: grid;
            gap: 0.2rem;
            padding: 0.85rem 0.7rem 0.95rem;
            border-left: 3px solid var(--lwc-accent, var(--primary-color));
            border-radius: 0 8px 8px 0;
            background: var(--lwc-surface-muted, #eef2f5);
            box-shadow: none;
        }

        .documentation-nav-title {
            margin: 0 0 0.35rem;
            padding: 0 0.45rem;
            color: var(--lwc-muted, #526373);
            font-size: 0.78rem;
            font-weight: 700;
            text-transform: uppercase;
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
            display: block;
            padding: 0.38rem 0.45rem;
            border-radius: 6px;
            color: var(--lwc-ink, #334155);
            font-size: 0.92rem;
            line-height: 1.25;
            text-decoration: none;
        }

        .documentation-nav .documentation-nav-level-3 {
            margin-left: 0.55rem;
            padding-top: 0.28rem;
            padding-bottom: 0.28rem;
            color: var(--lwc-muted, #506176);
            font-size: 0.86rem;
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
            background: var(--lwc-accent-soft, rgba(0, 188, 212, 0.1));
            font-weight: 700;
        }

        .documentation-source {
            margin: 2.25rem 0 0;
            padding-top: 1rem;
            border-top: 1px solid var(--lwc-border, rgba(0, 0, 0, 0.08));
            color: var(--lwc-muted, #506176);
            font-size: 0.95rem;
            text-align: left;
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

            .documentation-nav-toggle {
                box-sizing: border-box;
                display: flex;
                align-items: center;
                justify-content: space-between;
                width: 100%;
                min-height: 44px;
                padding: 0.6rem 0.75rem;
                border: 1px solid var(--lwc-border-strong, #71808d);
                border-radius: 6px;
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
            }

            .documentation-nav-links {
                display: none;
                max-height: min(65svh, 30rem);
                margin-top: 0.35rem;
                overflow-y: auto;
                overscroll-behavior: contain;
            }

            .documentation-nav.is-open .documentation-nav-links {
                display: grid;
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
                font-size: 0.86rem;
                line-height: 1.28;
            }

            .documentation-document tbody td::before {
                content: attr(data-label);
                color: var(--lwc-muted, #526373);
                font-size: 0.68rem;
                font-weight: 800;
                text-transform: uppercase;
                letter-spacing: 0;
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
                white-space: nowrap;
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
    </style>
</head>
<body>
    <!--HEADER-->
    <main class="documentation-page">
{{contentsHtml}}
        <article class="documentation-document">
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

            documentationNavToggle?.addEventListener("click", () => {
                setDocumentationNavOpen(documentationNavToggle.getAttribute("aria-expanded") !== "true");
            });

            const navLinks = Array.from(document.querySelectorAll(".documentation-nav a[href^='#']"));
            const headings = navLinks
                .map(link => document.getElementById(decodeURIComponent(link.getAttribute("href").slice(1))))
                .filter(Boolean);

            const getOffset = () => {
                const stickyHeader = document.getElementById("site-sticky-header");
                return ((stickyHeader && stickyHeader.offsetHeight) || 52) + 18;
            };

            const setActive = id => {
                navLinks.forEach(link => {
                    const active = decodeURIComponent(link.getAttribute("href").slice(1)) === id;
                    link.classList.toggle("is-active", active);
                    if (active) {
                        link.setAttribute("aria-current", "location");
                    } else {
                        link.removeAttribute("aria-current");
                    }
                });
            };

            navLinks.forEach(link => {
                link.addEventListener("click", event => {
                    const target = document.getElementById(decodeURIComponent(link.getAttribute("href").slice(1)));
                    if (!target) {
                        return;
                    }

                    event.preventDefault();
                    const top = target.getBoundingClientRect().top + window.scrollY - getOffset();
                    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
                    window.scrollTo({ top, behavior: reducedMotion ? "auto" : "smooth" });
                    history.pushState(null, "", link.getAttribute("href"));
                    setActive(target.id);
                    if (window.matchMedia("(max-width: 900px)").matches) {
                        setDocumentationNavOpen(false);
                    }

                    target.setAttribute("tabindex", "-1");
                    target.focus({ preventScroll: true });
                });
            });

            const updateActiveFromScroll = () => {
                const offset = getOffset() + 6;
                let current = headings[0];

                for (const heading of headings) {
                    if (heading.getBoundingClientRect().top <= offset) {
                        current = heading;
                    } else {
                        break;
                    }
                }

                if (current) {
                    setActive(current.id);
                }
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

            updateActiveFromScroll();
        });
    </script>
</body>
</html>
""";
}

internal sealed record DocumentationPage(string Source, string Output, string CanonicalPath, string SourceUrl);
