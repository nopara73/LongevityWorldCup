using LongevityWorldCup.Website.Tools;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LongevityWorldCup.Website.Business;

public interface IApplicationPaymentEmailSender
{
    Task SendAsync(Config config, ApplicationPayment payment, Action beforeSend, CancellationToken ct);
}

// An explicit negative SMTP response permits retry. All other exceptions after beforeSend are ambiguous.
public sealed class PaymentEmailNotAcceptedException(Exception inner) : Exception("SMTP rejected the payment notification.", inner);

public sealed class SmtpApplicationPaymentEmailSender(ILogger<SmtpApplicationPaymentEmailSender> logger) : IApplicationPaymentEmailSender
{
    private readonly Func<SmtpClient> _createClient = () => new SmtpClient();

    internal SmtpApplicationPaymentEmailSender(ILogger<SmtpApplicationPaymentEmailSender> logger, Func<SmtpClient> createClient)
        : this(logger) => _createClient = createClient;

    public async Task SendAsync(Config config, ApplicationPayment payment, Action beforeSend, CancellationToken ct)
    {
        using var message = BuildMessage(config, payment);
        using var client = _createClient();
        client.Timeout = checked((int)PublicRequestTimeoutPolicies.ApplicationExternalDependencyTimeout.TotalMilliseconds);
        await client.ConnectAsync(Required(config.SmtpServer), config.SmtpPort, SecureSocketOptions.StartTls, ct);
        var user = Required(config.SmtpUser);
        var password = Environment.GetEnvironmentVariable("LWC_SMTP_PASSWORD");
        if (string.IsNullOrWhiteSpace(password)) password = config.SmtpPassword;
        if (!string.IsNullOrWhiteSpace(password))
            await client.AuthenticateAsync(user, password.Trim(), ct);
        else
        {
            var token = await GmailAuth.GetAccessTokenAsync(config, ct);
            client.AuthenticationMechanisms.Remove("LOGIN");
            client.AuthenticationMechanisms.Remove("PLAIN");
            await client.AuthenticateAsync(new SaslMechanismOAuth2(user, token), ct);
        }

        ct.ThrowIfCancellationRequested();
        beforeSend();
        try { await client.SendAsync(message, ct); }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode is >= 400 and < 600)
        {
            throw new PaymentEmailNotAcceptedException(ex);
        }

        // A failed QUIT does not undo the SMTP server's acknowledgement of DATA.
        try { await client.DisconnectAsync(true, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "SMTP disconnect failed after accepting payment message {MessageId}", payment.MessageId); }
    }

    internal static MimeMessage BuildMessage(Config config, ApplicationPayment payment)
    {
        var invoice = payment.PaidInvoice ?? throw new InvalidOperationException("Payment evidence is required before sending.");
        var message = new MimeMessage { MessageId = payment.MessageId, Subject = BuildSubject(payment.ApplicantName) };
        message.From.Add(new MailboxAddress("Longevity World Cup", Required(config.EmailFrom)));
        message.To.Add(new MailboxAddress(string.Empty, Required(config.EmailTo)));
        if (MailboxAddress.TryParse(payment.AccountEmail, out var replyTo))
            message.ReplyTo.Add(new MailboxAddress(payment.ApplicantName?.Trim() ?? string.Empty, replyTo.Address));
        message.Body = new BodyBuilder
        {
            TextBody = string.Join("\n", new[]
            {
                BuildPaymentFollowupIntro(payment.SubmissionType),
                $"Invoice ID: {payment.InvoiceId}",
                $"Status: {invoice.Status ?? "unknown"}",
                $"Additional status: {invoice.AdditionalStatus ?? "unknown"}",
                $"Amount: {invoice.AmountText ?? "?"} {invoice.Currency ?? "?"}",
                $"Paid amount: {invoice.PaidAmountText ?? "?"} {invoice.Currency ?? "?"}",
                $"Checkout link: {invoice.CheckoutLink ?? "n/a"}",
                $"{BuildPaymentFollowupContactLabel(payment.SubmissionType)}: {payment.AccountEmail ?? "n/a"}"
            })
        }.ToMessageBody();
        return message;
    }

    internal static string BuildSubject(string? name) => $"[LWC26] Application: {name?.Trim() ?? "Unknown"}";

    internal static string BuildPaymentFollowupIntro(string? submissionType)
        => submissionType?.Trim().ToLowerInvariant() switch
        {
            "result" => "Payment detected for result upload.",
            "edit" => "Payment detected for profile change request.",
            _ => "Payment detected for submitted application."
        };

    internal static string BuildPaymentFollowupContactLabel(string? submissionType)
        => submissionType?.Trim().ToLowerInvariant() is "result" or "edit" ? "Athlete email" : "Applicant email";

    private static string Required(string? value) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim() : throw new InvalidOperationException("Application payment email settings are incomplete.");
}
