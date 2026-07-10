using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using System.Reflection;
using System.Text;
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
        Assert.Equal("Date of birth cannot be in the future.", badRequest.Value);
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
    public async Task ResultSubmissionNullBiomarkerRowReturnsDateRequiredBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [null!],
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
        Assert.Equal("Biomarker date cannot be in the future.", badRequest.Value);
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
    public async Task ResultSubmissionWithBortzDifferenceMissingBortzPanelReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Klaus Townsend",
            ChronoPhenoDifference = "-1.23",
            ChronoBortzDifference = "-2.34",
            Biomarkers =
            [
                new BiomarkerData
                {
                    Date = "2026-07-01",
                    NeutrophilPc = 49,
                    MonocytePc = 8.7
                }
            ],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Submitted bortz age results require all Bortz biomarkers.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionWithPhenoDifferenceMissingPhenoPanelReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            ChronoPhenoDifference = "-1.23",
            Biomarkers =
            [
                new BiomarkerData
                {
                    Date = "2026-02-02",
                    AlbGL = 45
                }
            ],
            ProofPics = ["data:image/png;base64,AA=="]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Submitted pheno age results require all pheno biomarkers.", badRequest.Value);
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
    public async Task ResultSubmissionBlankProofReturnsProofRequiredBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }],
            ProofPics = [" "]
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Proof attachment is required.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionTooManyProofsReturnsBadRequestBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }],
            ProofPics = Enumerable.Repeat("data:image/png;base64,AA==", 36).ToList()
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("You can upload a maximum of 35 proof images.", badRequest.Value);
    }

    [Fact]
    public async Task ResultSubmissionProofValidationRecordsServerStatistics()
    {
        using var factory = new TestWebApplicationFactory();
        var statistics = factory.Services.GetRequiredService<SiteStatisticsService>();
        var controller = CreateController(factory, statistics);

        var result = await controller.Application(new ApplicantData
        {
            Name = "Applicant Ada",
            Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }],
            ProofPics = ["data:image/png;base64,not-base64"]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);

        var dashboard = await statistics.GetDashboardAsync(new SiteStatisticsDashboardQuery
        {
            Range = "30d",
            Flow = "application",
            Limit = 20
        });

        var submitFailure = Assert.Single(dashboard.Events, ev => ev.EventName == "application_submit_failed");
        Assert.Equal("failed", submitFailure.Outcome);
        Assert.Equal("proof_parse_failed", submitFailure.ErrorCode);

        var proofFailure = Assert.Single(dashboard.Events, ev => ev.EventName == "proof_processing_failed");
        Assert.Equal("failed", proofFailure.Outcome);
        Assert.Equal("proof_parse_failed", proofFailure.ErrorCode);
        Assert.Equal("1", proofFailure.Metadata["proofCount"]);
        Assert.Equal("1", proofFailure.Metadata["proofIndex"]);
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
    [InlineData("Applicant Ada <athlete@example.test>", "athlete@example.test")]
    [InlineData("Applicant Ada <mailto:athlete@example.test?subject=Longevity>", "athlete@example.test")]
    [InlineData("mailto:athlete@example.test", "athlete@example.test")]
    [InlineData("mailto:athlete@example.test?subject=Longevity", "athlete@example.test")]
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
    [InlineData("Applicant Ada <athlete@example.test>", "athlete@example.test")]
    [InlineData("Applicant Ada <mailto:athlete@example.test?subject=Longevity>", "athlete@example.test")]
    [InlineData("mailto:athlete@example.test", "athlete@example.test")]
    [InlineData("mailto:athlete@example.test?subject=Longevity", "athlete@example.test")]
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
    public void TryReadExistingAthleteFields_ChecksUnderscoreSlugForHyphenatedFolderKey()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var athleteFolder = Path.Combine(tempRoot, "athletes", "anna_maria");
            Directory.CreateDirectory(athleteFolder);
            File.WriteAllText(
                Path.Combine(athleteFolder, "athlete.json"),
                """
                {
                  "Name": "Stored Athlete",
                  "MediaContact": "athlete@example.test"
                }
                """);

            var environment = new TestWebHostEnvironment { WebRootPath = tempRoot };
            var method = typeof(ApplicationController).GetMethod("TryReadExistingAthleteFields", BindingFlags.Static | BindingFlags.NonPublic);

            var fields = Assert.IsType<Dictionary<string, string?>>(method!.Invoke(null, [environment, "anna-maria", "Submitted Athlete"]));

            Assert.Equal("athlete@example.test", fields["MediaContact"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(" athlete@example.test ", "Applicant Ada", 1, "athlete@example.test", "Applicant Ada")]
    [InlineData("Applicant Ada <athlete@example.test>", "Applicant Ada", 1, "athlete@example.test", "Applicant Ada")]
    [InlineData("athlete@example.test", " Applicant Ada ", 1, "athlete@example.test", "Applicant Ada")]
    [InlineData("https://example.test/athlete", "Applicant Ada", 0, null, null)]
    [InlineData(null, "Applicant Ada", 0, null, null)]
    public void AddReplyToIfValid_UsesOnlyValidContactEmail(
        string? accountEmail,
        string? displayName,
        int expectedCount,
        string? expectedEmail,
        string? expectedName)
    {
        var method = typeof(ApplicationController).GetMethod("AddReplyToIfValid", BindingFlags.Static | BindingFlags.NonPublic);
        var message = new MimeMessage();

        method!.Invoke(null, [message, accountEmail, displayName]);

        Assert.Equal(expectedCount, message.ReplyTo.Count);
        if (expectedCount == 0)
            return;

        var mailbox = Assert.IsType<MailboxAddress>(message.ReplyTo[0]);
        Assert.Equal(expectedEmail, mailbox.Address);
        Assert.Equal(expectedName, mailbox.Name);
    }

    [Theory]
    [InlineData("athlete@example.test", true)]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    public void ApplicationAuditIntro_IncludesReplyHintWhenRequesterEmailIsKnown(string? accountEmail, bool expectedHint)
    {
        var method = typeof(ApplicationController).GetMethod("AppendLegacyApplicationEmailIntro", BindingFlags.Static | BindingFlags.NonPublic);
        var body = new StringBuilder();

        method!.Invoke(null, [body, accountEmail, "free (USD)", null, null, false, false]);

        var intro = body.ToString();
        if (expectedHint)
        {
            Assert.Contains("Reply to this email to contact the requester.", intro);
        }
        else
        {
            Assert.DoesNotContain("Reply to this email to contact the requester.", intro);
        }
    }

    [Fact]
    public void InterviewRequestEmail_UsesRequesterAsReplyTo()
    {
        var method = typeof(ApplicationController).GetMethod("BuildInterviewRequestEmail", BindingFlags.Static | BindingFlags.NonPublic);
        var config = new Config
        {
            EmailFrom = "lwc@example.test",
            EmailTo = "admin@example.test"
        };

        var message = Assert.IsType<MimeMessage>(method!.Invoke(null, [config, " athlete@example.test "]));

        Assert.Equal("LWC Interview Request", message.Subject);
        Assert.Equal("admin@example.test", Assert.IsType<MailboxAddress>(message.To[0]).Address);
        var replyTo = Assert.IsType<MailboxAddress>(message.ReplyTo[0]);
        Assert.Equal("athlete@example.test", replyTo.Address);
        Assert.Equal("athlete@example.test", replyTo.Name);
        Assert.Contains("Reply to this email to contact the requester.", message.TextBody);
        Assert.DoesNotContain("athlete@example.test", message.TextBody);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("www.example.test/athlete", "https://www.example.test/athlete")]
    [InlineData("example.test/athlete", "https://example.test/athlete")]
    [InlineData("instagram.com/athlete", "https://instagram.com/athlete")]
    [InlineData("https://example.test/athlete", "https://example.test/athlete")]
    [InlineData("not a url", "not a url")]
    public void NormalizeSubmittedPersonalLink_AddsHttpsToDomainLinks(string? value, string? expected)
    {
        var method = typeof(ApplicationController).GetMethod("NormalizeSubmittedPersonalLink", BindingFlags.Static | BindingFlags.NonPublic);

        var normalized = (string?)method!.Invoke(null, [value]);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void InterviewRequest_DoesNotPersistOrLogRawRequesterEmail()
    {
        string? sourcePath = null;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "LongevityWorldCup.Website",
                "Controllers",
                "ApplicationController.cs");
            if (File.Exists(candidate))
            {
                sourcePath = candidate;
                break;
            }

            directory = directory.Parent;
        }

        Assert.NotNull(sourcePath);

        var source = File.ReadAllText(sourcePath);
        var actionStart = source.IndexOf("public async Task<IActionResult> InterviewRequest", StringComparison.Ordinal);
        var actionEnd = source.IndexOf("[HttpPost(\"payment-status\")]", actionStart, StringComparison.Ordinal);

        Assert.True(actionStart >= 0);
        Assert.True(actionEnd > actionStart);

        var actionBody = source[actionStart..actionEnd];

        Assert.DoesNotContain("NewsletterService.SubscribeAsync(email", actionBody);
        Assert.DoesNotContain("{Email}", actionBody);
        Assert.Contains("Failed to send interview request email.", actionBody);
    }

    [Fact]
    public void SubmissionConfirmationSubject_UsesResultUploadCopyForResultSubmissions()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationSubject", BindingFlags.Static | BindingFlags.NonPublic);

        var subject = (string?)method!.Invoke(null, [true, false]);

        Assert.Equal("Your Longevity World Cup result upload was received", subject);
    }

    [Fact]
    public void SubmissionConfirmationRecipient_DoesNotPutApplicantNameInEmailHeader()
    {
        var method = typeof(ApplicationController).GetMethod("CreateSubmissionConfirmationRecipient", BindingFlags.Static | BindingFlags.NonPublic);

        var recipient = Assert.IsType<MailboxAddress>(method!.Invoke(null, ["athlete@example.test"]));

        Assert.Equal("athlete@example.test", recipient.Address);
        Assert.Equal(string.Empty, recipient.Name);
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

        var body = (string?)method!.Invoke(null, ["Athlete Ada", true, false, "https://pay.example.test/invoice", false]);

        Assert.NotNull(body);
        Assert.Contains("Hi Athlete Ada,", body);
        Assert.Contains("result upload and proof", body);
        Assert.Contains("update your athlete profile", body);
        Assert.Contains("https://pay.example.test/invoice", body);
        Assert.DoesNotContain("could not create the payment page automatically", body);
        Assert.DoesNotContain("application, which usually takes a day or two", body);
    }

    [Fact]
    public void SubmissionConfirmationBody_ExplainsUnavailableResultUploadPaymentPage()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Athlete Ada", true, false, null, true]);

        Assert.NotNull(body);
        Assert.Contains("result upload and proof", body);
        Assert.Contains("Your upload also has a payment step, but we could not create the payment page automatically.", body);
        Assert.DoesNotContain("If you were not redirected automatically", body);
        Assert.DoesNotContain("https://pay.example.test/invoice", body);
    }

    [Fact]
    public void SubmissionConfirmationBody_UsesChangeRequestCopyForEditSubmissions()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Athlete Ada", false, true, null, true]);

        Assert.NotNull(body);
        Assert.Contains("Hi Athlete Ada,", body);
        Assert.Contains("profile change request", body);
        Assert.Contains("update your athlete profile", body);
        Assert.DoesNotContain("result upload and proof", body);
        Assert.DoesNotContain("application, which usually takes a day or two", body);
        Assert.DoesNotContain("could not create the payment page automatically", body);
    }

    [Fact]
    public void SubmissionConfirmationBody_PreservesApplicationCopyForFullApplications()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Applicant Ada", false, false, null, false]);

        Assert.NotNull(body);
        Assert.Contains("Hi Applicant Ada,", body);
        Assert.Contains("application, which usually takes a day or two", body);
        Assert.DoesNotContain("result upload and proof", body);
    }

    [Fact]
    public void SubmissionConfirmationBody_ExplainsUnavailableApplicationPaymentPage()
    {
        var method = typeof(ApplicationController).GetMethod("BuildSubmissionConfirmationBody", BindingFlags.Static | BindingFlags.NonPublic);

        var body = (string?)method!.Invoke(null, ["Applicant Ada", false, false, null, true]);

        Assert.NotNull(body);
        Assert.Contains("application, which usually takes a day or two", body);
        Assert.Contains("Your application also has a payment step, but we could not create the payment page automatically.", body);
        Assert.DoesNotContain("If you were not redirected automatically", body);
    }

    [Fact]
    public void ApplicationSubmit_TreatsPostEmailInvoiceFailureAsAcceptedSubmission()
    {
        var source = ReadApplicationControllerSource();
        var failureStart = source.IndexOf("Application submission was saved but BTCPay invoice creation failed.", StringComparison.Ordinal);
        var acceptedReturn = source.IndexOf("return Ok(paymentUnavailableResponse);", failureStart, StringComparison.Ordinal);
        var failureEnd = acceptedReturn + "return Ok(paymentUnavailableResponse);".Length;

        Assert.True(failureStart >= 0);
        Assert.True(acceptedReturn > failureStart);
        Assert.True(failureEnd > failureStart);

        var failureBody = source[failureStart..failureEnd];

        Assert.Contains("paymentRequired: true", failureBody);
        Assert.Contains("invoiceId: null", failureBody);
        Assert.Contains("checkoutLink: null", failureBody);
        Assert.Contains("await TrySendSubmissionConfirmationEmailAsync(", failureBody);
        Assert.Contains("paymentUnavailable: true", failureBody);
        Assert.Contains("PaymentUnavailable: true", failureBody);
        Assert.Contains("submissionLease.Complete(paymentUnavailableResponse);", failureBody);
        Assert.Contains("return Ok(paymentUnavailableResponse);", failureBody);
        Assert.DoesNotContain("return StatusCode(500, $\"Application sent, but failed to create BTCPay invoice:", failureBody);
    }

    [Fact]
    public void ApplicationSubmit_ArchivesAndContinuesWhenAdminEmailFails()
    {
        var source = ReadApplicationControllerSource();
        var emailStart = source.IndexOf("// Send the admin notification email.", StringComparison.Ordinal);
        var paymentStart = source.IndexOf("if (!paymentRequired)", emailStart, StringComparison.Ordinal);
        var invoiceStart = source.IndexOf("var invoiceResult = await CreateBtcpayInvoiceAsync", paymentStart, StringComparison.Ordinal);

        Assert.True(emailStart >= 0);
        Assert.True(paymentStart > emailStart);
        Assert.True(invoiceStart > paymentStart);

        var emailBody = source[emailStart..paymentStart];
        var freeSubmissionBody = source[paymentStart..invoiceStart];

        Assert.Contains("archivedSubmissionPath = await PersistApplicationSubmissionArchiveAsync(zipPath, folderKey, submissionId, ct);", emailBody);
        Assert.Contains("Application submission email failed after archive was saved.", emailBody);
        Assert.Contains("Application submission email failed and archive could not be saved.", emailBody);
        Assert.Contains("return await StatusCodeWithStatsAsync(500, \"application_archive_failed\", \"Internal server error: application could not be saved.\").ConfigureAwait(false);", emailBody);
        Assert.Contains("return Ok(freeSubmissionResponse);", freeSubmissionBody);
        Assert.Contains("PaymentRequired: false", freeSubmissionBody);
        Assert.Contains("submissionLease.Complete(freeSubmissionResponse);", freeSubmissionBody);
        Assert.Contains("AuditEmailDelivered={AuditEmailDelivered} ArchivePath={ArchivePath}", freeSubmissionBody);
    }

    [Fact]
    public void ApplicationArchive_UsesDurableDataDirectory()
    {
        var source = ReadApplicationControllerSource();
        var methodStart = source.IndexOf("private static async Task<string> PersistApplicationSubmissionArchiveAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string SanitizeArchiveFilePart", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var methodBody = source[methodStart..methodEnd];

        Assert.Contains("EnvironmentHelpers.GetDataDir()", methodBody);
        Assert.Contains("\"ApplicationSubmissions\"", methodBody);
        Assert.Contains("FileMode.CreateNew", methodBody);
    }

    [Theory]
    [InlineData(false, false, "https://submit.example.test/review")]
    [InlineData(true, false, "https://submit.example.test/review?from=proof-upload")]
    [InlineData(false, true, "https://submit.example.test/review?from=edit-profile")]
    public void ReviewRedirectUrl_PreservesSubmissionReviewSource(bool isResultSubmissionOnly, bool isEditSubmissionOnly, string expected)
    {
        using var factory = new TestWebApplicationFactory();
        var controller = CreateController(factory);
        controller.ControllerContext.HttpContext.Request.Scheme = "https";
        controller.ControllerContext.HttpContext.Request.Host = new HostString("submit.example.test");
        var method = typeof(ApplicationController).GetMethod("BuildReviewRedirectUrlForCurrentRequest", BindingFlags.Instance | BindingFlags.NonPublic);

        var redirectUrl = (string?)method!.Invoke(controller, [isResultSubmissionOnly, isEditSubmissionOnly]);

        Assert.Equal(expected, redirectUrl);
    }

    [Fact]
    public void BtcpayInvoicePayload_PreservesAccountEmailInMetadata()
    {
        var source = ReadApplicationControllerSource();
        var metadataStart = source.IndexOf("[\"metadata\"] = new Dictionary<string, object?>", StringComparison.Ordinal);
        var payloadEnd = source.IndexOf("using var client = new HttpClient();", metadataStart, StringComparison.Ordinal);

        Assert.True(metadataStart >= 0);
        Assert.True(payloadEnd > metadataStart);

        var metadataBody = source[metadataStart..payloadEnd];

        Assert.Contains("[\"buyerEmail\"] = accountEmail", metadataBody);
    }

    [Theory]
    [InlineData("result", "Payment detected for result upload.")]
    [InlineData(" RESULT ", "Payment detected for result upload.")]
    [InlineData("edit", "Payment detected for profile change request.")]
    [InlineData("application", "Payment detected for submitted application.")]
    [InlineData("unknown", "Payment detected for submitted application.")]
    [InlineData(null, "Payment detected for submitted application.")]
    public void PaymentFollowupIntro_UsesSubmissionSpecificCopy(string? submissionType, string expected)
    {
        var method = typeof(ApplicationController).GetMethod("BuildPaymentFollowupIntro", BindingFlags.Static | BindingFlags.NonPublic);

        var intro = (string?)method!.Invoke(null, [submissionType]);

        Assert.Equal(expected, intro);
    }

    [Theory]
    [InlineData("result", "Athlete email")]
    [InlineData("edit", "Athlete email")]
    [InlineData("application", "Applicant email")]
    [InlineData("unknown", "Applicant email")]
    [InlineData(null, "Applicant email")]
    public void PaymentFollowupContactLabel_UsesSubmissionSpecificLabel(string? submissionType, string expected)
    {
        var method = typeof(ApplicationController).GetMethod("BuildPaymentFollowupContactLabel", BindingFlags.Static | BindingFlags.NonPublic);

        var label = (string?)method!.Invoke(null, [submissionType]);

        Assert.Equal(expected, label);
    }

    private static string ReadApplicationControllerSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "LongevityWorldCup.Website",
                "Controllers",
                "ApplicationController.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("ApplicationController.cs was not found.");
    }

    private static ApplicationController CreateController(TestWebApplicationFactory factory, SiteStatisticsService? statistics = null)
    {
        return new ApplicationController(
            factory.Services.GetRequiredService<IWebHostEnvironment>(),
            NullLogger<ApplicationController>.Instance,
            factory.Services.GetRequiredService<ApplicationSubmissionRetryStore>(),
            statistics: statistics)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = factory.Services,
                    Request =
                    {
                        Path = "/api/application/application"
                    }
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

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
