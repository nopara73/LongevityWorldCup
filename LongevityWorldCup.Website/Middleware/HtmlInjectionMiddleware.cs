﻿namespace LongevityWorldCup.Website.Middleware
{
    public class HtmlInjectionMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value;
            if (path == "/" || path?.EndsWith(".html") is true)
            {
                string filePath;

                if (path == "/")
                {
                    filePath = Path.Combine("wwwroot", "index.html");
                }
                else
                {
                    filePath = Path.Combine("wwwroot", path.TrimStart('/'));
                }

                if (File.Exists(filePath))
                {
                    // Read the main HTML file
                    var bodyContent = await File.ReadAllTextAsync(filePath);

                    // Read the header and footer files
                    var head = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "head.html"));
                    var header = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "header.html"));
                    var footer = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "footer.html"));
                    var progressBar = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "main-progress-bar.html"));
                    var subProgressBar = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "sub-progress-bar.html"));
                    var leaderboardContent = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "leaderboard-content.html"));

                    // Replace placeholders with header and footer content
                    bodyContent = bodyContent
                        .Replace("<!--HEAD-->", head)
                        .Replace("<!--HEADER-->", header)
                        .Replace("<!--FOOTER-->", footer)
                        .Replace("<!--MAIN-PROGRESS-BAR-->", progressBar)
                        .Replace("<!--SUB-PROGRESS-BAR-->", subProgressBar)
                        .Replace("<!--LEADERBOARD-CONTENT-->", leaderboardContent);

                    // Optionally remove the play button on certain pages
                    if (path.Contains("join-sport"))
                    {
                        bodyContent = bodyContent.Replace("<button class=\"join-game\">", "<!-- Removed Join Sport Button -->");
                    }

                    // Write the modified content to the response
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(bodyContent);

                    // Short-circuit the pipeline
                    return;
                }
            }

            // For all other requests, continue down the pipeline
            await _next(context);
        }
    }
}