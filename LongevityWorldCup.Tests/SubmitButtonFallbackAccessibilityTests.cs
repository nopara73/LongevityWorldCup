using Xunit;

namespace LongevityWorldCup.Tests;


public sealed class SubmitButtonFallbackAccessibilityTests(TestWebApplicationFactory sharedFactory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("/play/edit-profile.html", "Submit change request")]
    [InlineData("/play/proof-upload.html", "Submit new results")]
    public async Task SubmitButtons_HaveNamesBeforeScriptsRun(string path, string label)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains($"id=\"submitButton\" type=\"submit\"", html);
        Assert.Contains($"<span class=\"flow-action__label\">{label}</span><i class=\"fa fa-rocket\" aria-hidden=\"true\"></i></button>", html);
    }
}
