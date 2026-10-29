using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmtpAcceptedThenQuitFailed_RecordsStartAndDailyDigestWithoutRepeating(bool rejectFirstAttempt)
    {
        using var fixture = TestChallengeFixture.Create();
        var author = await fixture.ConfirmParticipantAsync("author@example.test", "Author Ana");
        var replier = await fixture.ConfirmParticipantAsync("reply@example.test", "Reply Rae");
        var post = fixture.Service.SubmitCheckIn(new(author, 1, 2, 2, 2, 2, "Read my post."),
            DateTimeOffset.Parse("2026-06-09T07:00:00Z"));
        fixture.Service.SubmitDiscussionReply(new(replier, post.Participant.Id, 1, "Read my reply.", Guid.NewGuid().ToString("D")),
            DateTimeOffset.Parse("2026-06-09T07:15:00Z"));
        var attempts = new List<EmailTestSmtpClient>();
        var reject = rejectFirstAttempt;
        var sender = new SmtpLongevitymaxxingEmailSender(fixture.Config, NullLogger<SmtpLongevitymaxxingEmailSender>.Instance, () =>
        {
            var smtp = new EmailTestSmtpClient
            {
                SendFailure = reject ? new IOException("No DATA acknowledgement") : null,
                DisconnectFailure = new IOException("QUIT response lost")
            };
            attempts.Add(smtp);
            return smtp;
        });
        using var events = CreateEventDataService(fixture);
        var job = new LongevitymaxxingReminderJob(fixture.Service, events, sender, NullLogger<LongevitymaxxingReminderJob>.Instance);
        var now = DateTimeOffset.Parse("2026-06-10T08:05:00Z");
        var expected = fixture.Service.GetChallengeStartCandidates(now).Count + fixture.Service.GetDailyReminderCandidates(now).Count;
        Assert.True(expected > 0);

        await job.ExecuteAtAsync(now);
        Assert.Equal(expected, attempts.Count);
        if (rejectFirstAttempt)
        {
            Assert.NotEmpty(fixture.Service.GetChallengeStartCandidates(now));
            Assert.Contains(fixture.Service.GetDailyReminderCandidates(now), reminder => reminder.DiscussionDigest.ReplyCount == 1);
            reject = false;
            await job.ExecuteAtAsync(now.AddMinutes(1));
            Assert.Equal(expected * 2, attempts.Count);
        }

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(now.AddMinutes(2)));
        Assert.Empty(fixture.Service.GetDailyReminderCandidates(now.AddMinutes(2)));
        var sentCount = attempts.Count;
        await job.ExecuteAtAsync(now.AddMinutes(2));
        Assert.Equal(sentCount, attempts.Count);
        var nextDay = fixture.Service.GetDailyReminderCandidates(now.AddDays(1));
        Assert.All(nextDay, reminder => Assert.Empty(reminder.DiscussionDigest.NotificationIds));
    }
}
