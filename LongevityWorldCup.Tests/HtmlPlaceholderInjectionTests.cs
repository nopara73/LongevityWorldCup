using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HtmlPlaceholderInjectionTests
{
    private static readonly string[] CanonicalHtmlRoutes =
    [
        "/",
        "/events",
        "/leaderboard",
        "/longevitymaxxing",
        "/helstab-kihivas",
        "/media",
        "/about",
        "/history",
        "/ruleset",
        "/privacy",
        "/play",
        "/join",
        "/apply",
        "/review",
        "/proofs",
        "/select-athlete",
        "/dashboard",
        "/edit-profile",
        "/unsubscribe",
        "/pheno-age",
        "/bortz-age"
    ];

    private static readonly string[] TopLevelCompositionMarkers =
    [
        "<!--HEAD-->",
        "<!--HEADER-->",
        "<!--FOOTER-->",
        "<!--HOMEPAGE-SCOREBOARD-->",
        "<!--MAIN-PROGRESS-BAR-->",
        "<!--SUB-PROGRESS-BAR-->",
        "<!--LEADERBOARD-CONTENT-->",
        "<!--GUESS-MY-AGE-->",
        "<!--EVENT-BOARD-CONTENT-->",
        "<!--AGE-VISUALIZATION-->"
    ];

    [Fact]
    public async Task CanonicalHtmlRoutes_DoNotLeakCompositionPlaceholders()
    {
        using var factory = new TestWebApplicationFactory(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders()));
        using var client = factory.CreateClient();

        foreach (var path in CanonicalHtmlRoutes)
        {
            using var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

            var unresolvedToken = Regex.Match(
                html,
                @"\{\{[A-Z][A-Z0-9_-]*\}\}",
                RegexOptions.CultureInvariant);
            Assert.False(
                unresolvedToken.Success,
                $"Route {path} leaked unresolved token {unresolvedToken.Value}.");

            foreach (var marker in TopLevelCompositionMarkers)
            {
                Assert.False(
                    html.Contains(marker, StringComparison.Ordinal),
                    $"Route {path} leaked top-level composition marker {marker}.");
            }
        }
    }
}
