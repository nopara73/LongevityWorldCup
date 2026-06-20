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
