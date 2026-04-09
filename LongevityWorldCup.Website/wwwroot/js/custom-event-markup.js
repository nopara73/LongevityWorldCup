(function () {
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

    function renderMarkup(text) {
        var s = String(text || "");
        var out = "";
        var i = 0;
        while (i < s.length) {
            var open = s.indexOf("[", i);
            if (open < 0) {
                out += escapeHtml(s.slice(i));
                break;
            }

            out += escapeHtml(s.slice(i, open));
            var close = s.indexOf("](", open + 1);
            if (close < 0) {
                out += escapeHtml(s.slice(open));
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
                out += escapeHtml(s.slice(open));
                break;
            }

            var inner = s.slice(parenStart, j - 1);
            var key = label.trim().toLowerCase();
            if (key === "bold") {
                out += '<span style="font-weight:800">' + renderMarkup(inner) + "</span>";
            } else if (key === "strong") {
                out += '<span style="font-weight:800;color:var(--secondary-color,#ff4f87)">' + renderMarkup(inner) + "</span>";
            } else if (isSafeHttpUrl(inner)) {
                out += '<a href="' + escapeHtml(inner.trim()) + '" target="_top" rel="noopener">' + escapeHtml(label) + "</a>";
            } else {
                out += escapeHtml(s.slice(open, j));
            }

            i = j;
        }

        return out;
    }

    function renderMarkupWithBreaks(text) {
        return normalizeNewlines(text || "").split("\n").map(renderMarkup).join("<br>");
    }

    window.CustomEventMarkup = {
        normalizeNewlines: normalizeNewlines,
        splitText: splitText,
        renderMarkup: renderMarkup,
        renderMarkupWithBreaks: renderMarkupWithBreaks
    };
})();
