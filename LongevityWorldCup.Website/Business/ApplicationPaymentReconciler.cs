using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class ApplicationPaymentReconciler(
    ApplicationPaymentStore store,
    IBtcpayInvoiceClient invoices,
    IApplicationPaymentEmailSender email,
    Config config,
    IWebHostEnvironment environment,
    ILogger<ApplicationPaymentReconciler> logger,
    TimeProvider timeProvider,
    DiscountSignupReportService? discounts = null)
{
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        foreach (var order in store.GetDueOrders(timeProvider.GetUtcNow()))
        {
            ct.ThrowIfCancellationRequested();
            var lease = store.TryClaim(order, timeProvider.GetUtcNow());
            if (lease is null) continue;
            await ReconcileAsync(lease, ct);
        }
    }

    private async Task ReconcileAsync(ApplicationPaymentLease lease, CancellationToken ct)
    {
        var payment = lease.Payment;
        var dispatchStarted = payment.State == "Sending";
        try
        {
            if (dispatchStarted)
            {
                // A previous process may have reached SMTP. Replaying here cannot guarantee no duplicate.
                MarkUncertain("The process stopped during SMTP delivery; check the message ID before retrying.");
                return;
            }

            if (!string.Equals(payment.ProviderBaseUrl, config.BTCPayBaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                || payment.StoreId != config.BTCPayStoreId)
                throw new InvalidOperationException("BTCPay configuration no longer matches this payment's store.");

            if (payment.InvoiceId is null)
            {
                using var lookupTimeout = DependencyTimeout(ct);
                var recovered = await invoices.FindInvoiceByOrderIdAsync(config, payment.OrderId, lookupTimeout.Token);
                if (!recovered.Success) throw new InvalidOperationException(recovered.Error);
                if (recovered.InvoiceId is null)
                {
                    ScheduleNextCheck();
                    return;
                }
                payment = payment with { InvoiceId = recovered.InvoiceId };
                store.Save(lease, payment, timeProvider.GetUtcNow(), release: false);
            }

            if (payment.PaidInvoice is null)
            {
                using var lookupTimeout = DependencyTimeout(ct);
                var invoice = await invoices.GetInvoiceAsync(config, payment.InvoiceId, lookupTimeout.Token);
                if (!invoice.Success) throw new InvalidOperationException(invoice.Error);
                if (invoice.IsPaid)
                {
                    payment = payment with { PaidInvoice = invoice, PaymentObservedAtUtc = timeProvider.GetUtcNow(), Failures = 0, LastError = null };
                    // Mail retries use this durable payment evidence, even while BTCPay is unavailable.
                    store.Save(lease, payment, timeProvider.GetUtcNow(), release: false);
                }

                if (discounts is not null)
                {
                    try { await discounts.UpdatePaymentStatusForInvoiceAsync(payment.InvoiceId, invoice, timeProvider.GetUtcNow(), ct); }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Could not update discount attribution for invoice {InvoiceId}", payment.InvoiceId);
                    }
                }
                if (!invoice.IsPaid)
                {
                    ScheduleNextCheck();
                    return;
                }
            }

            // Also suppress a notification sent by an older binary during a rollback.
            if (await ApplicationPaymentStore.WasLegacyNotificationSentAsync(environment.ContentRootPath, payment.InvoiceId, ct))
            {
                MarkSent();
                return;
            }

            using var sendTimeout = DependencyTimeout(ct);
            await email.SendAsync(config, payment, () =>
            {
                // Persist only after SMTP connects/authenticates, immediately before message transmission.
                payment = payment with { State = "Sending" };
                store.Save(lease, payment, timeProvider.GetUtcNow(), release: false);
                dispatchStarted = true;
            }, sendTimeout.Token);
            MarkSent();
        }
        catch (Exception ex)
        {
            if (dispatchStarted && ex is not PaymentEmailNotAcceptedException)
            {
                MarkUncertain("SMTP acceptance could not be recorded: " + ex.GetType().Name);
                return;
            }

            payment = payment with
            {
                State = "Pending",
                Failures = payment.Failures + 1,
                LastError = ex.GetType().Name,
                NextAttemptUtc = timeProvider.GetUtcNow().AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(payment.Failures, 6))))
            };
            // Persist retries even when shutdown cancelled a provider lookup or SMTP connection.
            store.Save(lease, payment, timeProvider.GetUtcNow());
            logger.LogWarning(ex, "Application payment {OrderId} will retry at {NextAttemptUtc}", payment.OrderId, payment.NextAttemptUtc);
        }

        void ScheduleNextCheck()
        {
            var now = timeProvider.GetUtcNow();
            var age = now - payment.CreatedAtUtc;
            // Keep observing expired/invalid/unpaid invoices for late/manual settlements, at lower frequency.
            var delay = age > TimeSpan.FromDays(25) ? TimeSpan.FromDays(1)
                : age > TimeSpan.FromDays(1) ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(1);
            store.Save(lease, payment with { NextAttemptUtc = now.Add(delay), Failures = 0, LastError = null }, now);
        }

        void MarkSent()
        {
            var now = timeProvider.GetUtcNow();
            store.Save(lease, payment with { State = "Sent", SentAtUtc = now, LastError = null }, now);
            logger.LogInformation("Application payment notification recorded for invoice {InvoiceId}", payment.InvoiceId);
        }

        void MarkUncertain(string reason)
        {
            store.Save(lease, payment with { State = "Uncertain", LastError = reason }, timeProvider.GetUtcNow());
            logger.LogError("Application payment notification needs reconciliation. InvoiceId={InvoiceId} MessageId={MessageId} Reason={Reason}",
                payment.InvoiceId, payment.MessageId, reason);
        }
    }

    private static CancellationTokenSource DependencyTimeout(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PublicRequestTimeoutPolicies.ApplicationExternalDependencyTimeout);
        return timeout;
    }
}

public sealed class ApplicationPaymentWorker(
    ApplicationPaymentReconciler reconciler,
    ILogger<ApplicationPaymentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try { await reconciler.RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Application payment reconciliation failed; durable work will be retried."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
