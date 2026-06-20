using LongevityWorldCup.Website.Tools;
using System.Text.RegularExpressions;
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

    [Fact]
    public async Task ApplicationSubmissionTimeout_WaitsForServerPublicWorkTimeout()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var match = Regex.Match(javascript, @"APPLICATION_SUBMISSION_TIMEOUT_MS\s*=\s*(\d+)");

        Assert.True(match.Success);
        var timeoutMs = int.Parse(match.Groups[1].Value);
        Assert.True(timeoutMs > PublicRequestTimeoutPolicies.PublicWorkTimeout.TotalMilliseconds);
    }

    [Fact]
    public async Task ApplicationSubmissionReport_IsTimeBoundedBecauseItIsBestEffort()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var submissionTimeoutMatch = Regex.Match(javascript, @"APPLICATION_SUBMISSION_TIMEOUT_MS\s*=\s*(\d+)");
        var reportTimeoutMatch = Regex.Match(javascript, @"APPLICATION_SUBMISSION_REPORT_TIMEOUT_MS\s*=\s*(\d+)");

        Assert.True(submissionTimeoutMatch.Success);
        Assert.True(reportTimeoutMatch.Success);
        Assert.True(int.Parse(reportTimeoutMatch.Groups[1].Value) < int.Parse(submissionTimeoutMatch.Groups[1].Value));
        Assert.Contains("const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;", javascript);
        Assert.Contains("window.setTimeout(() => controller.abort(), timeoutMs)", javascript);
        Assert.Contains("...(controller ? { signal: controller.signal } : {})", javascript);
    }

    [Fact]
    public async Task StartedSubmissionReports_DoNotDelayPrimarySubmission()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/onboarding/convergence.html",
            "/play/proof-upload.html",
            "/play/edit-profile.html"
        })
        {
            var html = await client.GetStringAsync(path);

            Assert.DoesNotContain("await window.sendApplicationSubmissionReport(", html);
            Assert.Contains("void window.sendApplicationSubmissionReport(", html);
            Assert.Contains("window.buildApplicationSubmissionReport(applicantData, submissionId, 'started', submissionKind)", html);
        }
    }

    [Fact]
    public async Task ApplicationFailures_UseReadableErrorExtractor()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("window.readApplicationErrorMessage = async function (response)", javascript);
        Assert.Contains("window.extractApplicationErrorMessage = function (text, fallback)", javascript);
        Assert.Contains("if (data && typeof data.message === 'string' && data.message.trim())", javascript);
        Assert.Contains("if (data && data.errors && typeof data.errors === 'object')", javascript);
        Assert.Contains("return messages.join('\\n');", javascript);
        Assert.Contains("window.readApplicationErrorMessage(response).then(badResponse =>", html);
        Assert.DoesNotContain("response.text().then(badResponse =>", html);
    }

    [Fact]
    public async Task ProfilePhotoSelection_ClearsInputAfterCapturingFile()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var selectionStart = html.IndexOf("function handleProfileUploadChange(event)", StringComparison.Ordinal);
        var selectionEnd = html.IndexOf("if (profilePicInput && !profilePicInput.hasAttribute('data-listener'))", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0);
        Assert.True(selectionEnd > selectionStart);

        var selectionBody = html[selectionStart..selectionEnd];
        Assert.Contains("const input = event.target;", selectionBody);
        Assert.Contains("const file = input.files[0];", selectionBody);
        Assert.Contains("input.value = '';", selectionBody);
        Assert.Contains("reader.onerror = function ()", selectionBody);
        Assert.Contains("customAlert('Profile picture upload failed.');", selectionBody);
    }

    [Fact]
    public async Task ProfilePhotoCrop_CanBeCanceledBackToUploadMode()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var cancelStart = html.IndexOf("document.getElementById('cancelProfileCropButton').addEventListener('click'", StringComparison.Ordinal);
        var cancelEnd = html.IndexOf("profilePicInput.setAttribute('data-listener', 'true');", cancelStart, StringComparison.Ordinal);

        Assert.Contains("id=\"cancelProfileCropButton\"", html);
        Assert.True(cancelStart >= 0);
        Assert.True(cancelEnd > cancelStart);

        var cancelBody = html[cancelStart..cancelEnd];
        Assert.Contains("cropper.destroy();", cancelBody);
        Assert.Contains("cropper = null;", cancelBody);
        Assert.Contains("document.getElementById('uploadPart').style.display = '';", cancelBody);
        Assert.Contains("document.getElementById('croppingPart').style.display = 'none';", cancelBody);
        Assert.Contains("nextButton.disabled = !profilePic;", cancelBody);
    }

    [Fact]
    public async Task ProfilePhotoCrop_FallsBackToRawCropWhenOptimizationFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var cropStart = html.IndexOf("document.getElementById('cropButton').addEventListener('click'", StringComparison.Ordinal);
        var cropEnd = html.IndexOf("document.getElementById('cancelProfileCropButton').addEventListener('click'", cropStart, StringComparison.Ordinal);

        Assert.True(cropStart >= 0);
        Assert.True(cropEnd > cropStart);

        var cropBody = html[cropStart..cropEnd];
        Assert.Contains("let croppedImageDataURL = raw;", cropBody);
        Assert.Contains("try {", cropBody);
        Assert.Contains("await window.optimizeImageClient(raw, window.PROFILE_IMAGE_OPTIMIZATION_OPTIONS);", cropBody);
        Assert.Contains("} catch {", cropBody);
        Assert.Contains("croppedImageDataURL = raw;", cropBody);
        Assert.Contains("profilePic = croppedImageDataURL;", cropBody);
    }
}
