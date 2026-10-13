using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

// These tests temporarily change the process-wide SMTP password override.
[CollectionDefinition("SMTP environment", DisableParallelization = true)]
public sealed class SmtpEnvironmentCollection;

[Collection("SMTP environment")]
public sealed class SmtpConfigurationTests : IDisposable
{
    private readonly string? _originalPassword = Environment.GetEnvironmentVariable("LWC_SMTP_PASSWORD");

    public SmtpConfigurationTests() => Environment.SetEnvironmentVariable("LWC_SMTP_PASSWORD", null);

    public void Dispose() => Environment.SetEnvironmentVariable("LWC_SMTP_PASSWORD", _originalPassword);

    [Theory]
    [InlineData(null, " configured-password ", "configured-password")]
    [InlineData(" ", " configured-password ", "configured-password")]
    [InlineData(" environment-password ", "configured-password", "environment-password")]
    [InlineData(" environment-password ", null, "environment-password")]
    public void Password_UsesEnvironmentOverrideOrConfig(string? environment, string? configured, string expected)
    {
        Environment.SetEnvironmentVariable("LWC_SMTP_PASSWORD", environment);
        Assert.Equal(expected, new Config { SmtpPassword = configured }.GetSmtpPassword());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void MissingPassword_ExplainsBothConfigurationOptions(string? environment, string? configured)
    {
        Environment.SetEnvironmentVariable("LWC_SMTP_PASSWORD", environment);
        var error = Assert.Throws<InvalidOperationException>(() => new Config { SmtpPassword = configured }.GetSmtpPassword());
        Assert.Equal("SmtpPassword or LWC_SMTP_PASSWORD must be configured.", error.Message);
    }

    [Fact]
    public async Task DiscountReport_MissingPasswordFailsBeforeConnecting()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SmtpDiscountSignupReportEmailSender().SendAsync(ConfigWithoutPassword(), ["recipient@example.test"], "Report", "Report body"));
        Assert.Equal("SmtpPassword or LWC_SMTP_PASSWORD must be configured.", error.Message);
    }

    [Fact]
    public async Task ChallengeConfirmation_MissingPasswordFailsBeforeConnecting()
    {
        var sender = new SmtpLongevitymaxxingEmailSender(ConfigWithoutPassword(), NullLogger<SmtpLongevitymaxxingEmailSender>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendConfirmationAsync("recipient@example.test", "Athlete", "https://example.test/confirm"));
        Assert.Equal("SmtpPassword or LWC_SMTP_PASSWORD must be configured.", error.Message);
    }

    private static Config ConfigWithoutPassword() => new()
    {
        SmtpServer = "smtp.example.test", SmtpPort = 587, SmtpUser = "sender@example.test",
        EmailFrom = "sender@example.test"
    };
}
