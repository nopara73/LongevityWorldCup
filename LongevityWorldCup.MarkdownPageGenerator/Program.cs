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
    var documentHtml = OpenAbsoluteLinksInNewTabs(RewriteWebsiteAssetImageSources(Markdown.ToHtml(markdown, pipeline)));
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
            <a class="documentation-source-link" href="{source}" target="_blank" rel="noopener">Public source</a>
        </nav>
""";
    }

    return $$"""
        <nav class="documentation-nav" aria-label="Document navigation">
            <div class="documentation-nav-title">Official index</div>
{{string.Join(Environment.NewLine, links)}}
            <a class="documentation-source-link" href="{{source}}" target="_blank" rel="noopener">Public source</a>
        </nav>
""";
}

static string StripTags(string value)
{
    return Regex.Replace(value, "<.*?>", string.Empty).Trim();
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
            border: 1px solid var(--surface-border);
            border-radius: var(--radius-panel, 10px);
            background:
                linear-gradient(180deg, var(--surface-green-tint), rgba(255, 254, 250, 0) 36%),
                var(--card-bg);
            box-shadow: var(--surface-shadow);
        }

        .documentation-nav-title {
            margin: 0 0 0.35rem;
            padding: 0 0.45rem;
            color: var(--primary-color);
            font-family: var(--display-font);
            font-size: 0.78rem;
            font-weight: 400;
            font-synthesis: none;
            letter-spacing: 0.08em;
            text-transform: uppercase;
        }

        .documentation-nav a {
            display: block;
            padding: 0.38rem 0.45rem;
            border-radius: 6px;
            color: var(--text-soft, #33423b);
            font-size: var(--text-ui, 0.9375rem);
            line-height: var(--leading-ui, 1.35);
            text-decoration: none;
        }

        .documentation-nav .documentation-nav-level-3 {
            margin-left: 0.55rem;
            padding-top: 0.28rem;
            padding-bottom: 0.28rem;
            color: var(--text-muted, #46554d);
            font-size: var(--text-small, 0.875rem);
        }

        .documentation-nav a:hover,
        .documentation-nav a:focus-visible,
        .documentation-nav a.is-active {
            background: var(--surface-green-tint-strong);
            color: var(--primary-color);
            outline: none;
        }

        .documentation-nav a.is-active {
            font-weight: 700;
        }

        .documentation-nav .documentation-source-link {
            margin-top: 0.65rem;
            padding-top: 0.75rem;
            border-top: 1px solid var(--surface-border);
            color: var(--primary-color);
            font-weight: 900;
        }

        .documentation-document {
            position: relative;
            box-sizing: border-box;
            padding: clamp(1.35rem, 3vw, 2.2rem);
            border: 1px solid var(--surface-border);
            border-radius: var(--radius-panel, 10px);
            background:
                linear-gradient(180deg, var(--surface-row-alt), rgba(255, 254, 250, 0) 22%),
                var(--card-bg);
            box-shadow: var(--surface-shadow);
            color: var(--text-soft, #33423b);
            font-size: var(--text-body, 1rem);
            line-height: var(--leading-reading, 1.68);
            overflow-wrap: break-word;
        }

        .documentation-document::before {
            content: "Official competition dossier";
            display: block;
            margin: 0 0 0.55rem;
            color: var(--primary-color);
            font-size: var(--text-caption, 0.75rem);
            font-weight: 900;
            letter-spacing: 0.12em;
            text-transform: uppercase;
        }

        .documentation-document h1,
        .documentation-document h2,
        .documentation-document h3,
        .documentation-document h4 {
            color: var(--ink);
            font-family: var(--display-font);
            font-weight: 400;
            font-synthesis: none;
            line-height: var(--leading-tight, 1.12);
            overflow-wrap: anywhere;
            text-wrap: balance;
            text-transform: uppercase;
        }

        .documentation-document h1 {
            margin: 0 0 1.25rem;
            padding: 0 0 1rem;
            border-bottom: 1px solid var(--surface-border-strong);
            text-align: left;
            font-size: clamp(2.1rem, 4vw, 2.85rem);
            letter-spacing: var(--tracking-display, 0.02em);
            text-shadow: none;
        }

        .documentation-document h2 {
            margin: 2.45rem 0 0.85rem;
            padding-top: 1.05rem;
            border-top: 1px solid var(--surface-border-strong);
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
            max-width: 68ch;
            margin: 0 0 1rem;
            text-wrap: pretty;
        }

        .documentation-document ul,
        .documentation-document ol {
            padding-left: 1.5rem;
        }

        .documentation-document li {
            margin: 0.44rem 0;
            padding-left: 0.1rem;
            line-height: var(--leading-reading, 1.68);
        }

        .documentation-document a {
            color: var(--primary-color);
            font-weight: 700;
            text-decoration-thickness: 0.08em;
            text-underline-offset: 0.14em;
        }

        .documentation-document a:hover,
        .documentation-document a:focus-visible {
            color: #0a5c36;
        }

        .documentation-document img {
            box-sizing: border-box;
            display: block;
            max-width: 100%;
            max-height: 560px;
            width: min(100%, 48rem);
            height: auto;
            object-fit: contain;
            margin: 1.45rem auto 1.8rem;
            border: 1px solid var(--surface-border-strong);
            border-radius: 8px;
            background: var(--card-bg);
            box-shadow: none;
        }

        .documentation-document table {
            display: block;
            width: 100%;
            margin: 1.25rem 0 1.5rem;
            overflow-x: auto;
            border-collapse: collapse;
            background: var(--card-bg);
            border: 1px solid var(--surface-border);
            border-radius: 8px;
        }

        .documentation-document th,
        .documentation-document td {
            padding: 0.62rem 0.75rem;
            border-bottom: 1px solid var(--surface-border);
            text-align: left;
            vertical-align: top;
            white-space: nowrap;
        }

        .documentation-document th {
            color: var(--primary-color);
            background: var(--surface-green-tint);
            font-weight: 900;
            text-transform: uppercase;
            letter-spacing: 0.04em;
        }

        .documentation-source {
            margin: 2.25rem 0 0;
            padding-top: 1rem;
            border-top: 1px solid var(--surface-border);
            color: var(--text-muted, #46554d);
            font-size: var(--text-ui, 0.9375rem);
            line-height: var(--leading-body, 1.55);
            text-align: left;
        }

        @media (max-width: 900px) {
            .documentation-page {
                display: block;
                margin: 2rem auto 3rem;
                padding: 0 1rem;
            }

            .documentation-nav {
                position: static;
                display: flex;
                flex-wrap: wrap;
                gap: 0.4rem;
                align-items: center;
                margin: 0 0 1rem;
                padding: 0.72rem;
                border: 1px solid var(--surface-border);
                border-radius: var(--radius-panel, 10px);
                max-height: min(17rem, 38svh);
                overflow-y: auto;
                overscroll-behavior: contain;
                scrollbar-width: thin;
                scrollbar-color: rgba(11, 125, 69, 0.45) transparent;
            }

            .documentation-nav-title {
                display: block;
                flex: 1 0 100%;
                margin: 0 0 0.18rem;
                padding: 0 0 0.45rem;
                border-bottom: 1px solid var(--surface-border);
                color: var(--primary-color);
                font-size: var(--text-caption, 0.75rem);
                letter-spacing: 0.14em;
            }

            .documentation-nav a {
                /* Long timeline entries must wrap inside the chip instead of
                   running off the right edge of narrow viewports. */
                flex: 1 1 10.5rem;
                max-width: 100%;
                min-height: 2.28rem;
                display: inline-flex;
                align-items: center;
                border: 1px solid var(--surface-border);
                background: var(--card-bg);
                font-size: var(--text-ui, 0.9375rem);
                white-space: normal;
                overflow-wrap: anywhere;
            }

            .documentation-nav .documentation-nav-level-3 {
                margin-left: 0;
                font-size: var(--text-ui, 0.9375rem);
            }

            .documentation-nav .documentation-source-link {
                margin-top: 0;
                border-color: transparent;
                background: var(--surface-green-tint);
            }
        }

        @media (max-width: 600px) {
            .documentation-document {
                padding: 1.1rem;
                font-size: var(--text-body, 1rem);
                line-height: var(--leading-reading, 1.68);
            }

            .documentation-document h1 {
                font-size: 1.95rem;
                line-height: 1.02;
            }

            .documentation-document h2 {
                font-size: 1.45rem;
                line-height: 1.08;
            }

            .documentation-document img {
                margin: 1.15rem auto 1.45rem;
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
