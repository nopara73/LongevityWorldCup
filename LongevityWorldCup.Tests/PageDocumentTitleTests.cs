using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PageDocumentTitleTests
{
    [Theory]
    [InlineData("/privacy", "Privacy Policy | Longevity World Cup", false)]
    [InlineData("/pheno-age", "Pheno Age Calculator | Longevity World Cup", true)]
    [InlineData("/bortz-age", "Bortz Age Calculator | Longevity World Cup", true)]
    [InlineData("/play", "Game Menu | Longevity World Cup", true)]
    [InlineData("/join", "Join Longevity World Cup", true)]
    [InlineData("/apply", "Athlete Application | Longevity World Cup", true)]
    [InlineData("/review", "Application Review | Longevity World Cup", true)]
    [InlineData("/proofs", "Proof Upload | Longevity World Cup", true)]
    [InlineData("/select-athlete", "Athlete Selection | Longevity World Cup", true)]
    [InlineData("/dashboard", "Athlete Dashboard | Longevity World Cup", true)]
    [InlineData("/edit-profile", "Edit Profile | Longevity World Cup", true)]
    [InlineData("/unsubscribe", "Unsubscribe | Longevity World Cup", true)]
    public async Task UtilityPages_KeepSpecificBrowserTabTitles(string path, string expectedTitle, bool expectsNoIndex)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains($"<title>{expectedTitle}</title>", html);
        if (expectsNoIndex)
        {
            Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
        }
        else
        {
            Assert.DoesNotContain("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
        }
    }
}
