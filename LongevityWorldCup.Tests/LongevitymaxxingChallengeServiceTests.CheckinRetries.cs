using System.Net.Http.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.False(fixture.Service.HasSubmittedCheckIn(access, request.SubmissionId!));
        using var first = CreatePngStream();
        using var second = CreatePngStream(8, 8);
        var initial = await fixture.Service.SubmitCheckInAsync(request, [CreatePngFormFile(first), CreatePngFormFile(second)], now);
        var initialUrls = Assert.Single(initial.Notes).Images.Select(image => image.Url).ToArray();
        fixture.Service.SubmitCheckIn(request with { Note = "A later edit.", SubmissionId = Guid.NewGuid().ToString() }, now.AddMinutes(1));
        var restarted = new LongevitymaxxingChallengeService(fixture.Db, fixture.Config, fixture.Http, fixture.Environment, fixture.Email, NullLogger<LongevitymaxxingChallengeService>.Instance, fixture.Athletes, fixture.Statistics);
        Assert.True(restarted.HasSubmittedCheckIn(access, request.SubmissionId!));
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
        Assert.False(fixture.Service.HasSubmittedCheckIn(otherAccess, request.SubmissionId!));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.HasSubmittedCheckIn("invalid-token", request.SubmissionId!));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.HasSubmittedCheckIn(access, "invalid-id"));
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
        using var processingGate = new Barrier(2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () => {
            using var image = CreatePngStream(1000, 1000);
            return await fixture.Service.SubmitCheckInAsync(request, [new PairedUpload(CreatePngFormFile(image), processingGate)], now);
        })));
        Assert.All(results, result => Assert.Single(Assert.Single(result.Notes).Images));
        Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
        var statistics = await fixture.Statistics.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d", Flow = "challenge", Limit = 100 });
        Assert.Single(statistics.Events, entry => entry.EventName == "challenge_scored_checkin_submitted");
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
    public async Task MultipartCheckInApi_ConfirmsBothPhotosByReceiptAndAcceptsReplay()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("controller-retry@example.com", "Controller Riley");
        var day = fixture.Service.GetParticipantState(access).EligibleDays.Max(eligible => eligible.ChallengeDay);
        var submissionId = Guid.NewGuid().ToString();
        await using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services => services.AddSingleton(fixture.Service)));
        using var client = factory.CreateClient();

        async Task<bool> ReceivedAsync()
        {
            using var response = await client.PostAsJsonAsync("/api/longevitymaxxing/check-in/receipt", new { accessToken = access, submissionId });
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            return await response.Content.ReadFromJsonAsync<bool>();
        }

        Assert.False(await ReceivedAsync());
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var form = new MultipartFormDataContent {
                { new StringContent(access), "accessToken" }, { new StringContent(submissionId), "submissionId" },
                { new StringContent(day.ToString()), "challengeDay" }, { new StringContent("Controller upload."), "note" },
                { new StreamContent(CreatePngStream()), "notePhotos", "walk.png" },
                { new StreamContent(CreatePngStream(8, 8)), "notePhotos", "lunch.png" }
            };
            foreach (var habit in new[] { "sleep", "exercise", "nutrition", "vices" }) form.Add(new StringContent("2"), habit);
            using var response = await client.PostAsync("/api/longevitymaxxing/check-in", form);
            response.EnsureSuccessStatusCode();
            Assert.True(await ReceivedAsync());
        }
        var images = Assert.Single(fixture.Service.GetParticipantState(access).Notes).Images;
        Assert.Equal(2, images.Count);
        var removed = Path.GetFileName(new Uri($"https://example.test{images[0].Url}").AbsolutePath);
        using var replacement = new MultipartFormDataContent {
            { new StringContent(access), "accessToken" }, { new StringContent(day.ToString()), "challengeDay" },
            { new StringContent("Controller upload."), "note" }, { new StringContent(removed), "removedPhotoNames" },
            { new StreamContent(CreatePngStream()), "notePhotos", "replacement.png" }
        };
        foreach (var habit in new[] { "sleep", "exercise", "nutrition", "vices" }) replacement.Add(new StringContent("2"), habit);
        using var replaced = await client.PostAsync("/api/longevitymaxxing/check-in", replacement);
        Assert.True(replaced.IsSuccessStatusCode, await replaced.Content.ReadAsStringAsync());
        var current = Assert.Single(fixture.Service.GetParticipantState(access).Notes).Images;
        Assert.Equal(2, current.Count);
        Assert.Equal(images[1], current[0]);
        Assert.False(File.Exists(Path.Combine(PhotoDirectory(fixture), removed)));
        Assert.Equal(2, Directory.GetFiles(PhotoDirectory(fixture)).Length);
    }

    private static string PhotoDirectory(TestChallengeFixture fixture) => Path.Combine(fixture.ContentRoot, "generated", "longevitymaxxing", "check-in-photos");

    private sealed class PairedUpload(IFormFile inner, Barrier gate) : IFormFile
    {
        private int opens;
        public string ContentType => inner.ContentType;
        public string ContentDisposition => inner.ContentDisposition;
        public IHeaderDictionary Headers => inner.Headers;
        public long Length => inner.Length;
        public string Name => inner.Name;
        public string FileName => inner.FileName;
        public Stream OpenReadStream()
        {
            // Both uploads must finish their receipt lookup before either can save.
            if (Interlocked.Increment(ref opens) == 2) Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(20)));
            return inner.OpenReadStream();
        }
        public void CopyTo(Stream target) => inner.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => inner.CopyToAsync(target, cancellationToken);
    }
}
