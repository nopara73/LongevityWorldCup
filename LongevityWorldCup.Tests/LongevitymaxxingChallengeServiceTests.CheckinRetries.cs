using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task CheckInReplay_SurvivesRestartAndDoesNotDuplicatePhotosOrOverwriteALaterEdit()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("retry@example.com", "Retry Riley");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var request = new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "Original note.", Guid.NewGuid().ToString());
        using var first = CreatePngStream();
        using var second = CreatePngStream(8, 8);
        var initial = await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(first), CreatePngFormFile(second)], now);
        var initialUrls = Assert.Single(initial.Notes).Images.Select(image => image.Url).ToArray();
        fixture.Service.SubmitCheckIn(request with { Note = "A later edit.", SubmissionId = Guid.NewGuid().ToString() }, now.AddMinutes(1));
        var restarted = new LongevitymaxxingChallengeService(fixture.Db, fixture.Config, fixture.Http, fixture.Environment, fixture.Email, NullLogger<LongevitymaxxingChallengeService>.Instance, fixture.Athletes, fixture.Statistics);
        using var retryFirst = CreatePngStream();
        using var retrySecond = CreatePngStream(8, 8);
        var replay = await restarted.SubmitCheckInAsync(request, [CreatePngFormFile(retryFirst), CreatePngFormFile(retrySecond)], now.AddMinutes(2));
        var note = Assert.Single(replay.Notes);
        Assert.Equal("A later edit.", note.Note);
        Assert.Equal(initialUrls, note.Images.Select(image => image.Url));
        Assert.Equal(2, Directory.GetFiles(PhotoDirectory(fixture)).Length);
    }

    [Fact]
    public async Task AcceptedCatchUpSubmission_CanBeReplayedAfterTheDayCloses()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("catchup-retry@example.com", "Catchup Riley");
        var now = DateTimeOffset.Parse("2026-06-29T08:00:00Z");
        var day = fixture.Service.GetParticipantState(access, now).EligibleDays.MinBy(day => day.ChallengeDay)!;
        var request = new LongevitymaxxingCheckInRequest(access, day.ChallengeDay, 2, 2, 2, 2, "Catch-up note.", Guid.NewGuid().ToString());
        using var image = CreatePngStream();
        var saved = await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(image)], now);
        Assert.DoesNotContain(saved.EligibleDays, candidate => candidate.ChallengeDay == day.ChallengeDay);
        using var retry = CreatePngStream();
        var replay = await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(retry)], now.AddMinutes(1));
        Assert.Single(Assert.Single(replay.Notes).Images);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitCheckInAsync(request with { SubmissionId = Guid.NewGuid().ToString() }, [], now.AddMinutes(1)));
    }

    [Fact]
    public async Task CheckInSubmissionId_IsBoundToItsContentAndAuthenticatedParticipant()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("content-retry@example.com", "Content Riley");
        var otherAccess = await fixture.ConfirmParticipantAsync("other-retry@example.com", "Other Riley");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var request = new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "Original.", Guid.NewGuid().ToString());
        using var image = CreatePngStream();
        await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(image)], now);
        using var changed = CreatePngStream(8, 8);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(changed)], now));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(request with { Note = "Changed." }, now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SubmitCheckInAsync(request with { AccessToken = "invalid-token" }, [], now));
        var other = fixture.Service.SubmitCheckIn(request with { AccessToken = otherAccess }, now);
        Assert.NotEqual(other.Participant.Id, fixture.Service.GetParticipantState(access, now).Participant.Id);
        Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
    }

    [Fact]
    public async Task ConcurrentCheckInReplays_PublishOnePhotoAndCleanUpUnusedFiles()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("concurrent-retry@example.com", "Concurrent Riley");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var request = new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "One upload.", Guid.NewGuid().ToString());
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () => {
            using var image = CreatePngStream(1000, 1000);
            return await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(image)], now);
        })));
        Assert.All(results, result => Assert.Single(Assert.Single(result.Notes).Images));
        Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
    }

    [Fact]
    public async Task ConcurrentDistinctCheckIns_EnforceThePhotoLimitInsideTheTransaction()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("capacity-retry@example.com", "Capacity Riley");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () => {
            using var first = CreatePngStream(800, 800);
            using var second = CreatePngStream(600, 600);
            using var third = CreatePngStream(400, 400);
            try {
                await fixture.Service.SubmitCheckInAsync(new(access, 12, 2, 2, 2, 2, $"Batch {index}.", Guid.NewGuid().ToString()), [CreatePngFormFile(first), CreatePngFormFile(second), CreatePngFormFile(third)], now);
                return true;
            } catch (InvalidOperationException ex) {
                Assert.Contains("up to 4 photos", ex.Message);
                return false;
            }
        })));
        Assert.Single(outcomes, success => success);
        Assert.Equal(3, Assert.Single(fixture.Service.GetParticipantState(access, now).Notes).Images.Count);
        Assert.Equal(3, Directory.GetFiles(PhotoDirectory(fixture)).Length);
    }

    [Fact]
    public async Task FailedPhotoBatch_PreservesPublishedFilesAndCanRetryTheSameSubmission()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("failed-batch@example.com", "Batch Riley");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var request = new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "Original.", Guid.NewGuid().ToString());
        using var original = CreatePngStream();
        await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(original)], now);
        var originalPath = Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
        var originalBytes = await File.ReadAllBytesAsync(originalPath);
        var nextRequest = request with { SubmissionId = Guid.NewGuid().ToString(), Note = "Next batch." };
        using var next = CreatePngStream(8, 8);
        using var invalid = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitCheckInAsync(nextRequest, [CreatePngFormFile(next), new FormFile(invalid, 0, invalid.Length, "notePhotos", "invalid.png")], now));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
        using var retry = CreatePngStream(8, 8);
        var saved = await fixture.Service.SubmitCheckInAsync(nextRequest, [CreatePngFormFile(retry)], now);
        Assert.Equal(2, Assert.Single(saved.Notes).Images.Count);
    }

    [Fact]
    public async Task MultipartController_PreservesTheSubmissionIdentityOnRetry()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("controller-retry@example.com", "Controller Riley");
        var day = fixture.Service.GetParticipantState(access).EligibleDays.First().ChallengeDay;
        var controller = new LongevitymaxxingController(fixture.Service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var form = new LongevitymaxxingCheckInFormRequest { AccessToken = access, ChallengeDay = day, Sleep = 2, Exercise = 2, Nutrition = 2, Vices = 2, Note = "Controller upload.", SubmissionId = Guid.NewGuid().ToString() };
        using var first = CreatePngStream();
        using var retry = CreatePngStream();
        Assert.IsType<OkObjectResult>(await controller.CheckInWithPhotos(form, [CreatePngFormFile(first)], CancellationToken.None));
        var result = Assert.IsType<OkObjectResult>(await controller.CheckInWithPhotos(form, [CreatePngFormFile(retry)], CancellationToken.None));
        var state = Assert.IsType<LongevitymaxxingParticipantState>(result.Value);
        Assert.Single(Assert.Single(state.Notes).Images);
    }

    private static string PhotoDirectory(TestChallengeFixture fixture) => Path.Combine(fixture.ContentRoot, "generated", "longevitymaxxing", "check-in-photos");
}
