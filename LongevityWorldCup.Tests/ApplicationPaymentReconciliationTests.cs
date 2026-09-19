using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationPaymentReconciliationTests
{
    [Fact]
    public async Task EnabledHostedWorker_DeliversPersistedWorkOnStartupWithoutARequest()
    {
        using var fixture = new Fixture();
        fixture.Register();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Mail.Deliver = (payment, beforeSend, _) =>
        {
            beforeSend();
            fixture.Mail.Sent.Enqueue(payment);
            delivered.TrySetResult();
            return Task.CompletedTask;
        };
        await using var factory = new TestWebApplicationFactory(builder =>
        {
            builder.UseSetting("EnableApplicationPaymentReconciliation", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationPaymentStore>();
                services.AddSingleton(fixture.Store);
                services.RemoveAll<Config>();
                services.AddSingleton(fixture.Config);
                services.RemoveAll<IBtcpayInvoiceClient>();
                services.AddSingleton<IBtcpayInvoiceClient>(fixture.Provider);
                services.RemoveAll<IApplicationPaymentEmailSender>();
                services.AddSingleton<IApplicationPaymentEmailSender>(fixture.Mail);
            });
        });
        // Resolving Services starts the actual registered host; no browser or API request is made.
        _ = factory.Services;
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(fixture.Mail.Sent);
    }

    [Theory]
    [InlineData(false, "application", "Payment detected for submitted application.")]
    [InlineData(true, "result", "Payment detected for result upload.")]
    public async Task ClosedCheckoutBrowser_StillNotifiesForApplicationAndProUpgrade(bool result, string kind, string intro)
    {
        using var fixture = new Fixture();
        var created = await fixture.CreateThroughController(result);
        Assert.True(created.Success);
        var tracked = Assert.IsType<ApplicationPayment>(fixture.Store.GetByInvoice("invoice-1"));
        Assert.Equal(kind, tracked.SubmissionType);
        Assert.Equal("Applicant Ada", tracked.ApplicantName);
        Assert.Equal("athlete@example.test", tracked.AccountEmail);
        Assert.Equal("submission-1", tracked.SubmissionId);

        // No /review or payment-status request occurs, and a new process opens the same database.
        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await fixture.Reconciler().RunOnceAsync();
        var sent = Assert.Single(fixture.Mail.Sent);
        Assert.Equal("Sent", fixture.Store.GetByInvoice("invoice-1")!.State);
        using var message = SmtpApplicationPaymentEmailSender.BuildMessage(fixture.Config, sent);
        Assert.Equal("[LWC26] Application: Applicant Ada", message.Subject);
        Assert.StartsWith(intro, message.TextBody);
        Assert.Equal("reviewer@example.test", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("athlete@example.test", Assert.Single(message.ReplyTo.Mailboxes).Address);
        Assert.Equal(tracked.MessageId, message.MessageId);

        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromDays(30));
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
    }

    [Fact]
    public async Task LostCreateResponse_RecoversOnlyTheDurablyRegisteredOrderAfterRestart()
    {
        using var fixture = new Fixture();
        fixture.Provider.Create = _ => throw new TaskCanceledException("Response lost after invoice creation.");
        var created = await fixture.CreateThroughController();
        Assert.False(created.Success);
        var order = fixture.Provider.CreatedRequest!.OrderId;
        Assert.Null(fixture.Store.GetByOrder(order)!.InvoiceId);

        fixture.Restart();
        fixture.Provider.Recovery = recoveredOrder =>
        {
            Assert.Equal(order, recoveredOrder);
            return new(true, "invoice-1", null);
        };
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
        Assert.Equal("Sent", fixture.Store.GetByInvoice("invoice-1")!.State);
        Assert.Equal(1, fixture.Provider.CreateCalls);
    }

    [Fact]
    public void InvoiceAttachmentRacingRecovery_IsPreservedAndCannotBeRebound()
    {
        using var fixture = new Fixture();
        fixture.Store.Register(fixture.Config, Request("order-1"), fixture.Clock.GetUtcNow().AddMinutes(-2));
        var lease = fixture.Store.TryClaim("order-1", fixture.Clock.GetUtcNow())!;
        fixture.Store.AttachInvoice("order-1", "invoice-1");
        fixture.Store.Save(lease, lease.Payment, fixture.Clock.GetUtcNow());
        Assert.Equal("invoice-1", fixture.Store.GetByOrder("order-1")!.InvoiceId);
        Assert.Throws<InvalidOperationException>(() => fixture.Store.AttachInvoice("order-1", "different-invoice"));
    }

    [Fact]
    public void DurableWrites_RestoreSharedConnectionPolicyAfterSuccessAndFailure()
    {
        using var fixture = new Fixture();
        var before = Synchronous();
        fixture.Register();
        Assert.Equal(before, Synchronous());
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => fixture.Register());
        Assert.Equal(before, Synchronous());

        long Synchronous() => fixture.Database.Run(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = "PRAGMA synchronous;";
            return (long)command.ExecuteScalar()!;
        });
    }

    [Fact]
    public async Task ProviderAndMailFailures_RetryAcrossRestartsWithoutLosingPaidEvidence()
    {
        using var fixture = new Fixture();
        fixture.Register();
        fixture.Provider.Lookup = _ => BtcpayInvoiceLookupResult.Failure("provider temporarily unavailable");
        await fixture.Reconciler().RunOnceAsync();
        Assert.Empty(fixture.Mail.Sent);
        Assert.Equal(1, fixture.Store.GetByInvoice("invoice-1")!.Failures);
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal(1, fixture.Provider.LookupCalls); // Due time survives scans.

        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        fixture.Provider.Lookup = _ => Paid;
        fixture.Mail.Deliver = (_, _, _) => throw new IOException("SMTP connection unavailable, before transmission.");
        await fixture.Reconciler().RunOnceAsync();
        Assert.NotNull(fixture.Store.GetByInvoice("invoice-1")!.PaidInvoice);
        Assert.Equal("Pending", fixture.Store.GetByInvoice("invoice-1")!.State);

        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        fixture.Provider.Lookup = _ => throw new InvalidOperationException("Paid notification retry must not depend on BTCPay.");
        fixture.Mail.Deliver = null;
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
        Assert.Equal("Sent", fixture.Store.GetByInvoice("invoice-1")!.State);
    }

    [Fact]
    public async Task ConcurrentWorkersAndRepeatedScans_SendOnceAcrossSeparateDatabaseConnections()
    {
        using var fixture = new Fixture();
        fixture.Register();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Mail.Deliver = async (payment, beforeSend, ct) =>
        {
            beforeSend();
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            fixture.Mail.Sent.Enqueue(payment);
        };
        var first = fixture.Reconciler().RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var otherDatabase = new DatabaseManager(dbPath: fixture.DbPath);
        var otherStore = new ApplicationPaymentStore(otherDatabase);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.Reconciler(otherStore).RunOnceAsync()));
        Assert.Equal(1, fixture.Mail.Attempts);
        release.SetResult();
        await first;
        await fixture.Reconciler(otherStore).RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
    }

    [Fact]
    public async Task ExpiredLookupLease_IsRecoveredButCannotBeUsedByTheOldWorkerToDispatch()
    {
        using var fixture = new Fixture();
        fixture.Register();
        var lease = fixture.Store.TryClaim("order-1", fixture.Clock.GetUtcNow())!;
        fixture.Restart();
        fixture.Clock.Advance(ApplicationPaymentStore.LeaseDuration.Add(TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => fixture.Store.Save(lease,
            lease.Payment with { State = "Sending" }, fixture.Clock.GetUtcNow(), release: false));
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
    }

    [Fact]
    public async Task CrashDuringDispatch_IsFlaggedForReconciliationWithoutDuplicateMail()
    {
        using var fixture = new Fixture();
        fixture.Register();
        var lease = fixture.Store.TryClaim("order-1", fixture.Clock.GetUtcNow())!;
        fixture.Store.Save(lease, lease.Payment with { PaidInvoice = Paid, State = "Sending" }, fixture.Clock.GetUtcNow(), release: false);
        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal("Uncertain", fixture.Store.GetByInvoice("invoice-1")!.State);
        Assert.NotNull(fixture.Store.GetByInvoice("invoice-1")!.PaidInvoice);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal(0, fixture.Mail.Attempts);
    }

    [Theory]
    [InlineData(true, "Pending")]
    [InlineData(false, "Uncertain")]
    public async Task SmtpRejectionRetries_ButAmbiguousAcceptanceDoesNot(bool definiteRejection, string state)
    {
        using var fixture = new Fixture();
        fixture.Register();
        fixture.Mail.Deliver = (_, beforeSend, _) =>
        {
            beforeSend();
            if (definiteRejection) throw new PaymentEmailNotAcceptedException(new IOException("explicit rejection"));
            throw new IOException("connection lost while sending DATA");
        };
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal(state, fixture.Store.GetByInvoice("invoice-1")!.State);
        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        fixture.Mail.Deliver = null;
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal(definiteRejection ? 1 : 0, fixture.Mail.Sent.Count);
    }

    [Fact]
    public async Task ShutdownDuringLookup_LeavesADurableRetry()
    {
        using var fixture = new Fixture();
        fixture.Register();
        using var cancelled = new CancellationTokenSource();
        fixture.Provider.Lookup = _ =>
        {
            cancelled.Cancel();
            throw new OperationCanceledException(cancelled.Token);
        };
        await fixture.Reconciler().RunOnceAsync(cancelled.Token);
        Assert.Equal("Pending", fixture.Store.GetByInvoice("invoice-1")!.State);
        fixture.Restart();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        fixture.Provider.Lookup = _ => Paid;
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
    }

    [Theory]
    [InlineData("Settled", "0")]
    [InlineData("Processing", "0.01")]
    [InlineData("Expired", "0.01")]
    public async Task ExistingPaidPolicy_IncludingPartialPayments_IsPreserved(string status, string paid)
    {
        using var fixture = new Fixture();
        fixture.Register();
        fixture.Provider.Lookup = _ => BtcpayInvoiceClient.ParseInvoiceJson($$"""{"status":"{{status}}","additionalStatus":"PaidPartial","paidAmount":"{{paid}}"}""");
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
    }

    [Fact]
    public async Task ExpiredUnpaidInvoice_ContinuesToObserveLatePayment()
    {
        using var fixture = new Fixture();
        fixture.Register();
        fixture.Provider.Lookup = _ => BtcpayInvoiceClient.ParseInvoiceJson("""{"status":"Expired","paidAmount":"0"}""");
        fixture.Clock.Advance(TimeSpan.FromDays(30));
        await fixture.Reconciler().RunOnceAsync();
        Assert.Empty(fixture.Mail.Sent);
        Assert.Equal(fixture.Clock.GetUtcNow().AddDays(1), fixture.Store.GetByInvoice("invoice-1")!.NextAttemptUtc);
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        fixture.Provider.Lookup = _ => Paid;
        await fixture.Reconciler().RunOnceAsync();
        Assert.Single(fixture.Mail.Sent);
    }

    [Fact]
    public async Task LegacySentMarker_SuppressesDelivery_AndAnEmptyQueueDoesNotImportHistory()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Environment.ContentRootPath, "AppData"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Environment.ContentRootPath, "AppData", "paid-invoice-email-sent.txt"), "invoice-1\n");
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal(0, fixture.Provider.LookupCalls);
        fixture.Register();
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal("Sent", fixture.Store.GetByInvoice("invoice-1")!.State);
        Assert.Equal(0, fixture.Mail.Attempts);
    }

    [Fact]
    public async Task ChangedProviderConfiguration_DoesNotMatchInvoiceIdsInAnotherStore()
    {
        using var fixture = new Fixture();
        fixture.Register();
        fixture.Config.BTCPayStoreId = "other-store";
        await fixture.Reconciler().RunOnceAsync();
        Assert.Equal(0, fixture.Provider.LookupCalls);
        Assert.Equal(0, fixture.Mail.Attempts);
        Assert.Equal("Pending", fixture.Store.GetByInvoice("invoice-1")!.State);
    }

    [Fact]
    public async Task RepeatedConcurrentBrowserCallbacks_CannotSendOrQueueHistoricalMailOrSpoofIdentity()
    {
        var provider = new FakeProvider();
        var mail = new FakeMail();
        await using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBtcpayInvoiceClient>();
            services.AddSingleton<IBtcpayInvoiceClient>(provider);
            services.RemoveAll<IApplicationPaymentEmailSender>();
            services.AddSingleton<IApplicationPaymentEmailSender>(mail);
        }));
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ApplicationPaymentStore>();
        var config = new FixtureConfig();
        store.Register(config, Request("order-1"), DateTimeOffset.UtcNow);
        store.AttachInvoice("order-1", "invoice-1");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            var invoiceId = i % 2 == 0 ? "invoice-1" : "historical-paid";
            using var response = await client.PostAsJsonAsync("/api/application/payment-status", new PaymentStatusRequest
            {
                InvoiceId = invoiceId, AccountEmail = "spoof@example.test", ApplicantName = "Spoof", SubmissionType = "edit"
            });
            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(status.GetProperty("isPaid").GetBoolean());
            Assert.False(status.GetProperty("notificationSent").GetBoolean());
        }));
        Assert.Equal(0, mail.Attempts);
        Assert.Null(store.GetByInvoice("historical-paid"));
        Assert.Equal("Applicant Ada", store.GetByInvoice("invoice-1")!.ApplicantName);
        Assert.Equal("athlete@example.test", store.GetByInvoice("invoice-1")!.AccountEmail);

        // Retain the completion flag used by the unchanged page to clear local pending-payment state.
        var now = DateTimeOffset.UtcNow.AddMinutes(2);
        var lease = store.TryClaim("order-1", now)!;
        store.Save(lease, lease.Payment with { State = "Sent", PaidInvoice = Paid }, now);
        using var completed = await client.PostAsJsonAsync("/api/application/payment-status", new PaymentStatusRequest { InvoiceId = "invoice-1" });
        var completion = await completed.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(completion.GetProperty("alreadyNotified").GetBoolean());
        Assert.Equal(0, mail.Attempts);
    }

    private static BtcpayInvoiceLookupResult Paid => BtcpayInvoiceClient.ParseInvoiceJson(
        """{"status":"Settled","amount":"100","currency":"USD","paidAmount":"100","checkoutLink":"https://pay.example.test/i/invoice-1"}""");

    private static BtcpayInvoiceCreateRequest Request(string order) => new(100m, "USD", order, "athlete@example.test", "Applicant Ada",
        new Dictionary<string, object?> { ["submissionType"] = "application" });

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "LwcPaymentTests", Guid.NewGuid().ToString("N"));
        public string DbPath => Path.Combine(_root, "payments.db");
        public DatabaseManager Database { get; private set; }
        public ApplicationPaymentStore Store { get; private set; }
        public FakeProvider Provider { get; } = new();
        public FakeMail Mail { get; } = new();
        public ManualClock Clock { get; } = new();
        public Config Config { get; } = new FixtureConfig();
        public TestEnvironment Environment { get; }

        public Fixture()
        {
            Database = new DatabaseManager(dbPath: DbPath);
            Store = new ApplicationPaymentStore(Database);
            Environment = new TestEnvironment { ContentRootPath = _root };
        }

        public void Restart()
        {
            Database.Dispose();
            Database = new DatabaseManager(dbPath: DbPath);
            Store = new ApplicationPaymentStore(Database);
        }

        public void Register()
        {
            Store.Register(Config, Request("order-1"), Clock.GetUtcNow().AddMinutes(-2));
            Store.AttachInvoice("order-1", "invoice-1");
        }

        public ApplicationPaymentReconciler Reconciler(ApplicationPaymentStore? store = null) => new(store ?? Store, Provider, Mail, Config,
            Environment, NullLogger<ApplicationPaymentReconciler>.Instance, Clock);

        public async Task<(bool Success, string? CheckoutLink, string? InvoiceId, string? Error)> CreateThroughController(bool result = false)
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var retries = new ApplicationSubmissionRetryStore(cache, Path.Combine(_root, "responses"), Clock);
            var controller = new ApplicationController(Environment, NullLogger<ApplicationController>.Instance, retries, Store, Provider)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controller.Request.Scheme = "http";
            controller.Request.Host = new HostString("longevityworldcup.com");
            var method = typeof(ApplicationController).GetMethod("CreateBtcpayInvoiceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return await (Task<(bool, string?, string?, string?)>)method.Invoke(controller,
                [Config, new ApplicantData { Name = "Applicant Ada", SubmissionId = "submission-1" }, result ? 80m : 100m,
                    "athlete@example.test", result, false, CancellationToken.None])!;
        }

        public void Dispose()
        {
            Database.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixtureConfig : Config
    {
        public FixtureConfig()
        {
            BTCPayBaseUrl = "https://pay.example.test";
            BTCPayStoreId = "store";
            BTCPayGreenfieldApiKey = "fake-test-key";
            EmailFrom = "sender@example.test";
            EmailTo = "reviewer@example.test";
        }
    }

    private sealed class FakeProvider : IBtcpayInvoiceClient
    {
        public BtcpayInvoiceCreateRequest? CreatedRequest;
        public int CreateCalls;
        public int LookupCalls;
        public Func<BtcpayInvoiceCreateRequest, BtcpayInvoiceCreateResult>? Create;
        public Func<string, BtcpayInvoiceLookupResult> Lookup = _ => Paid;
        public Func<string, BtcpayInvoiceRecoveryResult> Recovery = _ => new(true, null, null);
        public Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CreateCalls);
            CreatedRequest = request;
            return Task.FromResult(Create?.Invoke(request) ?? new(true, "https://pay.example.test/i/invoice-1", "invoice-1", null));
        }
        public Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref LookupCalls);
            return Task.FromResult(Lookup(invoiceId));
        }
        public Task<BtcpayInvoiceRecoveryResult> FindInvoiceByOrderIdAsync(Config config, string orderId, CancellationToken ct = default)
            => Task.FromResult(Recovery(orderId));
    }

    private sealed class FakeMail : IApplicationPaymentEmailSender
    {
        public ConcurrentQueue<ApplicationPayment> Sent { get; } = new();
        public int Attempts;
        public Func<ApplicationPayment, Action, CancellationToken, Task>? Deliver;
        public Task SendAsync(Config config, ApplicationPayment payment, Action beforeSend, CancellationToken ct)
        {
            Interlocked.Increment(ref Attempts);
            if (Deliver is not null) return Deliver(payment, beforeSend, ct);
            beforeSend();
            Sent.Enqueue(payment);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
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
