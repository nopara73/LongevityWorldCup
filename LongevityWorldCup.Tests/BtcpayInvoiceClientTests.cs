using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BtcpayInvoiceClientTests
{
    [Fact]
    public async Task CreateInvoiceAsync_SendsCanonicalPayloadAndAuthorizationHeader()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "id": "invoice-123",
                  "checkoutLink": "https://btcpay.example.test/i/invoice-123"
                }
                """)
        });
        var client = new BtcpayInvoiceClient(new RecordingHttpClientFactory(handler));
        var config = new Config
        {
            BTCPayBaseUrl = "https://btcpay.example.test/",
            BTCPayStoreId = "store id",
            BTCPayGreenfieldApiKey = "secret-token"
        };

        var result = await client.CreateInvoiceAsync(
            config,
            new BtcpayInvoiceCreateRequest(
                25.5m,
                " usd ",
                "order-1",
                "buyer@example.test",
                "Buyer Name",
                new Dictionary<string, object?> { ["athleteSlug"] = "alice" }));

        Assert.True(result.Success);
        Assert.Equal("invoice-123", result.InvoiceId);
        Assert.Equal("https://btcpay.example.test/i/invoice-123", result.CheckoutLink);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://btcpay.example.test/api/v1/stores/store%20id/invoices", request.RequestUri?.AbsoluteUri);
        Assert.Equal("token", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);

        using var payload = JsonDocument.Parse(request.Body);
        var root = payload.RootElement;
        Assert.Equal(25.5m, root.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", root.GetProperty("currency").GetString());
        Assert.Equal("BTC", root.GetProperty("checkout").GetProperty("paymentMethods")[0].GetString());
        Assert.Equal("HighSpeed", root.GetProperty("checkout").GetProperty("speedPolicy").GetString());
        Assert.Equal("buyer@example.test", root.GetProperty("buyer").GetProperty("email").GetString());

        var metadata = root.GetProperty("metadata");
        Assert.Equal("alice", metadata.GetProperty("athleteSlug").GetString());
        Assert.Equal("order-1", metadata.GetProperty("orderId").GetString());
        Assert.Equal("Buyer Name", metadata.GetProperty("buyerName").GetString());
        Assert.Equal("buyer@example.test", metadata.GetProperty("buyerEmail").GetString());
    }

    [Fact]
    public async Task GetInvoiceAsync_UsesEscapedTrimmedInvoiceIdAndParsesResult()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "status": "Processing",
                  "additionalStatus": "PaidPartial",
                  "amount": "25.50",
                  "currency": "USD",
                  "paidAmount": "1.25",
                  "checkoutLink": "https://btcpay.example.test/i/invoice-1",
                  "buyer": { "email": "buyer@example.test" },
                  "metadata": { "athleteName": "Alice Athlete", "submissionType": "result" }
                }
                """)
        });
        var client = new BtcpayInvoiceClient(new RecordingHttpClientFactory(handler));
        var config = new Config
        {
            BTCPayBaseUrl = "https://btcpay.example.test/",
            BTCPayStoreId = "store",
            BTCPayGreenfieldApiKey = "secret-token"
        };

        var result = await client.GetInvoiceAsync(config, " invoice/1 ");

        Assert.True(result.Success);
        Assert.True(result.IsPaid);
        Assert.Equal("Processing", result.Status);
        Assert.Equal("PaidPartial", result.AdditionalStatus);
        Assert.Equal(25.50m, result.Amount);
        Assert.Equal(1.25m, result.PaidAmount);
        Assert.Equal("buyer@example.test", result.BuyerEmail);
        Assert.Equal("Alice Athlete", result.AthleteNameFromMetadata);
        Assert.Equal("result", result.SubmissionTypeFromMetadata);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://btcpay.example.test/api/v1/stores/store/invoices/invoice%2F1", request.RequestUri?.AbsoluteUri);
        Assert.Equal("token", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);
    }

    [Fact]
    public async Task BtcpayFailuresDoNotExposeRawResponseBodies()
    {
        var client = new BtcpayInvoiceClient(new StaticHttpClientFactory(
            HttpStatusCode.BadRequest,
            "{\"error\":\"secret-store-detail\",\"buyerEmail\":\"user@example.test\"}"));
        var config = new Config
        {
            BTCPayBaseUrl = "https://btcpay.example.test",
            BTCPayStoreId = "store",
            BTCPayGreenfieldApiKey = "secret"
        };

        var create = await client.CreateInvoiceAsync(
            config,
            new BtcpayInvoiceCreateRequest(25m, "USD", "order-1", "user@example.test", "User", new Dictionary<string, object?>()));
        var lookup = await client.GetInvoiceAsync(config, "invoice-1");

        Assert.False(create.Success);
        Assert.False(lookup.Success);
        Assert.Equal("BTCPay API returned HTTP 400.", create.Error);
        Assert.Equal("BTCPay API returned HTTP 400.", lookup.Error);
        Assert.DoesNotContain("secret-store-detail", create.Error);
        Assert.DoesNotContain("user@example.test", lookup.Error);
    }

    [Fact]
    public void ParseInvoiceJson_TreatsSettledInvoiceAsPaidWithoutPaidAmount()
    {
        var result = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Settled",
              "additionalStatus": "None",
              "amount": "25.50",
              "currency": "USD",
              "checkoutLink": "https://btcpay.example.test/i/invoice-1"
            }
            """);

        Assert.True(result.Success);
        Assert.True(result.IsPaid);
        Assert.Equal("Settled", result.Status);
        Assert.Null(result.PaidAmount);
    }

    [Fact]
    public void ParseInvoiceJson_ParsesNumericAndCaseInsensitiveProviderFields()
    {
        var result = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "STATUS": "Processing",
              "ADDITIONALSTATUS": "PaidPartial",
              "AMOUNT": 25.50,
              "CURRENCY": "usd",
              "PAIDAMOUNT": 1.25,
              "CHECKOUTLINK": "https://btcpay.example.test/i/invoice-1",
              "BUYER": { "EMAIL": "buyer@example.test" },
              "METADATA": { "ATHLETENAME": "Alice Athlete", "SUBMISSIONTYPE": "edit" }
            }
            """);

        Assert.True(result.Success);
        Assert.True(result.IsPaid);
        Assert.Equal("Processing", result.Status);
        Assert.Equal("PaidPartial", result.AdditionalStatus);
        Assert.Equal(25.50m, result.Amount);
        Assert.Equal(1.25m, result.PaidAmount);
        Assert.Equal("usd", result.Currency);
        Assert.Equal("https://btcpay.example.test/i/invoice-1", result.CheckoutLink);
        Assert.Equal("buyer@example.test", result.BuyerEmail);
        Assert.Equal("Alice Athlete", result.AthleteNameFromMetadata);
        Assert.Equal("edit", result.SubmissionTypeFromMetadata);
    }

    [Fact]
    public void ParseInvoiceJson_UsesMetadataBuyerEmailWhenBuyerEmailIsMissing()
    {
        var result = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Settled",
              "amount": "80",
              "currency": "USD",
              "metadata": { "buyerEmail": "applicant@example.test" }
            }
            """);

        Assert.True(result.Success);
        Assert.True(result.IsPaid);
        Assert.Equal("applicant@example.test", result.BuyerEmail);
    }

    private sealed class StaticHttpClientFactory(HttpStatusCode statusCode, string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StaticHandler(statusCode, body));
    }

    private sealed class StaticHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
    }

    private sealed class RecordingHttpClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                body));
            return responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
