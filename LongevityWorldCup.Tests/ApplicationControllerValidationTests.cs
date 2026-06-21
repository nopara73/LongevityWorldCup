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
}
