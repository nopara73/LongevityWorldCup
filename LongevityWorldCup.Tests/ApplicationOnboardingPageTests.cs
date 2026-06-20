using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationOnboardingPageTests
{
    [Fact]
    public async Task ApplicationRetry_ReenablesEmailFieldAfterSubmissionFailure()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("accountEmailInput.setAttribute(\"disabled\", \"true\");", html);
        Assert.Contains("accountEmailInput.disabled = false;", html);
        Assert.DoesNotContain("accountEmailInput.setAttribute(\"disabled\", \"false\");", html);
    }
}
