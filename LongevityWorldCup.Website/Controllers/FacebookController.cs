using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("facebook")]
    public class FacebookController(ILogger<FacebookController> logger) : Controller
    {
        private readonly ILogger<FacebookController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        [HttpGet("callback")]
        public IActionResult Callback()
        {
            _logger.LogInformation("Facebook callback hit.");

            var html = new StringBuilder()
                .AppendLine("<!DOCTYPE html>")
                .AppendLine("<html lang=\"en\">")
                .AppendLine("<head>")
                .AppendLine("    <meta charset=\"utf-8\">")
                .AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                .AppendLine("    <title>Facebook Callback</title>")
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
                .AppendLine("    <h1>Facebook callback received.</h1>")
                .AppendLine("    <p>Copy the full URL from your browser address bar and paste it into the Facebook OAuth helper.</p>");

            if (Request.Query.TryGetValue("code", out var code))
                html.AppendLine($"    <p>code: <code>{System.Net.WebUtility.HtmlEncode(code.ToString())}</code></p>");

            if (Request.Query.TryGetValue("state", out var state))
                html.AppendLine($"    <p>state: <code>{System.Net.WebUtility.HtmlEncode(state.ToString())}</code></p>");

            if (Request.Query.TryGetValue("error", out var error))
                html.AppendLine($"    <p>error: <code>{System.Net.WebUtility.HtmlEncode(error.ToString())}</code></p>");

            if (Request.Query.TryGetValue("error_description", out var errorDescription))
                html.AppendLine($"    <p>error_description: <code>{System.Net.WebUtility.HtmlEncode(errorDescription.ToString())}</code></p>");

            html.AppendLine("  </main>")
                .AppendLine("</body>")
                .AppendLine("</html>");

            return Content(html.ToString(), "text/html; charset=utf-8");
        }
    }
}
