using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationControllerValidationTests
{
    [Fact]
    public async Task ApplicationInvalidModelStateReturnsValidationProblemDetails()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);
        controller.ModelState.AddModelError(nameof(ApplicantData.AccountEmail), "Email is invalid.");

        var result = await controller.Application(new ApplicantData(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var details = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.True(details.Errors.ContainsKey(nameof(ApplicantData.AccountEmail)));
    }

    [Fact]
    public async Task ApplicationNullBodyReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(null!, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Applicant data is null.", badRequest.Value);
    }

    [Fact]
    public async Task ApplicationMissingNameReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData { AccountEmail = "athlete@example.test" }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Applicant name is required.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationMissingAccountEmailReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 }
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Account email is required.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationInvalidAccountEmailReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "not-an-email",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 }
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Account email is invalid.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationMissingDateOfBirthReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            Biomarkers = [new BiomarkerData()],
            ProfilePic = "data:image/png;base64,AA==",
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Date of birth is required.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationInvalidDateOfBirthReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 2, Day = 31 }
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Date of birth is invalid.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationFutureDateOfBirthReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            DateOfBirth = new DateOfBirthData { Year = tomorrow.Year, Month = tomorrow.Month, Day = tomorrow.Day }
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Date of birth is invalid.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationMissingBiomarkersReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 },
            ProfilePic = "data:image/png;base64,AA==",
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Biomarker data is required.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationMissingProofReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 },
            Biomarkers = [new BiomarkerData()],
            ProfilePic = "data:image/png;base64,AA=="
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Proof attachment is required.", badRequest.Value);
    }

    [Fact]
    public async Task FullApplicationMissingProfilePictureReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 },
            Biomarkers = [new BiomarkerData()],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Profile picture is required.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionMissingBiomarkerDateReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData()],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Biomarker date is required.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionInvalidAccountEmailReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "not-an-email",
            Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Account email is invalid.", badRequest.Value);
    }

    [Theory]
    [InlineData("02/02/2026")]
    [InlineData("2026-02-31")]
    public async Task ResultSubmissionInvalidBiomarkerDateReturnsBadRequestBeforeProcessing(string biomarkerDate)
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = biomarkerDate }],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Biomarker date is invalid.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionFutureBiomarkerDateReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = tomorrow.ToString("yyyy-MM-dd") }],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Biomarker date is invalid.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionMissingBiomarkerValuesReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = "2026-02-02" }],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Biomarker result value is required.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionMissingProofReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Proof attachment is required.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionMissingBiomarkersReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Biomarker data is required.", badRequest.Value);
    }

    [Theory]
    [InlineData("division", "Division is required.")]
    [InlineData("flag", "Flag is required.")]
    [InlineData("why", "Why is required.")]
    [InlineData("mediaContact", "Media contact is required.")]
    public async Task FullApplicationMissingRequiredProfileFieldReturnsBadRequestBeforeProcessing(string missingField, string expectedError)
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);
        var applicantData = CreateValidFullApplication();

        switch (missingField)
        {
            case "division":
                applicantData.Division = null;
                break;
            case "flag":
                applicantData.Flag = null;
                break;
            case "why":
                applicantData.Why = null;
                break;
            case "mediaContact":
                applicantData.MediaContact = null;
                break;
        }

        var result = await controller.Application(applicantData, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(expectedError, badRequest.Value);
    }

    [Fact]
    public async Task InterviewRequestInvalidModelStateReturnsValidationProblemDetails()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);
        controller.ModelState.AddModelError(nameof(InterviewRequestData.Email), "Email is invalid.");

        var result = await controller.InterviewRequest(new InterviewRequestData(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var details = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.True(details.Errors.ContainsKey(nameof(InterviewRequestData.Email)));
    }

    [Theory]
    [InlineData(" athlete@example.test ", "athlete@example.test")]
    [InlineData("not-an-email", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeOptionalAccountEmail_TrimsValidEmailAndDropsInvalidEmail(string? accountEmail, string? expected)
    {
        var method = typeof(ApplicationController).GetMethod("NormalizeOptionalAccountEmail", BindingFlags.Static | BindingFlags.NonPublic);

        var normalized = (string?)method!.Invoke(null, [accountEmail]);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(" athlete@example.test ", "athlete@example.test")]
    [InlineData("https://example.test/athlete", null)]
    [InlineData("not-an-email", null)]
    [InlineData(null, null)]
    public void ResolveExistingAthleteContactEmail_UsesOnlyValidPublicMediaContact(string? mediaContact, string? expected)
    {
        var method = typeof(ApplicationController).GetMethod("ResolveExistingAthleteContactEmail", BindingFlags.Static | BindingFlags.NonPublic);
        var existingFields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MediaContact"] = mediaContact
        };

        var resolved = (string?)method!.Invoke(null, [existingFields]);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void SubmissionConfirmationSubject_UsesResultUploadCopyForResultSubmissions()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationSubject", BindingFlags.Static | BindingFlags.NonPublic);

        var subject = (string?)method!.Invoke(null, [true, false]);

        Assert.Equal("Your Longevity World Cup result upload was received", subject);
    }

    [Fact]
    public void SubmissionConfirmationSubject_UsesChangeRequestCopyForEditSubmissions()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationSubject", BindingFlags.Static | BindingFlags.NonPublic);

        var subject = (string?)method!.Invoke(null, [false, true]);

        Assert.Equal("Your Longevity World Cup change request was received", subject);
    }

    [Fact]
    public void SubmissionConfirmationBody_UsesResultUploadCopyForResultSubmissions()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Athlete Ada", true, false, "https://pay.example.test/invoice"]);

        Assert.NotNull(body);
        Assert.Contains("Hi Athlete Ada,", body);
        Assert.Contains("result upload and proof", body);
        Assert.Contains("update your athlete profile", body);
        Assert.Contains("https://pay.example.test/invoice", body);
        Assert.DoesNotContain("application, which usually takes a day or two", body);
    }

    [Fact]
    public void SubmissionConfirmationBody_UsesChangeRequestCopyForEditSubmissions()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Athlete Ada", false, true, null]);

        Assert.NotNull(body);
        Assert.Contains("Hi Athlete Ada,", body);
        Assert.Contains("profile change request", body);
        Assert.Contains("update your athlete profile", body);
        Assert.DoesNotContain("result upload and proof", body);
        Assert.DoesNotContain("application, which usually takes a day or two", body);
    }

    [Fact]
    public void SubmissionConfirmationBody_PreservesApplicationCopyForFullApplications()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Applicant Ada", false, false, null]);

        Assert.NotNull(body);
        Assert.Contains("Hi Applicant Ada,", body);
        Assert.Contains("application, which usually takes a day or two", body);
        Assert.DoesNotContain("result upload and proof", body);
    }

    private static ApplicationController CreateController(TestWebApplicationFactory factory)
    {
        return new ApplicationController(
            factory.Services.GetRequiredService<IWebHostEnvironment>(),
            NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = factory.Services
                }
            }
        };
    }

    private static ApplicantData CreateValidFullApplication()
    {
        return new ApplicantData
        {
            Name = "Applicant Ada",
            AccountEmail = "athlete@example.test",
            DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 },
            Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }],
            ProfilePic = "data:image/png;base64,AA==",
            ProofPics = ["data:image/png;base64,AA=="],
            Division = "Open",
            Flag = "Cyberspace",
            Why = "I want to compete, learn, and improve my healthspan.",
            MediaContact = "media@example.test"
        };
    }
}
