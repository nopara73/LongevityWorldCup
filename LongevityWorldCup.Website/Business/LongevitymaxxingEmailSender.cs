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
        var body =
            $"Hi {SafeName(reminder.DisplayName)},\n\n" +
            $"Day {reminder.ChallengeDay} is ready. Check in for {reminder.TargetDate}:\n" +
            $"{checkInUrl}\n\n" +
            "Sleep. Exercise. Nutrition. Vices. Keep the board moving.\n\n" +
            $"Stop challenge emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return SendAsync(reminder.Email, reminder.DisplayName, $"Longevitymaxxing Day {reminder.ChallengeDay} check-in", body, ct);
    }

    public Task SendCallReminderAsync(LongevitymaxxingCallReminderCandidate reminder, string challengeUrl, string stopUrl, CancellationToken ct = default)
    {
        var startsAt = DateTimeOffset.TryParse(reminder.StartsAtUtc, out var parsed)
            ? parsed.ToString("yyyy-MM-dd HH:mm 'UTC'")
            : reminder.StartsAtUtc;
        var body =
            $"Hi {SafeName(reminder.DisplayName)},\n\n" +
            $"The Longevitymaxxing {reminder.CallLabel} call starts at {startsAt}.\n\n" +
            $"Open the participant page for the call link:\n{challengeUrl}\n\n" +
            $"Stop challenge emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return SendAsync(reminder.Email, reminder.DisplayName, $"Longevitymaxxing {reminder.CallLabel} call reminder", body, ct);
    }

    public Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default)
    {
        var callLines = start.Calls
            .Where(call => call.SelectedSlot is not null)
            .Select(call => $"- {call.Label}: {FormatUtc(call.SelectedSlot!.StartsAtUtc)}")
            .ToList();
        var calls = callLines.Count == 0
            ? ""
            : "\n\nCalls:\n" + string.Join("\n", callLines);

        var body =
            $"Hi {SafeName(start.DisplayName)},\n\n" +
            "The Longevitymaxxing Challenge is starting.\n\n" +
            "For 14 days, check in once per day about the previous day: Sleep, Exercise, Nutrition, and Vices. No photos, no long report, no perfect schedule required.\n" +
            $"{calls}\n\n" +
            $"Open your participant page for check-ins, leaderboard, Slack, and meeting links:\n{challengeUrl}\n\n" +
            $"Stop challenge emails: {stopUrl}\n\n" +
            "Longevity World Cup";

        return SendAsync(start.Email, start.DisplayName, "Longevitymaxxing Challenge starts now", body, ct);
    }

    private async Task SendAsync(string email, string displayName, string subject, string textBody, CancellationToken ct)
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

    private static string FormatUtc(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'")
            : value;
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
