namespace LongevityWorldCup.Website.Business;

// Keep the existing invoice contract so saved submissions and BTCPay invoices remain readable.
// Provider selection affects new checkouts only; existing payments retain their original provider.
public sealed class ApplicationCheckoutClient(
    BtcpayInvoiceClient btcpay,
    StripeCheckoutClient stripe,
    ApplicationPaymentStore payments) : IBtcpayInvoiceClient
{
    public Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
        => CheckoutConfiguration.UseStripe(config)
            ? stripe.CreateInvoiceAsync(config, request, ct)
            : btcpay.CreateInvoiceAsync(config, request, ct);

    public Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default)
        => invoiceId.StartsWith("stripe_", StringComparison.Ordinal)
            ? stripe.GetInvoiceAsync(config, invoiceId, ct)
            : btcpay.GetInvoiceAsync(config, invoiceId, ct);

    public Task<BtcpayInvoiceRecoveryResult> FindInvoiceByOrderIdAsync(Config config, string orderId, CancellationToken ct = default)
        => payments.GetByOrder(orderId)?.Provider == "Stripe"
            ? stripe.FindInvoiceByOrderIdAsync(config, orderId, ct)
            : btcpay.FindInvoiceByOrderIdAsync(config, orderId, ct);
}

internal static class CheckoutConfiguration
{
    internal const string StripeBaseUrl = "https://api.stripe.com";

    internal static bool UseStripe(Config config)
        => string.Equals(Setting("LWC_PAYMENT_PROVIDER", config.ApplicationPaymentProvider), "Stripe", StringComparison.OrdinalIgnoreCase);

    internal static string AccountId(Config config) => Setting("LWC_STRIPE_ACCOUNT_ID", config.StripeAccountId);
    internal static string WebhookSecret(Config config) => Setting("LWC_STRIPE_WEBHOOK_SECRET", config.StripeWebhookSecret);
    internal static bool LiveMode(Config config)
        => bool.TryParse(Environment.GetEnvironmentVariable("LWC_STRIPE_LIVE_MODE"), out var live) ? live : config.StripeLiveMode;

    private static string Setting(string name, string? fallback)
        => (Environment.GetEnvironmentVariable(name) ?? fallback ?? "").Trim();

    internal static string? ValidateNewCheckout(Config config)
    {
        var provider = Setting("LWC_PAYMENT_PROVIDER", config.ApplicationPaymentProvider);
        if (string.Equals(provider, "Stripe", StringComparison.OrdinalIgnoreCase))
            return ValidateStripe(config, requireWebhook: true);
        if (!string.Equals(provider, "BTCPay", StringComparison.OrdinalIgnoreCase))
            return "Unknown application payment provider.";
        if (string.IsNullOrWhiteSpace(config.BTCPayBaseUrl)) return "BTCPayBaseUrl is missing in config.";
        if (string.IsNullOrWhiteSpace(config.BTCPayStoreId)) return "BTCPayStoreId is missing in config.";
        if (string.IsNullOrWhiteSpace(config.BTCPayGreenfieldApiKey)) return "BTCPayGreenfieldApiKey is missing in config.";
        return null;
    }

    internal static string? ValidateStripe(Config config, bool requireWebhook = false)
    {
        if (!AccountId(config).StartsWith("acct_", StringComparison.Ordinal)) return "Stripe account is not configured.";
        if (requireWebhook && !WebhookSecret(config).StartsWith("whsec_", StringComparison.Ordinal))
            return "Stripe webhook is not configured.";
        return null;
    }

    internal static StripeCheckoutLink? FindLink(Config config, BtcpayInvoiceCreateRequest request)
    {
        var file = Environment.GetEnvironmentVariable("LWC_STRIPE_CHECKOUT_LINKS_FILE");
        var links = string.IsNullOrWhiteSpace(file) ? config.StripeCheckoutLinks
            : System.Text.Json.JsonSerializer.Deserialize<List<StripeCheckoutLink>>(File.ReadAllText(file));
        var matches = links?.Where(link => link.AmountUsd == request.Amount
            && link.OfferType == request.Metadata.GetValueOrDefault("offerType")?.ToString()
            && link.SubmissionType == request.Metadata.GetValueOrDefault("submissionType")?.ToString()).ToArray();
        return matches?.Length == 1 && matches[0].Id.StartsWith("plink_", StringComparison.Ordinal)
            && StripeCheckoutClient.IsHostedCheckoutUrl(matches[0].Url) ? matches[0] : null;
    }

    internal static bool Matches(Config config, ApplicationPayment payment)
        => payment.Provider == "Stripe"
            ? payment.ProviderBaseUrl == StripeBaseUrl && payment.StoreId == AccountId(config)
                && payment.LiveMode == LiveMode(config)
            : payment.Provider == "BTCPay"
                && string.Equals(payment.ProviderBaseUrl, config.BTCPayBaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                && payment.StoreId == config.BTCPayStoreId;
}
