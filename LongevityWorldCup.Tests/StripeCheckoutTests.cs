using System.Security.Cryptography;
using System.Text;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Stripe;
using Stripe.Checkout;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class StripeCheckoutTests
{
    [Fact]
    public async Task Checkout_PreservesPriceAndOrderAndStopsOfferingPaymentAfterReceipt()
    {
        using var f = new Fixture();
        var created = await f.Checkout.CreateInvoiceAsync(f.Config, f.Request);
        Assert.True(created.Success);
        Assert.Equal("https://longevityworldcup.com/api/payments/stripe/checkout/order-1", created.CheckoutLink);
        Assert.Equal("stripe_order-1", created.InvoiceId);
        f.Store.AttachInvoice(f.Request.OrderId, created.InvoiceId!);
        Assert.Equal("https://buy.stripe.com/test_fixture?client_reference_id=order-1",
            f.Checkout.CheckoutDestination(f.Config, "order-1"));
        Assert.Null(f.Checkout.CheckoutDestination(f.Config, "unknown"));
        Assert.True(f.Checkout.HandleWebhook(f.Config, f.Event("checkout.session.completed", paid: true)));
        Assert.Equal(f.Request.RedirectUrl, f.Checkout.CheckoutDestination(f.Config, "order-1"));
        var invoice = await f.Checkout.GetInvoiceAsync(f.Config, created.InvoiceId!);
        Assert.True(invoice.IsPaid);
        Assert.Equal(80m, invoice.PaidAmount);
        Assert.Equal("athlete@example.test", invoice.BuyerEmail);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("link")]
    [InlineData("order")]
    [InlineData("mode")]
    [InlineData("account")]
    public void Webhook_RejectsPaymentForDifferentPriceOrderAccountOrMode(string mismatch)
    {
        using var f = new Fixture();
        var ev = f.Event("checkout.session.completed", paid: true);
        var session = (Session)ev.Data.Object;
        switch (mismatch)
        {
            case "amount": session.AmountTotal = 1000; break;
            case "currency": session.Currency = "eur"; break;
            case "link": session.PaymentLinkId = "plink_other"; break;
            case "order": session.ClientReferenceId = "other-order"; break;
            case "mode": ev.Livemode = true; session.Livemode = true; break;
            case "account": f.Config.StripeAccountId = "acct_other"; break;
        }
        Assert.False(f.Checkout.HandleWebhook(f.Config, ev));
        Assert.Null(f.Store.GetStripeReceipt("order-1"));
    }

    [Fact]
    public async Task DelayedAndRepeatedEvents_FulfillOnceAcrossRestartWithoutBrowserOrProviderApi()
    {
        using var f = new Fixture();
        f.Store.AttachInvoice("order-1", "stripe_order-1");
        Assert.True(f.Checkout.HandleWebhook(f.Config, f.Event("checkout.session.completed", paid: false)));
        await f.Reconciler().RunOnceAsync();
        Assert.Empty(f.Mail.Sent);
        Assert.Null(f.Store.GetStripeReceipt("order-1"));

        Assert.True(f.Checkout.HandleWebhook(f.Config, f.Event("checkout.session.async_payment_succeeded", paid: true)));
        Assert.True(f.Checkout.HandleWebhook(f.Config, f.Event("checkout.session.async_payment_failed", paid: false)));
        f.Restart();
        // A later provider switch must not reroute an already-created Stripe checkout.
        f.Config.ApplicationPaymentProvider = "BTCPay";
        await f.Reconciler().RunOnceAsync();
        Assert.Single(f.Mail.Sent);
        Assert.Equal("Sent", f.Store.GetByInvoice("stripe_order-1")!.State);
        Assert.True(f.Checkout.HandleWebhook(f.Config, f.Event("checkout.session.async_payment_succeeded", paid: true)));
        await f.Reconciler().RunOnceAsync();
        Assert.Single(f.Mail.Sent);
    }

    [Fact]
    public async Task LostCheckoutResponse_RecoversOriginalOrderAndSignedReceiptSurvivesConcurrentPoll()
    {
        using var f = new Fixture();
        var recovered = await f.Checkout.FindInvoiceByOrderIdAsync(f.Config, "order-1");
        Assert.True(recovered.Success);
        Assert.Equal("stripe_order-1", recovered.InvoiceId);
        var lease = f.Store.TryClaim("order-1", DateTimeOffset.UtcNow)!;
        Assert.NotNull(lease);
        Assert.True(f.Checkout.HandleWebhook(f.Config, f.Event("checkout.session.completed", paid: true)));
        f.Store.Save(lease, lease.Payment with { NextAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }, DateTimeOffset.UtcNow);
        Assert.NotNull(f.Store.GetStripeReceipt("order-1"));
        await f.Reconciler().RunOnceAsync();
        Assert.Single(f.Mail.Sent);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Endpoint_RequiresValidFreshSignatureBeforeSavingReceipt(bool validSignature, bool stale, bool accepted)
    {
        using var f = new Fixture();
        var json = JsonConvert.SerializeObject(f.Event("checkout.session.completed", paid: true));
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(stale ? -10 : 0).ToUnixTimeSeconds();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(validSignature ? f.Config.StripeWebhookSecret! : "wrong-secret"),
            Encoding.UTF8.GetBytes($"{timestamp}.{json}")));
        var controller = new StripeWebhookController(f.Config, f.Checkout, NullLogger<StripeWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        controller.Request.Headers["Stripe-Signature"] = $"t={timestamp},v1={signature}";
        var result = await controller.Receive(CancellationToken.None);
        Assert.Equal(accepted, result is OkResult);
        Assert.Equal(accepted, f.Store.GetStripeReceipt("order-1") is not null);
    }

    [Fact]
    public async Task UnconfiguredPrice_FailsClosedAndUnrelatedWebhookCannotFulfill()
    {
        using var f = new Fixture(amount: 25m);
        var created = await f.Checkout.CreateInvoiceAsync(f.Config, f.Request);
        Assert.False(created.Success);
        Assert.Null(created.CheckoutLink);
        var ev = f.Event("checkout.session.completed", true);
        ((Session)ev.Data.Object).Metadata["integration"] = "different-integration";
        Assert.True(f.Checkout.HandleWebhook(f.Config, ev));
        Assert.Null(f.Store.GetStripeReceipt("order-1"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "LwcStripeTests", Guid.NewGuid().ToString("N"));
        public Config Config { get; } = new()
        {
            ApplicationPaymentProvider = "Stripe", StripeAccountId = "acct_fixture", StripeLiveMode = false,
            StripeWebhookSecret = "whsec_fixture_only",
            StripeCheckoutLinks = [new("plink_fixture", "https://buy.stripe.com/test_fixture", 80m, "pro", "result")],
            BTCPayBaseUrl = "https://pay.example.test", BTCPayStoreId = "store", BTCPayGreenfieldApiKey = "fixture"
        };
        public DatabaseManager Database { get; private set; }
        public ApplicationPaymentStore Store { get; private set; }
        public StripeCheckoutClient Checkout { get; private set; }
        public BtcpayInvoiceCreateRequest Request { get; }
        public FakeMail Mail { get; } = new();

        public Fixture(decimal amount = 80m)
        {
            Database = new DatabaseManager(dbPath: Path.Combine(_root, "payments.db"));
            Store = new ApplicationPaymentStore(Database);
            Checkout = new StripeCheckoutClient(Store);
            Request = new(amount, "USD", "order-1", "athlete@example.test", "Fixture Athlete",
                new Dictionary<string, object?> { ["offerType"] = "pro", ["submissionType"] = "result" },
                "https://longevityworldcup.com/review?from=proof-upload");
            Store.Register(Config, Request, DateTimeOffset.UtcNow.AddMinutes(-2));
        }

        public Event Event(string type, bool paid) => new()
        {
            Id = "evt_fixture", Object = "event", ApiVersion = "2026-09-30.endive", Type = type, Livemode = false,
            Data = new EventData { Object = new Session
            {
                Id = "cs_test_fixture", Object = "checkout.session", Mode = "payment", Livemode = false,
                ClientReferenceId = "order-1", PaymentLinkId = "plink_fixture", AmountTotal = 8000, Currency = "usd",
                Status = "complete", PaymentStatus = paid ? "paid" : "unpaid",
                Metadata = new() { ["integration"] = StripeCheckoutClient.IntegrationIdentifier }
            } }
        };

        public ApplicationPaymentReconciler Reconciler() => new(Store,
            new ApplicationCheckoutClient(new BtcpayInvoiceClient(new NoNetwork()), Checkout, Store), Mail, Config,
            new TestEnvironment { ContentRootPath = _root }, NullLogger<ApplicationPaymentReconciler>.Instance, TimeProvider.System);

        public void Restart()
        {
            Database.Dispose();
            Database = new DatabaseManager(dbPath: Path.Combine(_root, "payments.db"));
            Store = new ApplicationPaymentStore(Database);
            Checkout = new StripeCheckoutClient(Store);
        }

        public void Dispose() { Database.Dispose(); Directory.Delete(_root, recursive: true); }
    }

    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Stripe fallback must not call BTCPay or require an API key.");
    }

    private sealed class FakeMail : IApplicationPaymentEmailSender
    {
        public List<ApplicationPayment> Sent { get; } = [];
        public Task SendAsync(Config config, ApplicationPayment payment, Action beforeSend, CancellationToken ct)
        { beforeSend(); Sent.Add(payment); return Task.CompletedTask; }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
