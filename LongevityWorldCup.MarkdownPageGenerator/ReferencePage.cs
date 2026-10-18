using System.Text;
using System.Text.RegularExpressions;

// A reference page (the Ruleset) reads as a numbered rulebook followed by an FAQ:
// the sections before the FAQ are numbered rules, each FAQ group is a card of
// questions that open in place, and the closing links become tiles. Headings, ids,
// wording and links stay exactly as the Markdown renders them; the page only gains
// wrappers and classes. Without scripts every answer stays open.
internal static partial class ReferencePage
{
    public static string BuildDocument(string documentHtml)
    {
        documentHtml = LedeParagraph().Replace(documentHtml, "${heading}\n<p class=\"ruleset-lede\">", 1);

        var sections = SectionHeading().Matches(documentHtml);
        if (sections.Count == 0)
        {
            return documentHtml;
        }

        var output = new StringBuilder();
        output.Append(documentHtml, 0, sections[0].Index);
        var faqSeen = false;
        for (var i = 0; i < sections.Count; i++)
        {
            var start = sections[i].Index;
            var end = i + 1 < sections.Count ? sections[i + 1].Index : documentHtml.Length;
            var section = documentHtml[start..end];
            var trailing = TrailingWhitespace().Match(section).Value;
            section = section[..^trailing.Length];

            string kind;
            if (section.Contains("<h3", StringComparison.Ordinal))
            {
                kind = "ruleset-faq";
                faqSeen = true;
                section = BuildFaq(section);
            }
            else
            {
                kind = faqSeen ? "ruleset-next" : "ruleset-rule";
                if (!faqSeen)
                {
                    section = SectionHeading().Replace(section,
                        "${start}<span class=\"ruleset-rule-title\">${title}</span></h2>", 1);
                }
            }

            output.Append($"<section class=\"ruleset-section {kind}\">\n{section}\n</section>{trailing}");
        }

        return output.ToString();
    }

    // Rule headings in the contents get the same numbers as the headings themselves.
    public static string BuildContents(string contentsHtml, string documentHtml)
    {
        foreach (Match rule in RuleHeading().Matches(documentHtml))
        {
            contentsHtml = contentsHtml.Replace(
                $"<a class=\"documentation-nav-level-2\" href=\"#{rule.Groups["id"].Value}\">",
                $"<a class=\"documentation-nav-level-2 documentation-nav-rule\" href=\"#{rule.Groups["id"].Value}\">",
                StringComparison.Ordinal);
        }

        return contentsHtml;
    }

    private static string BuildFaq(string section)
    {
        var groups = GroupHeading().Matches(section);
        var output = new StringBuilder();
        output.Append(section, 0, groups[0].Index);
        for (var i = 0; i < groups.Count; i++)
        {
            var start = groups[i].Index;
            var end = i + 1 < groups.Count ? groups[i + 1].Index : section.Length;
            var group = section[start..end].TrimEnd();
            output.Append($"<div class=\"ruleset-faq-group\">\n{BuildQuestions(group)}\n</div>\n");
        }

        return output.ToString().TrimEnd();
    }

    private static string BuildQuestions(string group)
    {
        var questions = QuestionHeading().Matches(group);
        if (questions.Count == 0)
        {
            return group;
        }

        var output = new StringBuilder();
        output.Append(group, 0, questions[0].Index);
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var end = i + 1 < questions.Count ? questions[i + 1].Index : group.Length;
            var answer = TermList().Replace(group[(question.Index + question.Length)..end].Trim(), "<ul class=\"ruleset-term-list\">${items}</ul>");
            var id = question.Groups["id"].Value;
            output.Append($"<div class=\"ruleset-faq-item\">\n{question.Value}\n<div class=\"ruleset-faq-answer\" id=\"{id}-answer\">\n{answer}\n</div>\n</div>\n");
        }

        return output.ToString().TrimEnd();
    }

    public const string Style = """

    <style>
        .documentation-document .ruleset-lede {
            margin-bottom: 0;
            color: var(--lwc-ink);
            font-size: clamp(1.12rem, 1.6vw, 1.28rem);
            line-height: 1.55;
        }

        .documentation-document {
            counter-reset: ruleset-rule;
        }

        .ruleset-section {
            margin-top: clamp(2.2rem, 4vw, 3rem);
        }

        .documentation-document .ruleset-section > h2 {
            margin-top: 0;
        }

        /* Rules are numbered like a rulebook; the number sits in a solid accent tile. */
        .documentation-document .ruleset-rule > h2 {
            display: flex;
            flex-wrap: wrap;
            align-items: center;
            gap: 0.5rem 0.85rem;
            counter-increment: ruleset-rule;
        }

        /* Collect flex lines using the longest word, not the entire title.
           If even that word exceeds a full line, break-word can still reflow it. */
        .ruleset-rule-title {
            flex: 1 1 min-content;
            min-width: 0;
            max-width: 100%;
        }

        .documentation-document .ruleset-rule > h2::before {
            content: counter(ruleset-rule);
            display: inline-grid;
            flex: 0 0 auto;
            place-items: center;
            width: 2.6rem;
            height: 2.6rem;
            border-radius: var(--lwc-radius-md);
            background: var(--lwc-accent);
            color: var(--lwc-on-accent);
            font-family: Orbitron, Roboto, sans-serif;
            font-size: 1rem;
            font-weight: 700;
            letter-spacing: 0.02em;
            line-height: 1;
        }

        .documentation-nav {
            counter-reset: ruleset-nav-rule;
        }

        .documentation-nav a.documentation-nav-rule {
            gap: 0.55rem;
            counter-increment: ruleset-nav-rule;
        }

        .documentation-nav a.documentation-nav-rule::before {
            content: counter(ruleset-nav-rule);
            flex: 0 0 auto;
            min-width: 1rem;
            color: var(--lwc-accent);
            font-family: Orbitron, Roboto, sans-serif;
            font-size: var(--lwc-type-xs);
            font-weight: 700;
        }

        .documentation-nav a.documentation-nav-rule.is-active::before {
            color: inherit;
        }

        /* Each rule is a clause with a square accent marker; sub-clauses hang off a rail. */
        .documentation-document .ruleset-rule ul {
            padding-left: 0;
            list-style: none;
        }

        .documentation-document .ruleset-rule li {
            position: relative;
            margin: 0.7rem 0;
            padding-left: 1.55rem;
        }

        .documentation-document .ruleset-rule li::before {
            content: "";
            position: absolute;
            top: 0.62em;
            left: 0.2rem;
            width: 0.5rem;
            height: 0.5rem;
            border-radius: 2px;
            background: var(--lwc-accent);
        }

        .documentation-document .ruleset-rule li ul {
            margin: 0.55rem 0 0.2rem;
            padding: 0.1rem 0 0.1rem 0.95rem;
            border-left: 2px solid var(--lwc-border);
        }

        .documentation-document .ruleset-rule li li {
            margin: 0.5rem 0;
            padding-left: 1.2rem;
        }

        .documentation-document .ruleset-rule li li::before {
            top: 0.68em;
            left: 0.1rem;
            width: 0.42rem;
            height: 0.42rem;
            border: 2px solid var(--lwc-accent);
            background: transparent;
            box-sizing: border-box;
        }

        /* FAQ: every group is a card of questions; answers open in place. */
        .ruleset-faq-group {
            margin: 1.4rem 0 0;
            border: 1px solid var(--lwc-border);
            border-radius: var(--lwc-radius-lg);
            background: var(--lwc-surface);
            box-shadow: var(--lwc-shadow-sm);
            overflow: hidden;
        }

        .documentation-document .ruleset-faq-group > h3 {
            margin: 0;
            padding: 0.9rem 1.15rem;
            border-bottom: 1px solid var(--lwc-border);
            background: var(--lwc-surface-muted);
            font-size: clamp(1.12rem, 2vw, 1.3rem);
        }

        .ruleset-faq-item {
            display: grid;
            grid-template-columns: minmax(0, 1fr) auto;
            align-items: start;
            column-gap: 0.5rem;
            border-top: 1px solid var(--lwc-border);
        }

        .ruleset-faq-group > h3 + .ruleset-faq-item {
            border-top: 0;
        }

        .documentation-document .ruleset-faq-item > h4 {
            grid-column: 1;
            margin: 0;
            padding: 0.85rem 0 0.85rem 1.15rem;
            font-size: 1.04rem;
            line-height: 1.4;
        }

        .ruleset-faq-answer {
            grid-column: 1 / -1;
            padding: 0 1.15rem 0.35rem;
        }

        .ruleset-faq-answer[hidden] {
            padding-top: 0;
            padding-bottom: 0;
        }

        .ruleset-faq-answer > :last-child {
            margin-bottom: 0.85rem;
        }

        .documentation-document .ruleset-faq-answer img {
            margin: 1rem auto 1.2rem;
        }

        .ruleset-faq-toggle {
            display: none;
        }

        .ruleset-faq.is-enhanced .ruleset-faq-item > h4 {
            cursor: pointer;
        }

        .ruleset-faq.is-enhanced .ruleset-faq-toggle {
            box-sizing: border-box;
            display: inline-grid;
            grid-column: 2;
            grid-row: 1;
            place-items: center;
            width: 2.75rem;
            height: 2.75rem;
            margin: 0.35rem 0.55rem 0.35rem 0;
            padding: 0;
            border: 1px solid transparent;
            border-radius: var(--lwc-radius-pill);
            background: transparent;
            color: var(--lwc-accent);
            font: inherit;
            cursor: pointer;
        }

        .ruleset-faq-toggle::before {
            content: "";
            width: 0.6rem;
            height: 0.6rem;
            margin-top: -0.3rem;
            border-right: 2.5px solid currentColor;
            border-bottom: 2.5px solid currentColor;
            transform: rotate(45deg);
            transition: transform var(--lwc-duration-normal) var(--lwc-ease), margin var(--lwc-duration-normal) var(--lwc-ease);
        }

        .ruleset-faq-item.is-open .ruleset-faq-toggle::before {
            margin-top: 0.3rem;
            transform: rotate(-135deg);
        }

        .ruleset-faq-item:hover,
        .ruleset-faq-item:focus-within {
            background: color-mix(in srgb, var(--lwc-accent-soft) 45%, transparent);
        }

        .ruleset-faq-item.is-open {
            background: transparent;
            box-shadow: inset 3px 0 0 var(--lwc-accent);
        }

        .ruleset-faq-item:hover .ruleset-faq-toggle,
        .ruleset-faq-toggle:focus-visible {
            border-color: var(--lwc-accent);
            background: var(--lwc-accent-soft);
        }

        .ruleset-faq-toggle:focus-visible {
            outline: 3px solid var(--lwc-accent-bright);
            outline-offset: 1px;
        }

        .ruleset-faq-item.is-open > h4 {
            color: var(--lwc-accent-hover);
        }

        /* Lists of lab names read as a grid of chips. */
        .documentation-document .ruleset-term-list {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(min(100%, 14rem), 1fr));
            gap: 0.45rem;
            padding: 0;
            list-style: none;
        }

        .documentation-document .ruleset-term-list li {
            margin: 0;
            padding: 0.45rem 0.7rem;
            border: 1px solid var(--lwc-border);
            border-left: 3px solid var(--lwc-accent);
            border-radius: var(--lwc-radius-sm);
            background: var(--lwc-surface-muted);
            font-size: var(--lwc-type-sm);
            line-height: 1.4;
        }

        @media (max-width: 600px) {
            .documentation-document.ruleset-document {
                padding: min(1.1rem, 18px);
            }

            .documentation-document .ruleset-rule > h2 {
                gap: 0.5rem 0.7rem;
            }

            .documentation-document .ruleset-rule > h2::before {
                width: 2.25rem;
                height: 2.25rem;
                font-size: 0.9rem;
            }

            .ruleset-faq-group {
                margin-right: -0.35rem;
                margin-left: -0.35rem;
            }

            .documentation-document .ruleset-faq-group > h3 {
                padding: 0.8rem 0.9rem;
            }

            .documentation-document .ruleset-faq-item > h4 {
                padding-left: 0.9rem;
                font-size: 1rem;
            }

            .ruleset-faq-answer {
                padding-right: 0.9rem;
                padding-left: 0.9rem;
            }

            .ruleset-faq.is-enhanced .ruleset-faq-toggle {
                margin-right: 0.3rem;
                max-width: 56px;
                max-height: 56px;
            }
        }

        @media (prefers-reduced-motion: reduce) {
            .ruleset-faq-toggle::before {
                transition: none;
            }
        }
    </style>
""";

    public const string Script = """

    <script>
        document.addEventListener("DOMContentLoaded", () => {
            const faq = document.querySelector(".ruleset-faq");
            if (!faq || faq.classList.contains("is-enhanced")) {
                return;
            }

            const items = Array.from(faq.querySelectorAll(".ruleset-faq-item"));
            // Browsers without hidden-until-found support keep the answers readable
            // and searchable instead of treating the value as ordinary hidden.
            if (!("onbeforematch" in document.body)) {
                return;
            }
            const setOpen = (item, open) => {
                const answer = item.querySelector(".ruleset-faq-answer");
                if (!open && answer.contains(document.activeElement)) {
                    item.querySelector(".ruleset-faq-toggle")?.focus({ preventScroll: true });
                }
                item.classList.toggle("is-open", open);
                item.querySelector(".ruleset-faq-toggle")?.setAttribute("aria-expanded", String(open));
                if (open) {
                    answer.removeAttribute("hidden");
                } else {
                    answer.setAttribute("hidden", "until-found");
                }
            };

            items.forEach(item => {
                const question = item.querySelector("h4");
                const answer = item.querySelector(".ruleset-faq-answer");
                const toggle = document.createElement("button");
                toggle.type = "button";
                toggle.className = "ruleset-faq-toggle";
                toggle.setAttribute("aria-controls", answer.id);
                toggle.setAttribute("aria-labelledby", question.id);
                question.after(toggle);
                toggle.addEventListener("click", () => setOpen(item, !item.classList.contains("is-open")));
                question.addEventListener("click", event => {
                    if (!event.target.closest("a") && !window.getSelection()?.toString()) {
                        toggle.focus({ preventScroll: true });
                        setOpen(item, !item.classList.contains("is-open"));
                    }
                });
                // Find in page reveals a collapsed answer.
                answer.addEventListener("beforematch", () => setOpen(item, true));
                setOpen(item, false);
            });
            faq.classList.add("is-enhanced");

            // A link to a question opens its answer and lands on it.
            const openFromHash = () => {
                let id;
                try {
                    id = decodeURIComponent(window.location.hash.slice(1));
                } catch {
                    return;
                }
                const target = id ? document.getElementById(id) : null;
                const item = target?.closest(".ruleset-faq-item");
                if (!item) {
                    return;
                }
                setOpen(item, true);
                const stickyHeader = document.getElementById("site-sticky-header");
                const measuredHeight = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--site-sticky-height"));
                const offset = ((stickyHeader && stickyHeader.offsetHeight) || measuredHeight || 52) + 18;
                window.scrollTo({ top: target.getBoundingClientRect().top + window.scrollY - offset, behavior: "instant" });
            };
            window.addEventListener("hashchange", openFromHash);
            openFromHash();
        });
    </script>
""";

    [GeneratedRegex("(?<heading><h1 id=\"[^\"]+\">.*?</h1>)\\s*<p>", RegexOptions.Singleline)]
    private static partial Regex LedeParagraph();

    [GeneratedRegex("(?<start><h2 id=\"[^\"]+\">)(?<title>.*?)</h2>", RegexOptions.Singleline)]
    private static partial Regex SectionHeading();

    [GeneratedRegex("<section class=\"ruleset-section ruleset-rule\">\\s*<h2 id=\"(?<id>[^\"]+)\">")]
    private static partial Regex RuleHeading();

    [GeneratedRegex("<h3 id=\"[^\"]+\">")]
    private static partial Regex GroupHeading();

    [GeneratedRegex("<h4 id=\"(?<id>[^\"]+)\">.*?</h4>", RegexOptions.Singleline)]
    private static partial Regex QuestionHeading();

    // A list whose items are all plain text, such as lab marker names.
    [GeneratedRegex("<ul>(?<items>(?:\\s*<li>[^<>]+</li>)+\\s*)</ul>")]
    private static partial Regex TermList();

    [GeneratedRegex("\\s*$")]
    private static partial Regex TrailingWhitespace();
}
