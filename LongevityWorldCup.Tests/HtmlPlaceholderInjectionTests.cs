using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The homepage hero once shipped raw {{HOME_HERO_*}} tokens because a stale
/// binary lacked the middleware replacement step. These tests pin the contract:
/// any page served through HtmlInjectionMiddleware must never expose an
/// unreplaced {{TOKEN}} or partial comment marker to the client.
/// </summary>
public sealed partial class HtmlPlaceholderInjectionTests
{
    [GeneratedRegex(@"\{\{[A-Z0-9_]+\}\}")]
    private static partial Regex UnreplacedTokenRegex();

    [Theory]
    [InlineData("/")]
    [InlineData("/onboarding/join-game.html")]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/play/menu.html")]
    [InlineData("/misc-pages/about.html")]
    public async Task InjectedPages_ServeNoUnreplacedPlaceholders(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        var tokenMatch = UnreplacedTokenRegex().Match(html);
        Assert.False(tokenMatch.Success, $"Unreplaced placeholder '{tokenMatch.Value}' served on {path}.");

        Assert.DoesNotContain("<!--HEAD-->", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--HEADER-->", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--FOOTER-->", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Homepage_HeroStats_RenderNumbersEvenWithoutWarmSnapshot()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        var scoreMatch = Regex.Match(html, @"<span class=""lwc-home-hero-score"">([^<]+)<");
        Assert.True(scoreMatch.Success, "Hero score span not found on homepage.");
        Assert.Matches(@"^-?\d+(\.\d+)?$", scoreMatch.Groups[1].Value.Trim());
    }
}
