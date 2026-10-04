using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.AspNetCore.WebUtilities;

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

    public Task SendConfirmationAsync(string email, string displayName, string confirmationUrl, CancellationToken ct = default)
    {
        var body =
            $"Hi {SafeName(displayName)},\n\n" +
            "Confirm your Longevitymaxxing Challenge spot:\n" +
            $"{EmailCampaignUrl(confirmationUrl, "confirmation")}\n\n" +
            "After confirmation, this browser can check in without another login.\n\n" +
            "Longevity World Cup";

        return SendAsync(email, displayName, "Confirm your Longevitymaxxing Challenge spot", body, ct);
    }

    public Task SendAccessLinkAsync(string email, string displayName, string accessUrl, CancellationToken ct = default)
    {
        var body =
            $"Hi {SafeName(displayName)},\n\n" +
            "Your Longevitymaxxing Challenge link:\n" +
            $"{EmailCampaignUrl(accessUrl, "access_link")}\n\n" +
            "Open it once on a browser and the page will remember you.\n\n" +
            "Longevity World Cup";

        return SendAsync(email, displayName, "Your Longevitymaxxing Challenge link", body, ct);
    }

    public Task SendDailyReminderAsync(LongevitymaxxingReminderCandidate reminder, string checkInUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildDailyReminderEmailContent(reminder, checkInUrl, stopUrl);
        return SendAsync(reminder.Email, reminder.DisplayName, content.Subject, content.TextBody, ct);
    }

    public Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildChallengeStartEmailContent(start, challengeUrl, stopUrl);
        return SendAsync(start.Email, start.DisplayName, content.Subject, content.TextBody, ct);
    }

    internal static LongevitymaxxingEmailContent BuildDailyReminderEmailContent(
        LongevitymaxxingReminderCandidate reminder,
        string checkInUrl,
        string stopUrl)
    {
        var isPractice = !reminder.CountsForScore;
        var lead = isPractice
            ? $"Day {reminder.ChallengeDay} practice check-in is ready. Check in for {reminder.TargetDate}:"
            : $"Day {reminder.ChallengeDay} is ready. Check in for {reminder.TargetDate}:";
        var guidance = isPractice
            ? "This first check-in counts for checked-in days and streak, not points. Use it to learn the sleep, exercise, nutrition, and vices flow."
            : "Sleep. Exercise. Nutrition. Vices. Your next move matters.";
        var continuation = reminder.ChallengeDay == 14
            ? "The 14-day sprint does not stop here. The leaderboard keeps going, and daily check-in emails continue until you stop them or miss 3 scored days in a row.\n\n"
            : "";
        var discussionDigest = BuildDiscussionDigestBlock(reminder.DiscussionDigest);

        var body =
            $"Hi {SafeName(reminder.DisplayName)},\n\n" +
            $"{lead}\n" +
            $"{EmailCampaignUrl(checkInUrl, "daily_reminder")}\n\n" +
            $"{guidance}\n\n" +
            $"{continuation}" +
            $"{discussionDigest}" +
            $"Stop Challenge reminder emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return new LongevitymaxxingEmailContent(
            $"Longevitymaxxing Day {reminder.ChallengeDay} check-in",
            body);
    }

    private static string BuildDiscussionDigestBlock(LongevitymaxxingDiscussionDigest digest)
    {
        if (digest.TotalCount <= 0 || digest.Items.Count == 0)
            return "";

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
                    LongevitymaxxingDiscussionActivityKind.Mention
                        when item.SystemPostKind == "participant-joined" && item.Count == 1
                        => $"- {actorText} mentioned you in the welcome thread for {postDisplayName} ({item.Date}).",
                    LongevitymaxxingDiscussionActivityKind.Mention
                        when item.SystemPostKind == "participant-joined"
                        => $"- {item.Count} new mentions in the welcome thread for {postDisplayName} ({item.Date}) from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Reply
                        when item.SystemPostKind == "participant-joined"
                        => $"- Your Challenge welcome thread ({item.Date}): {item.Count} new {(item.Count == 1 ? "reply" : "replies")} from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Mention when item.Count == 1
                        => $"- {actorText} mentioned you in a Day {item.ChallengeDay} post ({item.Date}).",
                    LongevitymaxxingDiscussionActivityKind.Mention
                        => $"- {item.Count} new mentions in a Day {item.ChallengeDay} post ({item.Date}) from {actorText}.",
                    LongevitymaxxingDiscussionActivityKind.Reply
                        => $"- Your Day {item.ChallengeDay} post ({item.Date}): {item.Count} new {(item.Count == 1 ? "reply" : "replies")} from {actorText}.",
                    _ => throw new InvalidOperationException("Unknown discussion activity kind.")
                };
            })
            .ToList();
        var remainingItems = Math.Max(0, digest.Items.Count - lines.Count);
        if (remainingItems > 0)
            lines.Add($"- Plus activity on {remainingItems} more discussion {(remainingItems == 1 ? "post" : "posts")}.");

        var summary = new List<string>();
        if (digest.MentionCount > 0)
            summary.Add($"{digest.MentionCount} new {(digest.MentionCount == 1 ? "mention" : "mentions")}");
        if (digest.ReplyCount > 0)
            summary.Add($"{digest.ReplyCount} new {(digest.ReplyCount == 1 ? "reply" : "replies")}");
        return
            $"Discussion activity: {string.Join(" and ", summary)}\n" +
            string.Join("\n", lines) +
            "\nOpen the check-in link above to read and reply.\n\n";
    }

    internal static LongevitymaxxingEmailContent BuildChallengeStartEmailContent(
        LongevitymaxxingChallengeStartCandidate start,
        string challengeUrl,
        string stopUrl)
    {
        var body =
            $"Hi {SafeName(start.DisplayName)},\n\n" +
            "Your Longevitymaxxing Challenge check-ins are ready.\n\n" +
            "Check in once per day about the previous day: Sleep, Exercise, Nutrition, and Vices. No photos, no long report, no perfect schedule required.\n" +
            "Your first eligible check-in is practice: it counts for checked-in days and streak, not points.\n" +
            $"Timezone: {SafeTimeZoneLabel(start.TimeZoneId)}\n\n" +
            $"Open your participant page for check-ins, leaderboard, and Slack:\n{EmailCampaignUrl(challengeUrl, "challenge_start")}\n\n" +
            $"Stop Challenge reminder emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return new LongevitymaxxingEmailContent("Longevitymaxxing Challenge check-ins are ready", body);
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
        string subject,
        string textBody,
        CancellationToken ct)
    {
        var smtpServer = RequireConfiguredValue(_config.SmtpServer, nameof(_config.SmtpServer));
        var smtpUser = RequireConfiguredValue(_config.SmtpUser, nameof(_config.SmtpUser));
        var smtpPort = RequireConfiguredPort(_config.SmtpPort);
        var smtpPassword = GetConfiguredSecret(_config.SmtpPassword, "LWC_SMTP_PASSWORD");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Longevity World Cup", RequireConfiguredValue(_config.EmailFrom, nameof(_config.EmailFrom))));
        message.To.Add(new MailboxAddress(displayName ?? string.Empty, email));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = textBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls, ct).ConfigureAwait(false);
        await client.AuthenticateAsync(smtpUser, smtpPassword, ct).ConfigureAwait(false);
        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);

        _logger.LogInformation("Sent Longevitymaxxing email '{Subject}' to {Email}", subject, email);
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

    private static string GetConfiguredSecret(string? configValue, string environmentVariableName)
    {
        var envValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        if (!string.IsNullOrWhiteSpace(configValue))
            return configValue.Trim();

        throw new InvalidOperationException($"{environmentVariableName} is not configured.");
    }
}

internal sealed record LongevitymaxxingEmailContent(
    string Subject,
    string TextBody);
