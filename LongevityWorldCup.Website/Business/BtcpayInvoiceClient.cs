using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public interface IBtcpayInvoiceClient
{
    Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default);
    Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default);
}

public sealed class BtcpayInvoiceClient(IHttpClientFactory httpClientFactory) : IBtcpayInvoiceClient
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    public async Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.BTCPayBaseUrl))
            return BtcpayInvoiceCreateResult.Failure("BTCPayBaseUrl is missing in config.");
        if (string.IsNullOrWhiteSpace(config.BTCPayStoreId))
            return BtcpayInvoiceCreateResult.Failure("BTCPayStoreId is missing in config.");
        if (string.IsNullOrWhiteSpace(config.BTCPayGreenfieldApiKey))
            return BtcpayInvoiceCreateResult.Failure("BTCPayGreenfieldApiKey is missing in config.");
        if (request.Amount <= 0)
            return BtcpayInvoiceCreateResult.Failure("Invoice amount must be positive.");
        if (string.IsNullOrWhiteSpace(request.Currency))
            return BtcpayInvoiceCreateResult.Failure("Invoice currency is required.");

        var baseUrl = config.BTCPayBaseUrl!.TrimEnd('/');
        var endpoint = $"{baseUrl}/api/v1/stores/{Uri.EscapeDataString(config.BTCPayStoreId!)}/invoices";
        var client = _httpClientFactory.CreateClient(nameof(BtcpayInvoiceClient));
        var metadata = new Dictionary<string, object?>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"] = request.OrderId,
            ["buyerName"] = request.BuyerName,
            ["buyerEmail"] = request.BuyerEmail
        };
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = request.Amount,
            ["currency"] = request.Currency.Trim().ToUpperInvariant(),
            ["metadata"] = metadata,
            ["checkout"] = new Dictionary<string, object?>
            {
                ["speedPolicy"] = "HighSpeed",
                ["paymentMethods"] = new[] { "BTC" }
            }
        };

        if (!string.IsNullOrWhiteSpace(request.BuyerEmail))
        {
            payload["buyer"] = new Dictionary<string, object?>
            {
                ["email"] = request.BuyerEmail!.Trim()
            };
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("token", config.BTCPayGreenfieldApiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return BtcpayInvoiceCreateResult.Failure($"BTCPay API {(int)response.StatusCode}: {responseBody}");

        using var json = JsonDocument.Parse(responseBody);
        if (!TryGetPropertyString(json.RootElement, "checkoutLink", out var checkoutLink) || string.IsNullOrWhiteSpace(checkoutLink))
            return BtcpayInvoiceCreateResult.Failure("BTCPay response missing checkoutLink.");
        if (!TryGetPropertyString(json.RootElement, "id", out var invoiceId) || string.IsNullOrWhiteSpace(invoiceId))
            return BtcpayInvoiceCreateResult.Failure("BTCPay response missing invoice id.");

        return new BtcpayInvoiceCreateResult(true, checkoutLink, invoiceId, null);
    }

    public async Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.BTCPayBaseUrl))
            return BtcpayInvoiceLookupResult.Failure("BTCPayBaseUrl is missing in config.");
        if (string.IsNullOrWhiteSpace(config.BTCPayStoreId))
            return BtcpayInvoiceLookupResult.Failure("BTCPayStoreId is missing in config.");
        if (string.IsNullOrWhiteSpace(config.BTCPayGreenfieldApiKey))
            return BtcpayInvoiceLookupResult.Failure("BTCPayGreenfieldApiKey is missing in config.");
        if (string.IsNullOrWhiteSpace(invoiceId))
            return BtcpayInvoiceLookupResult.Failure("invoiceId is required.");

        var baseUrl = config.BTCPayBaseUrl!.TrimEnd('/');
        var endpoint = $"{baseUrl}/api/v1/stores/{Uri.EscapeDataString(config.BTCPayStoreId!)}/invoices/{Uri.EscapeDataString(invoiceId.Trim())}";
        var client = _httpClientFactory.CreateClient(nameof(BtcpayInvoiceClient));

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", config.BTCPayGreenfieldApiKey);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return BtcpayInvoiceLookupResult.Failure($"BTCPay API {(int)response.StatusCode}: {responseBody}");
        }

        return ParseInvoiceJson(responseBody);
    }

    internal static BtcpayInvoiceLookupResult ParseInvoiceJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        TryGetPropertyString(document.RootElement, "status", out var status);
        TryGetPropertyString(document.RootElement, "additionalStatus", out var additionalStatus);
        TryGetPropertyString(document.RootElement, "amount", out var amountText);
        TryGetPropertyString(document.RootElement, "currency", out var currency);
        TryGetPropertyString(document.RootElement, "paidAmount", out var paidAmountText);
        TryGetPropertyString(document.RootElement, "checkoutLink", out var checkoutLink);
        TryGetNestedPropertyString(document.RootElement, "buyer", "email", out var buyerEmail);
        TryGetNestedPropertyString(document.RootElement, "metadata", "athleteName", out var athleteNameFromMetadata);

        var amount = ParseDecimal(amountText);
        var paidAmount = ParseDecimal(paidAmountText);
        var isPaid = string.Equals(status, "Settled", StringComparison.OrdinalIgnoreCase)
            || paidAmount.GetValueOrDefault() > 0m;

        return new BtcpayInvoiceLookupResult(
            Success: true,
            IsPaid: isPaid,
            Status: status,
            AdditionalStatus: additionalStatus,
            AmountText: amountText,
            Currency: currency,
            PaidAmountText: paidAmountText,
            Amount: amount,
            PaidAmount: paidAmount,
            CheckoutLink: checkoutLink,
            BuyerEmail: buyerEmail,
            AthleteNameFromMetadata: athleteNameFromMetadata,
            Error: null);
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool TryGetPropertyString(JsonElement element, string propertyName, out string? value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetNestedPropertyString(JsonElement element, string parentPropertyName, string childPropertyName, out string? value)
    {
        value = null;
        if (!TryGetPropertyElement(element, parentPropertyName, out var parentElement))
            return false;
        if (parentElement.ValueKind != JsonValueKind.Object)
            return false;
        return TryGetPropertyString(parentElement, childPropertyName, out value);
    }

    private static bool TryGetPropertyElement(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

public sealed record BtcpayInvoiceCreateRequest(
    decimal Amount,
    string Currency,
    string OrderId,
    string? BuyerEmail,
    string? BuyerName,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record BtcpayInvoiceCreateResult(
    bool Success,
    string? CheckoutLink,
    string? InvoiceId,
    string? Error)
{
    public static BtcpayInvoiceCreateResult Failure(string error) =>
        new(false, null, null, error);
}

public sealed record BtcpayInvoiceLookupResult(
    bool Success,
    bool IsPaid,
    string? Status,
    string? AdditionalStatus,
    string? AmountText,
    string? Currency,
    string? PaidAmountText,
    decimal? Amount,
    decimal? PaidAmount,
    string? CheckoutLink,
    string? BuyerEmail,
    string? AthleteNameFromMetadata,
    string? Error)
{
    public static BtcpayInvoiceLookupResult Failure(string error) =>
        new(false, false, null, null, null, null, null, null, null, null, null, null, error);
}
