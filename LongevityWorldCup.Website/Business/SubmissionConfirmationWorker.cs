using System.Text;
using LongevityWorldCup.Website.Tools;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LongevityWorldCup.Website.Business;

public interface ISubmissionConfirmationSender
{
    Task SendAsync(Config config, MimeMessage message, Action beforeSend, CancellationToken ct);
}

internal sealed class SubmissionConfirmationRejectedException(Exception inner)
    : Exception("SMTP rejected the submission confirmation.", inner);

public sealed class SmtpSubmissionConfirmationSender(ILogger<SmtpSubmissionConfirmationSender> logger) : ISubmissionConfirmationSender
{
    private readonly Func<SmtpClient> _createClient = () => new SmtpClient();

    internal SmtpSubmissionConfirmationSender(ILogger<SmtpSubmissionConfirmationSender> logger, Func<SmtpClient> createClient)
        : this(logger) => _createClient = createClient;

    public async Task SendAsync(Config config, MimeMessage message, Action beforeSend, CancellationToken ct)
    {
        var password = config.GetSmtpPassword();
        var host = !string.IsNullOrWhiteSpace(config.SmtpServer) ? config.SmtpServer.Trim()
            : throw new InvalidOperationException("SMTP server is not configured.");
        var user = !string.IsNullOrWhiteSpace(config.SmtpUser) ? config.SmtpUser.Trim()
            : throw new InvalidOperationException("SMTP user is not configured.");
        using var client = _createClient();
        client.Timeout = checked((int)PublicRequestTimeoutPolicies.ApplicationExternalDependencyTimeout.TotalMilliseconds);
        await client.ConnectAsync(host, config.SmtpPort, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(user, password, ct);
        ct.ThrowIfCancellationRequested();
        beforeSend();
        try { await SmtpMessageDelivery.SendAsync(client, message, logger, ct); }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode is >= 400 and < 600)
        {
            throw new SubmissionConfirmationRejectedException(ex);
        }
    }
}

public sealed class SubmissionConfirmationDispatcher(SubmissionConfirmationStore store,
    ISubmissionConfirmationSender sender, Config config, TimeProvider clock, ILogger<SubmissionConfirmationDispatcher> logger)
{
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        foreach (var key in store.GetDue(clock.GetUtcNow()))
        {
            ct.ThrowIfCancellationRequested();
            var lease = store.TryClaim(key, clock.GetUtcNow());
            if (lease is null) continue;
            await DeliverAsync(lease, ct);
        }
    }

    private async Task DeliverAsync(SubmissionConfirmationLease lease, CancellationToken ct)
    {
        var receipt = lease.Receipt;
        var dispatchStarted = receipt.State == "Sending";
        try
        {
            if (dispatchStarted)
            {
                // SMTP cannot provide exactly-once delivery. A crash or lost DATA acknowledgement
                // may hide acceptance. Retain Uncertain for operator reconciliation; never replay
                // automatically, even if the crash happened just before transmission. Message-ID
                // is stable for investigation, not a promise of recipient-side deduplication.
                SaveTerminal("Uncertain", "InterruptedDispatch");
                return;
            }
            if (receipt.Attempts >= SubmissionConfirmationStore.MaximumAttempts || receipt.ExpiresAtUtc <= clock.GetUtcNow())
            {
                SaveTerminal("Exhausted", "RetryLimit");
                return;
            }

            receipt = receipt with { Attempts = receipt.Attempts + 1 };
            store.Save(lease, receipt, clock.GetUtcNow(), release: false);
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(receipt.Message!));
            using var message = MimeMessage.Load(data);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PublicRequestTimeoutPolicies.ApplicationExternalDependencyTimeout);
            await sender.SendAsync(config, message, () =>
            {
                // An expired/reassigned lease must fail before SMTP can transmit anything.
                receipt = receipt with { State = "Sending" };
                store.Save(lease, receipt, clock.GetUtcNow(), release: false);
                dispatchStarted = true;
            }, timeout.Token);
            store.Save(lease, receipt with { State = "Sent", SentAtUtc = clock.GetUtcNow(), LastError = null }, clock.GetUtcNow());
            logger.LogInformation("Submission confirmation accepted. ReceiptKey={ReceiptKey}", receipt.SubmissionKey);
        }
        catch (Exception ex)
        {
            // Never log SMTP exception messages: they can contain addresses or message content.
            if (dispatchStarted && ex is not SubmissionConfirmationRejectedException)
            {
                SaveTerminal("Uncertain", ex.GetType().Name);
                return;
            }
            var state = receipt.Attempts >= SubmissionConfirmationStore.MaximumAttempts ? "Exhausted" : "Pending";
            receipt = receipt with
            {
                State = state, LastError = ex.GetType().Name,
                NextAttemptUtc = clock.GetUtcNow().AddMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, receipt.Attempts - 1))))
            };
            // Save without the canceled request/host token. Shutdown before dispatch remains retryable.
            store.Save(lease, receipt, clock.GetUtcNow());
            logger.LogWarning("Submission confirmation deferred. ReceiptKey={ReceiptKey} State={State} Attempts={Attempts} ExceptionType={ExceptionType}",
                receipt.SubmissionKey, state, receipt.Attempts, ex.GetType().Name);
        }

        void SaveTerminal(string state, string reason)
        {
            store.Save(lease, receipt with { State = state, LastError = reason }, clock.GetUtcNow());
            logger.LogError("Submission confirmation needs reconciliation. ReceiptKey={ReceiptKey} State={State} Reason={Reason}",
                receipt.SubmissionKey, state, reason);
        }
    }
}

public sealed class SubmissionConfirmationWorker(SubmissionConfirmationDispatcher dispatcher,
    ILogger<SubmissionConfirmationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Durable submission confirmation worker started.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            do
            {
                try { await dispatcher.RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError("Submission confirmation scan failed. ExceptionType={ExceptionType}", ex.GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
