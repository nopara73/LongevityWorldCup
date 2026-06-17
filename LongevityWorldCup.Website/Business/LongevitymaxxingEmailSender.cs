using System.Globalization;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LongevityWorldCup.Website.Business;

public interface ILongevitymaxxingEmailSender
{
    Task SendConfirmationAsync(string email, string displayName, string confirmationUrl, CancellationToken ct = default);
    Task SendAccessLinkAsync(string email, string displayName, string accessUrl, CancellationToken ct = default);
    Task SendDailyReminderAsync(LongevitymaxxingReminderCandidate reminder, string checkInUrl, string stopUrl, CancellationToken ct = default);
    Task SendCallReminderAsync(LongevitymaxxingCallReminderCandidate reminder, string challengeUrl, string stopUrl, CancellationToken ct = default);
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
            $"{confirmationUrl}\n\n" +
            "After confirmation, this browser can check in without another login.\n\n" +
            "Longevity World Cup";

        return SendAsync(email, displayName, "Confirm your Longevitymaxxing Challenge spot", body, ct);
    }

    public Task SendAccessLinkAsync(string email, string displayName, string accessUrl, CancellationToken ct = default)
    {
        var body =
            $"Hi {SafeName(displayName)},\n\n" +
            "Your Longevitymaxxing Challenge link:\n" +
            $"{accessUrl}\n\n" +
            "Open it once on a browser and the page will remember you.\n\n" +
            "Longevity World Cup";

        return SendAsync(email, displayName, "Your Longevitymaxxing Challenge link", body, ct);
    }

    public Task SendDailyReminderAsync(LongevitymaxxingReminderCandidate reminder, string checkInUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildDailyReminderEmailContent(reminder, checkInUrl, stopUrl);
        return SendAsync(reminder.Email, reminder.DisplayName, content.Subject, content.TextBody, ct, content.Attachments);
    }

    public Task SendCallReminderAsync(LongevitymaxxingCallReminderCandidate reminder, string challengeUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildCallReminderEmailContent(reminder, challengeUrl, stopUrl);
        return SendAsync(reminder.Email, reminder.DisplayName, content.Subject, content.TextBody, ct, content.Attachments);
    }

    public Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default)
    {
        var content = BuildChallengeStartEmailContent(start, challengeUrl, stopUrl);
        return SendAsync(start.Email, start.DisplayName, content.Subject, content.TextBody, ct, content.Attachments);
    }

    internal static LongevitymaxxingEmailContent BuildDailyReminderEmailContent(
        LongevitymaxxingReminderCandidate reminder,
        string checkInUrl,
        string stopUrl)
    {
        if (reminder.IsCommitmentPaymentReminder)
        {
            var amount = reminder.CommitmentOwedAmountUsd is decimal owed
                ? $"USD {owed:0.##}"
                : "your configured amount";
            var paymentBody =
                $"Hi {SafeName(reminder.DisplayName)},\n\n" +
                $"Your Longevitymaxxing commitment is due for Day {reminder.CommitmentTriggerChallengeDay}: {amount}.\n\n" +
                "That check-in landed below your recent average. Open your participant page to pay, or fix the triggering check-in while it is still editable:\n" +
                $"{checkInUrl}\n\n" +
                $"Stop challenge emails: {stopUrl}\n\n" +
                "Longevity World Cup";

            return new LongevitymaxxingEmailContent(
                $"Longevitymaxxing commitment due for Day {reminder.CommitmentTriggerChallengeDay}",
                paymentBody,
                []);
        }

        var isPractice = !reminder.CountsForScore;
        var lead = isPractice
            ? $"Day {reminder.ChallengeDay} practice check-in is ready. Check in for {reminder.TargetDate}:"
            : $"Day {reminder.ChallengeDay} is ready. Check in for {reminder.TargetDate}:";
        var guidance = isPractice
            ? "This first check-in counts for checked-in days and streak, not points. Use it to learn the sleep, exercise, nutrition, and vices flow."
            : "Sleep. Exercise. Nutrition. Vices. Keep the board moving.";
        var continuation = reminder.ChallengeDay == 14
            ? "The 14-day sprint does not stop here. The leaderboard keeps going, and daily check-in emails continue until you stop them or miss 3 scored days in a row.\n\n"
            : "";
        var schedule = BuildScheduleBlock(reminder.Calls, reminder.TimeZoneId);
        var scheduleUpdate = reminder.IncludeCallScheduleUpdate && reminder.Calls.Any(call => call.SelectedSlot is not null)
            ? $"Updated call schedule:\n{schedule}\n\n"
            : "";

        var body =
            $"Hi {SafeName(reminder.DisplayName)},\n\n" +
            $"{lead}\n" +
            $"{checkInUrl}\n\n" +
            $"{guidance}\n\n" +
            $"{continuation}" +
            $"{scheduleUpdate}" +
            $"Stop challenge emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return new LongevitymaxxingEmailContent(
            $"Longevitymaxxing Day {reminder.ChallengeDay} check-in",
            body,
            []);
    }

    internal static LongevitymaxxingEmailContent BuildCallReminderEmailContent(
        LongevitymaxxingCallReminderCandidate reminder,
        string challengeUrl,
        string stopUrl)
    {
        var localStartsAt = FormatInParticipantTimeZone(reminder.StartsAtUtc, reminder.TimeZoneId);
        var link = string.IsNullOrWhiteSpace(reminder.VideoCallUrl)
            ? "Call link: not configured yet."
            : $"Call link:\n{reminder.VideoCallUrl}";

        var body =
            $"Hi {SafeName(reminder.DisplayName)},\n\n" +
            $"The Longevitymaxxing {reminder.CallLabel} call starts at {localStartsAt}.\n" +
            $"{link}\n\n" +
            $"Participant page:\n{challengeUrl}\n\n" +
            $"Stop challenge emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return new LongevitymaxxingEmailContent(
            $"Longevitymaxxing {reminder.CallLabel} call reminder",
            body,
            []);
    }

    internal static LongevitymaxxingEmailContent BuildChallengeStartEmailContent(
        LongevitymaxxingChallengeStartCandidate start,
        string challengeUrl,
        string stopUrl)
    {
        var callLines = start.Calls
            .Where(call => call.SelectedSlot is not null)
            .Select(call => FormatCallLine(call, start.TimeZoneId))
            .ToList();
        var calls = callLines.Count == 0
            ? ""
            : "\n\nCalls:\n" + string.Join("\n", callLines);
        var attachmentText = callLines.Count == 0
            ? ""
            : "\nA calendar invite with all selected calls is attached.\n";
        var attachments = BuildCalendarAttachment(
            "longevitymaxxing-calls.ics",
            "Longevitymaxxing Challenge calls",
            start.Calls,
            challengeUrl);

        var body =
            $"Hi {SafeName(start.DisplayName)},\n\n" +
            "Your Longevitymaxxing Challenge check-ins are ready.\n\n" +
            "Check in once per day about the previous day: Sleep, Exercise, Nutrition, and Vices. No photos, no long report, no perfect schedule required.\n" +
            "Your first eligible check-in is practice: it counts for checked-in days and streak, not points.\n" +
            $"Timezone: {SafeTimeZoneLabel(start.TimeZoneId)}\n" +
            $"{calls}\n\n" +
            $"{attachmentText}" +
            $"Open your participant page for check-ins, leaderboard, Slack, and meeting links:\n{challengeUrl}\n\n" +
            $"Stop challenge emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return new LongevitymaxxingEmailContent("Longevitymaxxing Challenge check-ins are ready", body, attachments);
    }

    private async Task SendAsync(
        string email,
        string displayName,
        string subject,
        string textBody,
        CancellationToken ct,
        IReadOnlyList<LongevitymaxxingEmailAttachment>? attachments = null)
    {
        var smtpServer = RequireConfiguredValue(_config.SmtpServer, nameof(_config.SmtpServer));
        var smtpUser = RequireConfiguredValue(_config.SmtpUser, nameof(_config.SmtpUser));
        var smtpPort = RequireConfiguredPort(_config.SmtpPort);
        var smtpPassword = GetConfiguredSecret(_config.SmtpPassword, "LWC_SMTP_PASSWORD");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Longevity World Cup", RequireConfiguredValue(_config.EmailFrom, nameof(_config.EmailFrom))));
        message.To.Add(new MailboxAddress(displayName ?? string.Empty, email));
        message.Subject = subject;
        message.Body = BuildBody(textBody, attachments);

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls, ct).ConfigureAwait(false);
        await client.AuthenticateAsync(smtpUser, smtpPassword, ct).ConfigureAwait(false);
        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);

        _logger.LogInformation("Sent Longevitymaxxing email '{Subject}' to {Email}", subject, email);
    }

    private static MimeEntity BuildBody(string textBody, IReadOnlyList<LongevitymaxxingEmailAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return new TextPart("plain") { Text = textBody };

        var builder = new BodyBuilder { TextBody = textBody };
        foreach (var attachment in attachments)
        {
            builder.Attachments.Add(
                attachment.FileName,
                Encoding.UTF8.GetBytes(attachment.Text),
                ContentType.Parse(attachment.ContentType));
        }

        return builder.ToMessageBody();
    }

    private static string SafeName(string? displayName)
    {
        var name = (displayName ?? "").Trim();
        return string.IsNullOrWhiteSpace(name) ? "there" : name;
    }

    private static string BuildScheduleBlock(IReadOnlyList<LongevitymaxxingParticipantCall> calls, string timeZoneId)
    {
        var callLines = calls
            .Where(call => call.SelectedSlot is not null)
            .Select(call => FormatCallLine(call, timeZoneId))
            .ToList();

        return callLines.Count == 0
            ? "not selected yet."
            : string.Join("\n", callLines);
    }

    private static string FormatCallLine(LongevitymaxxingParticipantCall call, string timeZoneId)
    {
        var startsAt = call.SelectedSlot is null
            ? "time pending"
            : FormatInParticipantTimeZone(call.SelectedSlot.StartsAtUtc, timeZoneId);
        var link = string.IsNullOrWhiteSpace(call.VideoCallUrl)
            ? ""
            : $"\n  Call link: {call.VideoCallUrl}";

        return $"- {call.Label}: {startsAt}{link}";
    }

    private static string FormatInParticipantTimeZone(string startsAtUtc, string timeZoneId)
    {
        if (!DateTimeOffset.TryParse(startsAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return startsAtUtc;

        var local = TimeZoneInfo.ConvertTime(parsed.ToUniversalTime(), ResolveTimeZone(timeZoneId));
        return $"{local:yyyy-MM-dd HH:mm} ({SafeTimeZoneLabel(timeZoneId)})";
    }

    private static string SafeTimeZoneLabel(string? timeZoneId)
    {
        var value = (timeZoneId ?? "").Trim();
        return string.IsNullOrWhiteSpace(value) ? "UTC" : value;
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }
                catch
                {
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    private static IReadOnlyList<LongevitymaxxingEmailAttachment> BuildCalendarAttachment(
        string fileName,
        string calendarName,
        IReadOnlyList<LongevitymaxxingParticipantCall> calls,
        string challengeUrl)
    {
        var selectedCalls = calls
            .Where(call => call.SelectedSlot is not null)
            .ToList();
        if (selectedCalls.Count == 0)
            return [];

        var builder = new StringBuilder()
            .AppendLine("BEGIN:VCALENDAR")
            .AppendLine("VERSION:2.0")
            .AppendLine("PRODID:-//Longevity World Cup//Longevitymaxxing Challenge//EN")
            .AppendLine("CALSCALE:GREGORIAN")
            .AppendLine("METHOD:PUBLISH")
            .AppendLine($"X-WR-CALNAME:{EscapeCalendarText(calendarName)}");

        foreach (var call in selectedCalls)
        {
            if (!DateTimeOffset.TryParse(call.SelectedSlot!.StartsAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                continue;

            var startsAt = parsed.ToUniversalTime();
            var endsAt = startsAt.AddHours(1);
            var description = string.IsNullOrWhiteSpace(call.VideoCallUrl)
                ? $"Participant page: {challengeUrl}"
                : $"Call link: {call.VideoCallUrl}\nParticipant page: {challengeUrl}";
            builder
                .AppendLine("BEGIN:VEVENT")
                .AppendLine($"UID:{BuildCalendarUid(call, startsAt)}")
                .AppendLine($"DTSTAMP:{FormatCalendarUtc(startsAt)}")
                .AppendLine($"DTSTART:{FormatCalendarUtc(startsAt)}")
                .AppendLine($"DTEND:{FormatCalendarUtc(endsAt)}")
                .AppendLine($"SUMMARY:{EscapeCalendarText($"Longevitymaxxing {call.Label} call")}")
                .AppendLine($"DESCRIPTION:{EscapeCalendarText(description)}");

            if (!string.IsNullOrWhiteSpace(call.VideoCallUrl))
                builder.AppendLine($"LOCATION:{EscapeCalendarText(call.VideoCallUrl)}");

            builder.AppendLine("END:VEVENT");
        }

        builder.AppendLine("END:VCALENDAR");
        return
        [
            new LongevitymaxxingEmailAttachment(
                fileName,
                "text/calendar; charset=utf-8",
                builder.ToString())
        ];
    }

    private static string BuildCalendarUid(LongevitymaxxingParticipantCall call, DateTimeOffset startsAt)
        => $"longevitymaxxing-{SanitizeUidPart(call.Key)}-{FormatCalendarUtc(startsAt)}@longevityworldcup.com";

    private static string SanitizeUidPart(string value)
        => new((value ?? "").Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray());

    private static string FormatCalendarUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string EscapeCalendarText(string value)
        => (value ?? "")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal);

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
    string TextBody,
    IReadOnlyList<LongevitymaxxingEmailAttachment> Attachments);

internal sealed record LongevitymaxxingEmailAttachment(
    string FileName,
    string ContentType,
    string Text);
