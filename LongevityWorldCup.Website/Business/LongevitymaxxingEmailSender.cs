using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.AspNetCore.WebUtilities;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public interface ILongevitymaxxingEmailSender
{
    Task SendConfirmationAsync(string email, string displayName, string confirmationUrl, CancellationToken ct = default);
    Task SendAccessLinkAsync(string email, string displayName, string accessUrl, CancellationToken ct = default);
    Task SendDailyReminderAsync(LongevitymaxxingReminderCandidate reminder, string checkInUrl, string stopUrl, CancellationToken ct = default);
    Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default);
}

public sealed class SmtpLongevitymaxxingEmailSender(Config config, ILogger<SmtpLongevitymaxxingEmailSender> logger) : ILongevitymaxxingEmailSender
{
    private readonly Config _config = config;
    private readonly ILogger<SmtpLongevitymaxxingEmailSender> _logger = logger;
    private readonly Func<SmtpClient> _createClient = () => new SmtpClient();

    internal SmtpLongevitymaxxingEmailSender(Config config, ILogger<SmtpLongevitymaxxingEmailSender> logger, Func<SmtpClient> createClient)
        : this(config, logger) => _createClient = createClient;

    public Task SendConfirmationAsync(string email, string displayName, string confirmationUrl, CancellationToken ct = default)
        => SendAsync(email, displayName, BuildConfirmationEmailContent(displayName, confirmationUrl), ct);

    public Task SendAccessLinkAsync(string email, string displayName, string accessUrl, CancellationToken ct = default)
        => SendAsync(email, displayName, BuildAccessLinkEmailContent(displayName, accessUrl), ct);

    public Task SendDailyReminderAsync(LongevitymaxxingReminderCandidate reminder, string checkInUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildDailyReminderEmailContent(reminder, checkInUrl, stopUrl);
        return SendAsync(reminder.Email, reminder.DisplayName, content, ct);
    }

    public Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildChallengeStartEmailContent(start, challengeUrl, stopUrl);
        return SendAsync(start.Email, start.DisplayName, content, ct);
    }

    internal static LongevitymaxxingEmailContent BuildConfirmationEmailContent(string displayName, string confirmationUrl)
    {
        const string subject = "Confirm your Longevitymaxxing Challenge spot";
        const string note = "After confirmation, this browser can check in without another login.";
        var url = EmailCampaignUrl(confirmationUrl, "confirmation");
        var body =
            $"Hi {SafeName(displayName)},\n\n" +
            $"{subject}:\n" +
            $"{url}\n\n" +
            $"{note}\n\n" +
            "Longevity World Cup";
        var html = new TransactionalEmail(subject)
            .Greeting($"Hi {SafeName(displayName)},")
            .Action(subject, url)
            .Paragraph(note)
            .Render();

        return new LongevitymaxxingEmailContent(subject, body, html);
    }

    internal static LongevitymaxxingEmailContent BuildAccessLinkEmailContent(string displayName, string accessUrl)
    {
        const string subject = "Your Longevitymaxxing Challenge link";
        const string note = "Open it once on a browser and the page will remember you.";
        var url = EmailCampaignUrl(accessUrl, "access_link");
        var body =
            $"Hi {SafeName(displayName)},\n\n" +
            $"{subject}:\n" +
            $"{url}\n\n" +
            $"{note}\n\n" +
            "Longevity World Cup";
        var html = new TransactionalEmail(subject)
            .Greeting($"Hi {SafeName(displayName)},")
            .Action(subject, url)
            .Paragraph(note)
            .Render();

        return new LongevitymaxxingEmailContent(subject, body, html);
    }

    internal static LongevitymaxxingEmailContent BuildDailyReminderEmailContent(
        LongevitymaxxingReminderCandidate reminder,
        string checkInUrl,
        string stopUrl)
    {
        var isPractice = !reminder.CountsForScore;
        var lead = isPractice
            ? $"Day {reminder.ChallengeDay} practice check-in is ready. Check in for {reminder.TargetDate}"
            : $"Day {reminder.ChallengeDay} is ready. Check in for {reminder.TargetDate}";
        var guidance = isPractice
            ? "This first check-in counts for checked-in days and streak, not points. Use it to learn the sleep, exercise, nutrition, and vices flow."
            : "Sleep. Exercise. Nutrition. Vices. Your next move matters.";
        var continuation = reminder.ChallengeDay == 14
            ? "The 14-day sprint does not stop here. The leaderboard keeps going, and daily check-in emails continue until you stop them or miss 3 scored days in a row."
            : null;
        var digest = BuildDiscussionDigest(reminder.DiscussionDigest);
        var url = EmailCampaignUrl(checkInUrl, "daily_reminder");
        var subject = $"Longevitymaxxing Day {reminder.ChallengeDay} check-in";

        var body =
            $"Hi {SafeName(reminder.DisplayName)},\n\n" +
            $"{lead}:\n" +
            $"{url}\n\n" +
            $"{guidance}\n\n" +
            (continuation is null ? "" : $"{continuation}\n\n") +
            (digest is null ? "" : $"{digest.Value.Summary}\n{string.Join("\n", digest.Value.Lines.Select(line => $"- {line}"))}\n{DiscussionDigestFooter}\n\n") +
            $"{StopRemindersText}: {stopUrl}\n\n" +
            "Longevity World Cup";

        var html = new TransactionalEmail(subject)
            .Greeting($"Hi {SafeName(reminder.DisplayName)},")
            .Action(lead, url)
            .Paragraph(guidance);
        if (continuation is not null)
            html.Paragraph(continuation);
        if (digest is not null)
        {
            html.Panel(digest.Value.Summary,
                TransactionalEmail.ListRows(digest.Value.Lines) +
                TransactionalEmail.PanelParagraph(TransactionalEmail.Encode(DiscussionDigestFooter), muted: true));
        }

        return new LongevitymaxxingEmailContent(
            subject,
            body,
            html.FooterLink(stopUrl, StopRemindersText).Render());
    }

    private const string StopRemindersText = "Stop Challenge reminder emails";
    private const string DiscussionDigestFooter = "Open the check-in link above to read and reply.";

    private static (string Summary, List<string> Lines)? BuildDiscussionDigest(LongevitymaxxingDiscussionDigest digest)
    {
        if (digest.TotalCount <= 0 || digest.Items.Count == 0)
            return null;

        const int displayedItemLimit = 3;
        const int displayedAuthorLimit = 3;
        var lines = digest.Items
            .Take(displayedItemLimit)
            .Select(item =>
            {
                var actors = item.ActorDisplayNames
                    .Take(displayedAuthorLimit)
                    .Select(SafeName)
                    .ToList();
                var remainingActors = Math.Max(0, item.ActorDisplayNames.Count - actors.Count);
                var actorText = string.Join(", ", actors);
                if (remainingActors > 0)
                    actorText += $" and {remainingActors} more";

                var postDisplayName = SafeName(item.PostDisplayName ?? "a new participant");

                return item.Kind switch
                {
                    LongevitymaxxingDiscussionActivityKind.Mention when item.SystemPostKind == "announcement"
                        => $"{actorText} mentioned you in a Challenge announcement ({item.Date}).",
                    LongevitymaxxingDiscussionActivityKind.Reply when item.SystemPostKind == "announcement"
                        => $"Your Challenge announcement ({item.Date}): {item.Count} new {(item.Count == 1 ? "reply" : "replies")} from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Mention
                        when item.SystemPostKind == "participant-joined" && item.Count == 1
                        => $"{actorText} mentioned you in the welcome thread for {postDisplayName} ({item.Date}).",
                    LongevitymaxxingDiscussionActivityKind.Mention
                        when item.SystemPostKind == "participant-joined"
                        => $"{item.Count} new mentions in the welcome thread for {postDisplayName} ({item.Date}) from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Reply
                        when item.SystemPostKind == "participant-joined"
                        => $"Your Challenge welcome thread ({item.Date}): {item.Count} new {(item.Count == 1 ? "reply" : "replies")} from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Mention when item.Count == 1
                        => $"{actorText} mentioned you in a Day {item.ChallengeDay} post ({item.Date}).",
                    LongevitymaxxingDiscussionActivityKind.Mention
                        => $"{item.Count} new mentions in a Day {item.ChallengeDay} post ({item.Date}) from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Reply
                        => $"Your Day {item.ChallengeDay} post ({item.Date}): {item.Count} new {(item.Count == 1 ? "reply" : "replies")} from {actorText}.",
                    _ => throw new InvalidOperationException("Unknown discussion activity kind.")
                };
            })
            .ToList();
        var remainingItems = Math.Max(0, digest.Items.Count - lines.Count);
        if (remainingItems > 0)
            lines.Add($"Plus activity on {remainingItems} more discussion {(remainingItems == 1 ? "post" : "posts")}.");

        var summary = new List<string>();
        if (digest.MentionCount > 0)
            summary.Add($"{digest.MentionCount} new {(digest.MentionCount == 1 ? "mention" : "mentions")}");
        if (digest.ReplyCount > 0)
            summary.Add($"{digest.ReplyCount} new {(digest.ReplyCount == 1 ? "reply" : "replies")}");
        return ($"Discussion activity: {string.Join(" and ", summary)}", lines);
    }

    internal static LongevitymaxxingEmailContent BuildChallengeStartEmailContent(
        LongevitymaxxingChallengeStartCandidate start,
        string challengeUrl,
        string stopUrl)
    {
        const string subject = "Longevitymaxxing Challenge check-ins are ready";
        const string ready = "Your Longevitymaxxing Challenge check-ins are ready.";
        const string howTo = "Check in once per day about the previous day: Sleep, Exercise, Nutrition, and Vices. No photos, no long report, no perfect schedule required.";
        const string practice = "Your first eligible check-in is practice: it counts for checked-in days and streak, not points.";
        const string open = "Open your participant page for check-ins, leaderboard, and Slack";
        var timeZone = $"Timezone: {SafeTimeZoneLabel(start.TimeZoneId)}";
        var url = EmailCampaignUrl(challengeUrl, "challenge_start");
        var body =
            $"Hi {SafeName(start.DisplayName)},\n\n" +
            $"{ready}\n\n" +
            $"{howTo}\n" +
            $"{practice}\n" +
            $"{timeZone}\n\n" +
            $"{open}:\n{url}\n\n" +
            $"{StopRemindersText}: {stopUrl}\n\n" +
            "Longevity World Cup";
        var html = new TransactionalEmail(subject)
            .Greeting($"Hi {SafeName(start.DisplayName)},")
            .Paragraph(ready)
            .Lead(howTo, practice)
            .Meta(timeZone)
            .Action(open, url)
            .FooterLink(stopUrl, StopRemindersText)
            .Render();

        return new LongevitymaxxingEmailContent(subject, body, html);
    }

    private static string EmailCampaignUrl(string url, string content)
        => QueryHelpers.AddQueryString(url, new Dictionary<string, string?>
        {
            ["utm_source"] = "longevityworldcup",
            ["utm_medium"] = "email",
            ["utm_campaign"] = "longevitymaxxing",
            ["utm_content"] = content
        });

    private async Task SendAsync(
        string email,
        string displayName,
        LongevitymaxxingEmailContent content,
        CancellationToken ct)
    {
        var smtpServer = RequireConfiguredValue(_config.SmtpServer, nameof(_config.SmtpServer));
        var smtpUser = RequireConfiguredValue(_config.SmtpUser, nameof(_config.SmtpUser));
        var smtpPort = RequireConfiguredPort(_config.SmtpPort);
        var smtpPassword = _config.GetSmtpPassword();

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Longevity World Cup", RequireConfiguredValue(_config.EmailFrom, nameof(_config.EmailFrom))));
        message.To.Add(new MailboxAddress(displayName ?? string.Empty, email));
        message.Subject = content.Subject;
        message.Body = new BodyBuilder { TextBody = content.TextBody, HtmlBody = content.HtmlBody }.ToMessageBody();

        using var client = _createClient();
        client.Timeout = checked((int)PublicRequestTimeoutPolicies.ApplicationExternalDependencyTimeout.TotalMilliseconds);
        await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls, ct).ConfigureAwait(false);
        await client.AuthenticateAsync(smtpUser, smtpPassword, ct).ConfigureAwait(false);
        await SmtpMessageDelivery.SendAsync(client, message, _logger, ct).ConfigureAwait(false);

        _logger.LogInformation("SMTP accepted a Longevitymaxxing email.");
    }

    private static string SafeName(string? displayName)
    {
        var name = (displayName ?? "").Trim();
        return string.IsNullOrWhiteSpace(name) ? "there" : name;
    }

    private static string SafeTimeZoneLabel(string? timeZoneId)
    {
        var value = (timeZoneId ?? "").Trim();
        return string.IsNullOrWhiteSpace(value) ? "UTC" : value;
    }

    private static string RequireConfiguredValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is not configured.");
        return value.Trim();
    }

    private static int RequireConfiguredPort(int value)
    {
        if (value <= 0)
            throw new InvalidOperationException($"{nameof(Config.SmtpPort)} is not configured.");
        return value;
    }
}

internal sealed record LongevitymaxxingEmailContent(
    string Subject,
    string TextBody,
    string HtmlBody);
