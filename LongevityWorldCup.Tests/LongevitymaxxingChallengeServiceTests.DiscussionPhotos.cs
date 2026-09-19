using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionPhotos_AppearAcrossViewsAndSurviveRetriesAndTextEdits(bool welcome)
    {
        using var fixture = TestChallengeFixture.Create();
        var author = await fixture.ConfirmParticipantAsync("photo-author@example.com", "Photo Author");
        var replier = await fixture.ConfirmParticipantAsync("photo-replier@example.com", "Photo Replier");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(author, 12, 2, 2, 2, 2, "Opening post."), now);
        var participant = state.Participant.Id;
        var post = welcome ? state.Public.SystemDiscussionPosts.Single(item => item.ParticipantId == participant).Id : null;
        var request = new LongevitymaxxingDiscussionReplyRequest(replier, participant, 12, "", Guid.NewGuid().ToString("N"), post);
        using var first = CreatePngStream(1800, 900);
        using var second = CreatePngStream(8, 8);
        await fixture.Service.SubmitDiscussionReplyWithPhotosAsync(request, [CreatePngFormFile(first), CreatePngFormFile(second)], now);
        var thread = fixture.Service.GetDiscussionThread(participant, 12, post);
        var reply = Assert.Single(thread.Note?.Replies ?? thread.SystemPost!.Replies);
        Assert.Empty(reply.Body);
        Assert.Equal(2, reply.Images.Count);
        Assert.Equal(1600, reply.Images[0].Width);
        Assert.Equal(800, reply.Images[0].Height);
        Assert.All(reply.Images, image => Assert.Contains(".webp?v=", image.Url));
        var urls = reply.Images.Select(image => image.Url).ToArray();

        var restarted = new LongevitymaxxingChallengeService(fixture.Db, fixture.Config, fixture.Http, fixture.Environment, fixture.Email,
            NullLogger<LongevitymaxxingChallengeService>.Instance, fixture.Athletes, fixture.Statistics);
        using var retryFirst = CreatePngStream(1800, 900);
        using var retrySecond = CreatePngStream(8, 8);
        await restarted.SubmitDiscussionReplyWithPhotosAsync(request, [CreatePngFormFile(retryFirst), CreatePngFormFile(retrySecond)], now.AddMinutes(1));
        Assert.Equal(2, Directory.GetFiles(PhotoDirectory(fixture)).Length);
        Assert.Equal(1, CountContextNotifications(fixture, request.ReplyId));
        var paged = Assert.Single(restarted.GetDiscussionReplyPage(new(null, participant, 12, null, null, post)).Replies);
        Assert.Equal(urls, paged.Images.Select(image => image.Url));
        var edited = restarted.EditDiscussionReply(new(replier, request.ReplyId, "Added a caption."), now.AddMinutes(2));
        Assert.Equal(urls, edited.Images.Select(image => image.Url));
        Assert.Empty(restarted.EditDiscussionReply(new(replier, request.ReplyId, ""), now.AddMinutes(3)).Body);
        Assert.Equal("Opening post.", Assert.Single(restarted.GetParticipantState(author, now).Notes).Note);
        Assert.Throws<UnauthorizedAccessException>(() => restarted.DeleteDiscussionReply(new(author, request.ReplyId), now));
        Assert.Equal(2, Directory.GetFiles(PhotoDirectory(fixture)).Length);
        restarted.DeleteDiscussionReply(new(replier, request.ReplyId), now);
        Assert.Empty(Directory.GetFiles(PhotoDirectory(fixture)));
    }

    [Fact]
    public async Task DiscussionPhotos_ChangedAndInvalidUploadsCannotAlterAnAcceptedReply()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("photo-validation@example.com", "Photo Validation");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(access, 12, 2, 2, 2, 2, "Opening post."), now);
        var request = new LongevitymaxxingDiscussionReplyRequest(access, state.Participant.Id, 12, "Photo reply.", Guid.NewGuid().ToString("N"));
        using var original = CreatePngStream();
        await fixture.Service.SubmitDiscussionReplyWithPhotosAsync(request, [CreatePngFormFile(original)], now);
        using var different = CreatePngStream(8, 8);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReplyWithPhotosAsync(request, [CreatePngFormFile(different)], now));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(request, now));
        using var valid = CreatePngStream(8, 8);
        using var invalid = new MemoryStream([1, 2, 3]);
        var failedRequest = request with { ReplyId = Guid.NewGuid().ToString("N") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReplyWithPhotosAsync(failedRequest,
            [CreatePngFormFile(valid), new FormFile(invalid, 0, invalid.Length, "photos", "invalid.png")], now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SubmitDiscussionReplyWithPhotosAsync(
            request with { AccessToken = "invalid-token" }, [CreatePngFormFile(valid)], now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReplyWithPhotosAsync(failedRequest,
            Enumerable.Range(0, 5).Select(_ => CreatePngFormFile(valid)).ToArray(), now));
        Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
        Assert.Single(Assert.Single(fixture.Service.GetParticipantState(access, now).Notes).Replies);
    }

    [Fact]
    public async Task DiscussionPhotos_ConcurrentRetriesPublishOnceAndCleanUnusedFiles()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("photo-concurrent@example.com", "Concurrent Photos");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(access, 12, 2, 2, 2, 2, "Opening post."), now);
        var request = new LongevitymaxxingDiscussionReplyRequest(access, state.Participant.Id, 12, "", Guid.NewGuid().ToString("N"));
        using var gate = new Barrier(2);
        var states = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var image = CreatePngStream(100, 100);
            return await fixture.Service.SubmitDiscussionReplyWithPhotosAsync(request, [new PairedUpload(CreatePngFormFile(image), gate)], now);
        })));
        Assert.All(states, result => Assert.Single(Assert.Single(Assert.Single(result.Notes).Replies).Images));
        Assert.Single(Directory.GetFiles(PhotoDirectory(fixture)));
    }

    [Fact]
    public async Task DiscussionPhotos_MultipartControllerAcceptsAnImageWithoutText()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("photo-controller@example.com", "Controller Photos");
        var state = fixture.Service.GetParticipantState(access);
        await using var factory = new TestWebApplicationFactory(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(fixture.Service)));
        using var client = factory.CreateClient();
        using var image = CreatePngStream();
        var replyId = Guid.NewGuid().ToString("N");
        var postId = state.Public.SystemDiscussionPosts.Single(post => post.ParticipantId == state.Participant.Id).Id;
        using var form = new MultipartFormDataContent
        {
            { new StringContent(access), "accessToken" },
            { new StringContent(replyId), "replyId" },
            { new StringContent(postId), "systemPostId" },
            { new ByteArrayContent(image.ToArray()), "photos", "clipboard.png" }
        };
        using var result = await client.PostAsync("/api/longevitymaxxing/discussion/replies", form);
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
        var saved = (await result.Content.ReadFromJsonAsync<LongevitymaxxingParticipantState>())!;
        Assert.Single(Assert.Single(saved.Public.SystemDiscussionPosts.Single(post => post.ParticipantId == state.Participant.Id).Replies).Images);
        using var edit = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/replies/edit", new { accessToken = access, replyId, body = "" });
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());
        using var textOnly = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/replies",
            new LongevitymaxxingDiscussionReplyRequest(access, state.Participant.Id, 0, "Text still works.", Guid.NewGuid().ToString("N"), postId));
        Assert.True(textOnly.IsSuccessStatusCode, await textOnly.Content.ReadAsStringAsync());
    }
}
