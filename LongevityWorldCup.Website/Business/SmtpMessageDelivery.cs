using MailKit.Net.Smtp;
using MimeKit;

namespace LongevityWorldCup.Website.Business;

internal static class SmtpMessageDelivery
{
    internal static async Task SendAsync(SmtpClient client, MimeMessage message, ILogger logger, CancellationToken ct)
    {
        // Only a successful DATA acknowledgement counts as accepted. Connection, authentication,
        // rejection, cancellation and lost acknowledgements must still reach the caller.
        await client.SendAsync(message, ct).ConfigureAwait(false);

        // QUIT cannot undo acceptance. Propagating its failure would cause callers to retry
        // accepted reminders or archive an application whose audit email was already accepted.
        try { await client.DisconnectAsync(true, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            // SMTP responses can contain recipient addresses and private message content.
            logger.LogWarning("SMTP disconnect failed after accepting the message. ExceptionType={ExceptionType}", ex.GetType().Name);
        }
    }
}
