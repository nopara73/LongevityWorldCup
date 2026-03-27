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
                .AppendLine("</head>")
                .AppendLine("<body>")
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

            html.AppendLine("</body>")
                .AppendLine("</html>");

            return Content(html.ToString(), "text/html; charset=utf-8");
        }
    }
}
