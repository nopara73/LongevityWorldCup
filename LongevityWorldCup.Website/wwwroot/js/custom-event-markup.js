(function () {
    let mentionResolver = null;

    function escapeHtml(value) {
        return String(value ?? "").replace(/[&<>"']/g, function (ch) {
            return {
                "&": "&amp;",
                "<": "&lt;",
                ">": "&gt;",
                "\"": "&quot;",
                "'": "&#39;"
            }[ch];
        });
    }

    function normalizeNewlines(value) {
        return String(value || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    }

    function splitText(text) {
        var normalized = normalizeNewlines(text);
        var idx = normalized.indexOf("\n\n");
        if (idx >= 0) {
            return { title: normalized.slice(0, idx).trimEnd(), content: normalized.slice(idx + 2) };
        }

        return { title: normalized.trimEnd(), content: "" };
    }

    function isSafeHttpUrl(value) {
        return /^https?:\/\/.+/i.test(String(value || "").trim());
    }

    function humanizeSlug(value) {
        return String(value || "")
            .replace(/[_-]+/g, " ")
            .trim()
            .split(/\s+/)
            .filter(Boolean)
            .map(function (part) { return part.charAt(0).toUpperCase() + part.slice(1); })
            .join(" ");
    }

    function resolveMentionText(slug, overrideResolver) {
        var normalized = String(slug || "").trim();
        if (!normalized) {
            return "";
        }

        var resolver = typeof overrideResolver === "function" ? overrideResolver : mentionResolver;
        var resolved = resolver ? resolver(normalized) : "";
        return resolved ? String(resolved) : (humanizeSlug(normalized) || normalized);
    }

    function walk(text, handlers) {
        var s = String(text || "");
        var out = "";
        var i = 0;
        while (i < s.length) {
            var open = s.indexOf("[", i);
            if (open < 0) {
                out += handlers.text(s.slice(i));
                break;
            }

            out += handlers.text(s.slice(i, open));
            var close = s.indexOf("](", open + 1);
            if (close < 0) {
                out += handlers.text(s.slice(open));
                break;
            }

            var label = s.slice(open + 1, close);
            var parenStart = close + 2;
            var depth = 1;
            var j = parenStart;
            while (j < s.length && depth > 0) {
                var ch = s[j];
                if (ch === "(") depth++;
                else if (ch === ")") depth--;
                j++;
            }

            if (depth !== 0) {
                out += handlers.text(s.slice(open));
                break;
            }

            var inner = s.slice(parenStart, j - 1);
            var key = label.trim().toLowerCase();
            if (key === "bold") {
                out += handlers.bold(inner);
            } else if (key === "strong") {
                out += handlers.strong(inner);
            } else if (key === "mention") {
                out += handlers.mention(inner);
            } else if (isSafeHttpUrl(inner)) {
                out += handlers.link(label, inner);
            } else {
                out += handlers.text(s.slice(open, j));
            }

            i = j;
        }

        return out;
    }

    function renderMarkup(text, options) {
        var opts = options || {};
        return walk(text, {
            text: function (value) { return escapeHtml(value); },
            bold: function (inner) {
                return '<span style="font-weight:800">' + renderMarkup(inner, opts) + "</span>";
            },
            strong: function (inner) {
                return '<span style="font-weight:800;color:var(--secondary-color,#ff4f87)">' + renderMarkup(inner, opts) + "</span>";
            },
            mention: function (slug) {
                var resolvedText = resolveMentionText(slug, opts.mentionResolver);
                if (typeof opts.mentionRenderer === "function") {
                    return String(opts.mentionRenderer(String(slug || "").trim(), resolvedText) || "");
                }
                return escapeHtml(resolvedText);
            },
            link: function (label, href) {
                return '<a href="' + escapeHtml(href.trim()) + '" target="_top" rel="noopener">' + escapeHtml(label) + "</a>";
            }
        });
    }

    function renderMarkupWithBreaks(text, options) {
        return normalizeNewlines(text || "").split("\n").map(function (line) {
            return renderMarkup(line, options);
        }).join("<br>");
    }

    function renderWebpageMarkup(text, options) {
        var opts = options || {};
        return renderMarkup(text, {
            mentionResolver: opts.mentionResolver,
            mentionRenderer: function (slug, displayText) {
                var href = typeof opts.mentionHrefResolver === "function"
                    ? opts.mentionHrefResolver(String(slug || "").trim())
                    : "";

                if (!href) {
                    return escapeHtml(displayText);
                }

                return '<a href="' + escapeHtml(String(href)) + '" target="_blank" rel="noopener noreferrer">' + escapeHtml(displayText) + '</a>';
            }
        });
    }

    function renderWebpageMarkupWithBreaks(text, options) {
        return normalizeNewlines(text || "").split("\n").map(function (line) {
            return renderWebpageMarkup(line, options);
        }).join("<br>");
    }

    function toPlainText(text, options) {
        var opts = options || {};
        var keepHyperlinkLabels = opts.keepHyperlinkLabels !== false;
        return walk(text, {
            text: function (value) { return value; },
            bold: function (inner) { return toPlainText(inner, opts); },
            strong: function (inner) { return toPlainText(inner, opts); },
            mention: function (slug) { return resolveMentionText(slug, opts.mentionResolver); },
            link: function (label) { return keepHyperlinkLabels ? label : ""; }
        });
    }

    function setMentionResolver(resolver) {
        mentionResolver = typeof resolver === "function" ? resolver : null;
    }

    window.CustomEventMarkup = {
        normalizeNewlines: normalizeNewlines,
        splitText: splitText,
        renderMarkup: renderMarkup,
        renderMarkupWithBreaks: renderMarkupWithBreaks,
        renderWebpageMarkup: renderWebpageMarkup,
        renderWebpageMarkupWithBreaks: renderWebpageMarkupWithBreaks,
        toPlainText: toPlainText,
        setMentionResolver: setMentionResolver
    };
})();
