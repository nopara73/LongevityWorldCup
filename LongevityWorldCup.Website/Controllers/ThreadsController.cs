using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace LongevityWorldCup.Website.Controllers
{
    [Route("threads")]
    public class ThreadsController(ILogger<ThreadsController> logger) : Controller
    {
        private readonly ILogger<ThreadsController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        [HttpGet("callback")]
        public IActionResult Callback()
        {
            var html = new StringBuilder()
                .AppendLine("<!DOCTYPE html>")
                .AppendLine("<html lang=\"en\">")
                .AppendLine("<head>")
                .AppendLine("    <meta charset=\"utf-8\">")
                .AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                .AppendLine("    <title>Threads Callback</title>")
                .AppendLine("    <style>")
                .AppendLine("        * { box-sizing: border-box; }")
                .AppendLine("        body { margin: 0; padding: clamp(1rem, 4vw, 2rem); max-width: 48rem; font-family: system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif; line-height: 1.5; color: #172033; background: #f7f8fb; }")
                .AppendLine("        h1 { margin: 0 0 1rem; font-size: clamp(1.6rem, 7vw, 2.25rem); line-height: 1.1; }")
                .AppendLine("        p { margin: 0 0 1rem; }")
                .AppendLine("        code { display: inline-block; max-width: 100%; overflow-wrap: anywhere; word-break: break-word; font-family: ui-monospace, SFMono-Regular, Consolas, \"Liberation Mono\", monospace; }")
                .AppendLine("    </style>")
                .AppendLine("</head>")
                .AppendLine("<body>")
                .AppendLine("    <h1>Threads callback received.</h1>");

            if (Request.Query.TryGetValue("code", out var code))
                html.AppendLine($"    <p>code: <code>{System.Net.WebUtility.HtmlEncode(code.ToString())}</code></p>");

            if (Request.Query.TryGetValue("state", out var state))
                html.AppendLine($"    <p>state: <code>{System.Net.WebUtility.HtmlEncode(state.ToString())}</code></p>");

            if (Request.Query.TryGetValue("error", out var error))
                html.AppendLine($"    <p>error: <code>{System.Net.WebUtility.HtmlEncode(error.ToString())}</code></p>");

            if (Request.Query.TryGetValue("error_description", out var errorDescription))
                html.AppendLine($"    <p>error_description: <code>{System.Net.WebUtility.HtmlEncode(errorDescription.ToString())}</code></p>");

            html.AppendLine("</body>")
                .AppendLine("</html>");

            return Content(html.ToString(), "text/html; charset=utf-8");
        }

        [HttpGet("uninstall")]
        [HttpPost("uninstall")]
        public IActionResult Uninstall()
        {
            _logger.LogInformation("Threads uninstall callback hit.");
            return Content("Threads uninstall callback received.", "text/plain");
        }

        [HttpGet("delete")]
        [HttpPost("delete")]
        public IActionResult Delete()
        {
            _logger.LogInformation("Threads delete callback hit.");
            return Json(new
            {
                status = "received",
                timestampUtc = DateTime.UtcNow.ToString("o")
            });
        }
    }
}
