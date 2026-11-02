using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Stripe;
using Stripe.Checkout;

namespace LongevityWorldCup.Website.Business;

public sealed record StripeCheckoutLink(string Id, string Url, decimal AmountUsd, string OfferType, string SubmissionType);

// Temporary hosted Payment Links use a pre-provisioned price catalogue and signed Stripe events.
// No Stripe API credential is needed on the website or on the offline BTCPay server.
public sealed class StripeCheckoutClient(ApplicationPaymentStore payments)
{
    internal const string IntegrationIdentifier = "lwc-temporary-checkout-rqmvtxpa";
    internal static string InvoiceId(string orderId) => "stripe_" + orderId;

    public Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (CheckoutConfiguration.ValidateStripe(config, requireWebhook: true) is { } error)
            return Task.FromResult(BtcpayInvoiceCreateResult.Failure(error));
        var payment = payments.GetByOrder(request.OrderId);
        if (payment is null || payment.Provider != "Stripe" || !CheckoutConfiguration.Matches(config, payment)
            || payment.StripePaymentLinkId is null || !IsHostedCheckoutUrl(payment.CheckoutLink)
            || payment.ExpectedAmount != request.Amount || request.Amount <= 0
            || !string.Equals(request.Currency, "USD", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(payment.ReturnUrl, UriKind.Absolute, out var returnUrl)
            || returnUrl.Scheme is not ("http" or "https"))
            return Task.FromResult(BtcpayInvoiceCreateResult.Failure("Stripe checkout has no matching configured price or saved order."));

        var checkout = returnUrl.GetLeftPart(UriPartial.Authority) + "/api/payments/stripe/checkout/" + Uri.EscapeDataString(payment.OrderId);
        return Task.FromResult(new BtcpayInvoiceCreateResult(true, checkout, InvoiceId(payment.OrderId), null));
    }

    public Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var payment = payments.GetByInvoice(invoiceId);
        if (payment is null || payment.Provider != "Stripe" || !CheckoutConfiguration.Matches(config, payment))
            return Task.FromResult(BtcpayInvoiceLookupResult.Failure("Stripe checkout has no matching application payment."));
        return Task.FromResult(payments.GetStripeReceipt(payment.OrderId) ?? Pending(payment));
    }

    public Task<BtcpayInvoiceRecoveryResult> FindInvoiceByOrderIdAsync(Config config, string orderId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var payment = payments.GetByOrder(orderId);
        return Task.FromResult(payment?.Provider == "Stripe" && CheckoutConfiguration.Matches(config, payment)
            && payment.StripePaymentLinkId is not null
            ? new BtcpayInvoiceRecoveryResult(true, InvoiceId(orderId), null)
            : new(false, null, "Stripe recovery requires a matching saved payment order."));
    }

    internal bool HandleWebhook(Config config, Event stripeEvent)
    {
        if (stripeEvent.Livemode != CheckoutConfiguration.LiveMode(config)) return false;
        if (stripeEvent.Type is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded"
            or "checkout.session.async_payment_failed" or "checkout.session.expired")) return true;
        if (stripeEvent.Data.Object is not Session session) return false;
        if (session.Metadata?.GetValueOrDefault("integration") != IntegrationIdentifier) return true;
        if (string.IsNullOrWhiteSpace(session.ClientReferenceId)) return false;
        var payment = payments.GetByOrder(session.ClientReferenceId);
        if (payment is null || !Matches(session, payment, config)) return false;
        // The controller has verified the signature and timestamp of this immutable event payload.
        // Completed can still mean unpaid for delayed methods. Never downgrade a previously paid receipt.
        if (session.Status == "complete" && session.PaymentStatus == "paid"
            && stripeEvent.Type is "checkout.session.completed" or "checkout.session.async_payment_succeeded")
        {
            payments.RecordStripeReceipt(payment.OrderId, session.Id, Paid(payment, session.Id), DateTimeOffset.UtcNow);
        }
        return true;
    }

    internal string? CheckoutDestination(Config config, string orderId)
    {
        var payment = payments.GetByOrder(orderId);
        if (payment?.Provider != "Stripe" || !CheckoutConfiguration.Matches(config, payment)
            || CheckoutConfiguration.ValidateStripe(config, requireWebhook: true) is not null) return null;
        if (payment.PaidInvoice?.IsPaid == true || payments.GetStripeReceipt(orderId)?.IsPaid == true)
            return payment.ReturnUrl;
        return IsHostedCheckoutUrl(payment.CheckoutLink) && payment.StripePaymentLinkId is not null
            ? QueryHelpers.AddQueryString(payment.CheckoutLink!, "client_reference_id", payment.OrderId) : null;
    }

    internal static bool Matches(Session session, ApplicationPayment payment, Config config)
        => payment.Provider == "Stripe" && CheckoutConfiguration.Matches(config, payment)
            && payment.ExpectedAmount > 0 && payment.Currency?.ToUpperInvariant() == "USD"
            && session.Id?.StartsWith(payment.LiveMode == true ? "cs_live_" : "cs_test_", StringComparison.Ordinal) == true
            && session.Mode == "payment" && session.Livemode == payment.LiveMode
            && session.ClientReferenceId == payment.OrderId
            && session.Metadata?.GetValueOrDefault("integration") == IntegrationIdentifier
            && !string.IsNullOrEmpty(payment.StripePaymentLinkId) && session.PaymentLinkId == payment.StripePaymentLinkId
            && session.Currency == "usd" && session.AmountTotal == payment.ExpectedAmount * 100m;

    private static BtcpayInvoiceLookupResult Pending(ApplicationPayment payment)
        => new(true, false, "New", "unpaid", payment.ExpectedAmount?.ToString(CultureInfo.InvariantCulture),
            payment.Currency, "0", payment.ExpectedAmount, 0m, payment.CheckoutLink,
            payment.AccountEmail, payment.ApplicantName, payment.SubmissionType, null);

    private static BtcpayInvoiceLookupResult Paid(ApplicationPayment payment, string sessionId)
        => Pending(payment) with { IsPaid = true, Status = "Settled", AdditionalStatus = sessionId,
            PaidAmount = payment.ExpectedAmount, PaidAmountText = payment.ExpectedAmount?.ToString(CultureInfo.InvariantCulture) };

    internal static bool IsHostedCheckoutUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && uri.Host == "buy.stripe.com" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath.Length > 1;
}
