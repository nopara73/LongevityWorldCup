using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
            Biomarkers = [new BiomarkerData { Date = "2026-02-02" }],
            ProfilePic = "data:image/png;base64,AA==",
            ProofPics = ["data:image/png;base64,AA=="],
            Division = "Open",
            Flag = "Cyberspace",
            Why = "I want to compete, learn, and improve my healthspan.",
            MediaContact = "media@example.test"
        };
    }
}
