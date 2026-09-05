using System.Net;
using System.Text;

namespace LongevityWorldCup.Website.Tools;

internal static class OAuthCallbackPage
{
    private static readonly string[] QueryFields = ["code", "state", "error", "error_description"];

    public static string Render(string provider, IQueryCollection query, string? instructions = null)
    {
        var encodedProvider = WebUtility.HtmlEncode(provider);
        var html = new StringBuilder()
            .AppendLine("<!DOCTYPE html>")
            .AppendLine("<html lang=\"en\">")
            .AppendLine("<head>")
            .AppendLine("    <meta charset=\"utf-8\">")
            .AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .AppendLine($"    <title>{encodedProvider} Callback</title>")
            .AppendLine("    <style>")
            .AppendLine("        * { box-sizing: border-box; }")
            .AppendLine("        body { margin: 0; min-height: 100vh; padding: clamp(1rem, 4vw, 2rem); display: flex; justify-content: center; align-items: flex-start; font-family: system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif; line-height: 1.5; color: #172033; background: #f7f8fb; }")
            .AppendLine("        .callback-card { width: min(100%, 48rem); padding: clamp(1.25rem, 4vw, 2rem); border: 1px solid rgba(23, 32, 51, 0.12); border-radius: 8px; background: #fff; box-shadow: 0 1rem 2.5rem rgba(23, 32, 51, 0.08); }")
            .AppendLine("        h1 { margin: 0 0 1rem; font-size: clamp(1.6rem, 7vw, 2.25rem); line-height: 1.1; }")
            .AppendLine("        p { margin: 0 0 1rem; }")
            .AppendLine("        code { display: inline-block; max-width: 100%; overflow-wrap: anywhere; word-break: break-word; font-family: ui-monospace, SFMono-Regular, Consolas, \"Liberation Mono\", monospace; }")
            .AppendLine("    </style>")
            .AppendLine("</head>")
            .AppendLine("<body>")
            .AppendLine("  <main class=\"callback-card\">")
            .AppendLine($"    <h1>{encodedProvider} callback received.</h1>");

        if (instructions is not null)
            html.AppendLine($"    <p>{WebUtility.HtmlEncode(instructions)}</p>");

        foreach (var field in QueryFields)
        {
            if (query.TryGetValue(field, out var value))
                html.AppendLine($"    <p>{field}: <code>{WebUtility.HtmlEncode(value.ToString())}</code></p>");
        }

        html.AppendLine("  </main>")
            .AppendLine("</body>")
            .AppendLine("</html>");

        return html.ToString();
    }
}
