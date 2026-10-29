using System.Net;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection("SMTP environment")]
public sealed class SubmissionConfirmationTests
{
    [Theory]
    [InlineData("application", "paid", false)]
    [InlineData("application", "paid", true)]
    [InlineData("application", "unavailable", false)]
    [InlineData("application", "unavailable", true)]
    [InlineData("application", "free", false)]
    [InlineData("application", "free", true)]
    [InlineData("result", "free", false)]
    [InlineData("result", "free", true)]
    [InlineData("edit", "free", false)]
    [InlineData("edit", "free", true)]
    public async Task AcceptedPaths_RetryReceiptAfterRestart_WithoutRepeatingSubmissionOrPayment(string kind, string payment, bool archive)
    {
        using var f = new Fixture();
        f.Provider.Unavailable = payment == "unavailable";
        var controller = f.Controller(archive);
        var submitted = Applicant(kind, payment);
        var originalJson = JsonSerializer.Serialize(submitted); // Controller normalizes its working DTO.
        var response = Assert.IsType<ApplicationSubmissionResponse>(Assert.IsType<OkObjectResult>(
            await controller.Application(submitted, CancellationToken.None)).Value);
        Assert.True(response.Success);
        Assert.Equal(payment != "free", response.PaymentRequired);
        Assert.Equal(payment == "unavailable" ? true : (bool?)null, response.PaymentUnavailable);
        Assert.Equal(archive ? 1 : 0, controller.Archives);
        Assert.Equal(1, controller.AuditAttempts);
        var receipt = Assert.IsType<SubmissionConfirmation>(f.Store.Get("submission-1"));
        Assert.Equal(response, receipt.Response);
        Assert.Equal("Pending", receipt.State);
        using var original = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(receipt.Message!)));
        Assert.Equal("sender@example.test", Assert.Single(original.From.Mailboxes).Address);
        Assert.Equal("Longevity World Cup", Assert.Single(original.From.Mailboxes).Name);
        Assert.Equal("athlete@example.test", Assert.Single(original.To.Mailboxes).Address);
        Assert.Equal("", Assert.Single(original.To.Mailboxes).Name);
        Assert.Contains(kind == "result" ? "result upload" : kind == "edit" ? "change request" : "application", original.Subject);
        Assert.Contains("Questions, concerns, or signs of aging? Reply to this email.", original.TextBody);
        if (payment == "paid") Assert.Contains(response.CheckoutLink!, original.TextBody);
        if (payment == "unavailable") Assert.Contains("Your payment link is unavailable. We'll email you the next step.", original.TextBody);

        f.Smtp.ConnectFailure = new IOException("private-address@example.test connection failed");
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal("Pending", f.Store.Get("submission-1")!.State);
        Assert.Equal(0, f.Smtp.SendCalls);
        f.Restart(); // New database connection, retry store, cache and dispatcher.
        var retryController = f.Controller(archive);
        var retried = Assert.IsType<OkObjectResult>(await retryController.Application(
            JsonSerializer.Deserialize<ApplicantData>(originalJson)!, CancellationToken.None));
        Assert.Equal(response, retried.Value);
        Assert.Equal(0, retryController.AuditAttempts);
        Assert.Equal(payment == "free" ? 0 : 1, f.Provider.CreateCalls);
        Assert.Equal(payment == "free" ? 0 : 1, f.Database.Run(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ApplicationPayments";
            return Convert.ToInt32(command.ExecuteScalar());
        }));
        f.Smtp.ConnectFailure = null;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal("Sent", f.Store.Get("submission-1")!.State);
        Assert.Equal(original.MessageId, f.Smtp.MessageId);
        Assert.Equal(original.TextBody, f.Smtp.TextBody);
        Assert.Equal(original.HtmlBody, f.Smtp.HtmlBody);
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(1, f.Smtp.SendCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.test/contact")]
    public async Task NoTrustworthyExistingAddress_SavesResponseWithoutQueuingOrGuessing(string? contact)
    {
        using var f = new Fixture(contact);
        var request = Applicant("edit", "free");
        request.MediaContact = "new-unverified@example.test";
        Assert.IsType<OkObjectResult>(await f.Controller().Application(request, CancellationToken.None));
        var receipt = f.Store.Get("submission-1")!;
        Assert.Equal("Skipped", receipt.State);
        Assert.Null(receipt.Message);
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(0, f.Smtp.ConnectCalls);
    }

    [Fact]
    public async Task LegacyCachedResponse_IsReturnedWithoutCreatingHistoricalReceipt()
    {
        using var f = new Fixture();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var legacy = new ApplicationSubmissionRetryStore(cache, f.Responses, f.Clock);
        await using (var lease = await legacy.AcquireAsync("historical", "fingerprint", CancellationToken.None))
            lease.Complete(Response);
        Assert.Equal(Response, await f.Retries.GetCompletedResponseAsync("historical", CancellationToken.None));
        Assert.Null(f.Store.Get("historical"));
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(0, f.Smtp.ConnectCalls);
    }

    [Fact]
    public async Task AtomicCompletion_RecoversWithoutResponseFiles_AndCannotChangeRecipientOnRetry()
    {
        using var f = new Fixture();
        f.Queue();
        using var otherMessage = Message("other@example.test");
        f.Store.Complete("submission-1", "different-payload", Response with { InvoiceId = "other" }, otherMessage, f.Clock.GetUtcNow());
        f.Restart();
        await using var lease = await f.Retries.AcquireAsync("submission-1", "fingerprint", CancellationToken.None);
        Assert.Equal(Response, lease.CachedResponse);
        Assert.False(lease.ShouldProcess);
        Assert.Contains("athlete@example.test", f.Store.Get("submission-1")!.Message);
        Assert.DoesNotContain("other@example.test", f.Store.Get("submission-1")!.Message);
    }

    [Fact]
    public async Task ParallelWorkersOnDifferentConnections_ClaimOnlyOnce()
    {
        using var f = new Fixture();
        f.Queue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Smtp.DuringSend = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        var first = f.Dispatcher().RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var otherDatabase = new DatabaseManager(dbPath: f.DbPath);
        var otherStore = new SubmissionConfirmationStore(otherDatabase);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => f.Dispatcher(otherStore).RunOnceAsync()));
        Assert.Equal(1, f.Smtp.SendCalls);
        release.SetResult();
        await first;
        Assert.Equal("Sent", otherStore.Get("submission-1")!.State);
    }

    [Theory]
    [InlineData(false, "Sent")]
    [InlineData(true, "Uncertain")]
    public async Task RestartDuringLease_RecoversBeforeDispatch_ButNeverReplaysAmbiguousDispatch(bool sending, string expected)
    {
        using var f = new Fixture();
        f.Queue();
        var lease = f.Store.TryClaim(f.Store.Get("submission-1")!.SubmissionKey, f.Clock.GetUtcNow())!;
        if (sending) f.Store.Save(lease, lease.Receipt with { State = "Sending", Attempts = 1 }, f.Clock.GetUtcNow(), release: false);
        f.Restart();
        f.Clock.Advance(SubmissionConfirmationStore.LeaseDuration.Add(TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => f.Store.Save(lease, lease.Receipt with { State = "Sending" }, f.Clock.GetUtcNow(), release: false));
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(expected, f.Store.Get("submission-1")!.State);
        Assert.Equal(sending ? 0 : 1, f.Smtp.SendCalls);
    }

    [Theory]
    [InlineData("reject", "Pending")]
    [InlineData("lost-data-ack", "Uncertain")]
    [InlineData("quit", "Sent")]
    public async Task SmtpAcceptanceBoundary_DistinguishesRejectedData_AmbiguousAcceptance_AndFailedQuit(string failure, string expected)
    {
        using var f = new Fixture();
        f.Queue();
        if (failure == "reject") f.Smtp.SendFailure = new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)451, "private@example.test rejected");
        if (failure == "lost-data-ack") f.Smtp.SendFailure = new IOException("private@example.test DATA acknowledgement lost");
        if (failure == "quit") f.Smtp.DisconnectFailure = new IOException("private@example.test QUIT failed");
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(expected, f.Store.Get("submission-1")!.State);
        Assert.DoesNotContain(f.Log.Messages, value => value.Contains("private@example.test"));
        f.Smtp.SendFailure = null;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(failure == "reject" ? 2 : 1, f.Smtp.SendCalls);
    }

    [Theory]
    [InlineData(false, "Pending")]
    [InlineData(true, "Uncertain")]
    public async Task Cancellation_PreservesDurableStateAccordingToDispatchBoundary(bool duringSend, string expected)
    {
        using var f = new Fixture();
        f.Queue();
        using var cancellation = new CancellationTokenSource();
        Task Cancel(CancellationToken ct) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        if (duringSend) f.Smtp.DuringSend = Cancel;
        else f.Smtp.DuringConnect = Cancel;
        await f.Dispatcher().RunOnceAsync(cancellation.Token);
        Assert.Equal(expected, f.Store.Get("submission-1")!.State);
        f.Restart();
        f.Smtp.DuringConnect = null;
        f.Smtp.DuringSend = null;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(duringSend ? "Uncertain" : "Sent", f.Store.Get("submission-1")!.State);
    }

    [Fact]
    public async Task PersistentFailure_UsesBackoffAndStopsAtBoundedAttemptLimit()
    {
        using var f = new Fixture();
        f.Queue();
        f.Smtp.ConnectFailure = new IOException("offline");
        for (var i = 1; i <= SubmissionConfirmationStore.MaximumAttempts; i++)
        {
            var before = f.Clock.GetUtcNow();
            await f.Dispatcher().RunOnceAsync();
            var receipt = f.Store.Get("submission-1")!;
            Assert.Equal(i, receipt.Attempts);
            Assert.True(receipt.NextAttemptUtc > before);
            await f.Dispatcher().RunOnceAsync();
            Assert.Equal(i, f.Smtp.ConnectCalls);
            f.Clock.Advance(receipt.NextAttemptUtc - before);
        }
        Assert.Equal("Exhausted", f.Store.Get("submission-1")!.State);
        f.Restart();
        f.Smtp.ConnectFailure = null;
        f.Clock.Advance(TimeSpan.FromDays(1));
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(0, f.Smtp.SendCalls);
    }

    [Fact]
    public async Task ExpiredLeaseCannotTransmitAfterSlowAuthentication()
    {
        using var f = new Fixture();
        f.Queue();
        f.Smtp.DuringConnect = _ => { f.Clock.Advance(TimeSpan.FromMinutes(3)); return Task.CompletedTask; };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Dispatcher().RunOnceAsync());
        Assert.Equal(0, f.Smtp.SendCalls);
        f.Smtp.DuringConnect = null;
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal("Sent", f.Store.Get("submission-1")!.State);
    }

    [Fact]
    public async Task ParallelApplicationRetries_AndCanceledPostAcceptanceReporting_DoNotRepeatAcceptedWork()
    {
        using var f = new Fixture();
        var controller = f.Controller();
        controller.CancelSuccessReport = true;
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => controller.Application(Applicant("application", "paid"), CancellationToken.None)));
        foreach (var result in results)
            Assert.True(Assert.IsType<ApplicationSubmissionResponse>(Assert.IsType<OkObjectResult>(result).Value).Success);
        Assert.Equal(1, controller.AuditAttempts);
        Assert.Equal(1, f.Provider.CreateCalls);
        Assert.Equal("Pending", f.Store.Get("submission-1")!.State);
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal(1, f.Smtp.SendCalls);
    }

    [Fact]
    public async Task ResponseExpiry_DoesNotResetDeliveryTombstoneOrResendHistoricalReceipt()
    {
        using var f = new Fixture();
        f.Queue();
        await f.Dispatcher().RunOnceAsync();
        f.Clock.Advance(ApplicationSubmissionRetryStore.CompletedSubmissionLifetime.Add(TimeSpan.FromDays(1)));
        f.Queue();
        await f.Dispatcher().RunOnceAsync();
        Assert.Equal("Sent", f.Store.Get("submission-1")!.State);
        Assert.Equal(1, f.Smtp.SendCalls);
    }

    [Fact]
    public async Task RegisteredWorker_RecoversPendingReceiptAtHostStartup()
    {
        using var f = new Fixture();
        f.Queue();
        await using var factory = new TestWebApplicationFactory(builder =>
        {
            builder.UseSetting("EnableSubmissionConfirmations", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<SubmissionConfirmationStore>();
                services.AddSingleton(f.Store);
                services.RemoveAll<ISubmissionConfirmationSender>();
                services.AddSingleton<ISubmissionConfirmationSender>(f.Sender());
                services.RemoveAll<Config>();
                services.AddSingleton(f.Config);
            });
        });
        _ = factory.Services;
        await f.Smtp.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, f.Smtp.SendCalls);
    }

    private static ApplicationSubmissionResponse Response => new(true, false, null, null);
    private static MimeMessage Message(string email = "athlete@example.test")
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Longevity World Cup", "sender@example.test"));
        message.To.Add(new MailboxAddress("", email));
        message.Subject = "Your Longevity World Cup application was received";
        message.Body = new BodyBuilder { TextBody = "receipt text", HtmlBody = "<p>receipt text</p>" }.ToMessageBody();
        return message;
    }

    private static ApplicantData Applicant(string kind, string payment)
    {
        using var image = new Image<Rgb24>(2, 2);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        var picture = "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
        var applicant = new ApplicantData { SubmissionId = "submission-1", Name = "Applicant Ada" };
        if (kind == "edit") { applicant.Why = "Updated motivation."; return applicant; }
        applicant.Biomarkers = [new BiomarkerData { Date = "2026-02-02", AlbGL = 45 }];
        applicant.ProofPics = [picture];
        if (kind == "result") return applicant;
        applicant.AccountEmail = "athlete@example.test";
        applicant.DateOfBirth = new DateOfBirthData { Year = 1980, Month = 1, Day = 2 };
        applicant.ProfilePic = picture;
        applicant.Division = "Open";
        applicant.Flag = "Cyberspace";
        applicant.Why = "Learning about health.";
        applicant.MediaContact = "athlete@example.test";
        if (payment == "free") applicant.FreePass = "fixture";
        return applicant;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "LwcConfirmationTests", Guid.NewGuid().ToString("N"));
        private MemoryCache _cache = new(new MemoryCacheOptions());
        public string DbPath => Path.Combine(_root, "receipts.db");
        public string Responses => Path.Combine(_root, "responses");
        public DatabaseManager Database { get; private set; }
        public SubmissionConfirmationStore Store { get; private set; }
        public ApplicationSubmissionRetryStore Retries { get; private set; }
        public ManualClock Clock { get; } = new();
        public FakeSmtp Smtp { get; } = new();
        public RecordingLog Log { get; } = new();
        public FakeProvider Provider { get; } = new();
        public Config Config { get; } = new()
        {
            EmailFrom = "sender@example.test", EmailTo = "reviewer@example.test", SmtpServer = "smtp.example.test",
            SmtpPort = 587, SmtpUser = "fixture", SmtpPassword = "fixture-only", BTCPayBaseUrl = "https://pay.example.test",
            BTCPayStoreId = "fixture", BTCPayGreenfieldApiKey = "fixture-only"
        };
        public TestEnvironment Environment { get; }

        public Fixture(string? contact = "athlete@example.test")
        {
            Database = new DatabaseManager(dbPath: DbPath);
            Store = new SubmissionConfirmationStore(Database);
            Retries = NewRetries();
            Environment = new TestEnvironment { ContentRootPath = _root, WebRootPath = Path.Combine(_root, "wwwroot") };
            var athlete = Path.Combine(Environment.WebRootPath, "athletes", "applicant_ada");
            Directory.CreateDirectory(athlete);
            File.WriteAllText(Path.Combine(athlete, "athlete.json"), JsonSerializer.Serialize(new { Name = "Applicant Ada", MediaContact = contact }));
        }
        private ApplicationSubmissionRetryStore NewRetries() => new(_cache, Responses, Clock, confirmations: Store);
        public void Restart()
        {
            Database.Dispose();
            _cache.Dispose();
            _cache = new MemoryCache(new MemoryCacheOptions());
            Database = new DatabaseManager(dbPath: DbPath);
            Store = new SubmissionConfirmationStore(Database);
            Retries = NewRetries();
        }
        public void Queue()
        {
            using var message = Message();
            Store.Complete("submission-1", "fingerprint", Response, message, Clock.GetUtcNow());
        }
        public SmtpSubmissionConfirmationSender Sender() => new(Log, () => Smtp);
        public SubmissionConfirmationDispatcher Dispatcher(SubmissionConfirmationStore? store = null)
            => new(store ?? Store, Sender(), Config, Clock, Log);
        public FixtureController Controller(bool archive = false) => new(this, archive)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        public void Dispose() { _cache.Dispose(); Database.Dispose(); Directory.Delete(_root, recursive: true); }
    }

    private sealed class FixtureController(Fixture f, bool archive) : ApplicationController(f.Environment,
        NullLogger<ApplicationController>.Instance, f.Retries, new ApplicationPaymentStore(f.Database), f.Provider)
    {
        public int AuditAttempts;
        public int Archives;
        public bool CancelSuccessReport;
        internal override Task TrackApplicationEventAsync(string eventName, string component, string step,
            string outcome, string? errorCode, long? durationMs, IReadOnlyDictionary<string, object?>? metadata, CancellationToken ct)
            => CancelSuccessReport && eventName == "application_submit_succeeded"
                ? Task.FromException(new OperationCanceledException("optional reporting canceled")) : Task.CompletedTask;
        internal override Task<Config> LoadSubmissionConfigAsync() => Task.FromResult(f.Config);
        internal override Task SendEmailThroughSmtpAsync(Config config, MimeMessage message, CancellationToken ct = default)
        {
            AuditAttempts++;
            Assert.Equal("reviewer@example.test", Assert.Single(message.To.Mailboxes).Address);
            return archive ? Task.FromException(new IOException("audit unavailable")) : Task.CompletedTask;
        }
        internal override async Task<string> PersistApplicationSubmissionArchiveAsync(string zipPath, string folderKey, string submissionId, CancellationToken ct)
        {
            Archives++;
            var destination = Path.Combine(f.Environment.ContentRootPath, "saved.zip");
            await System.IO.File.WriteAllBytesAsync(destination, await System.IO.File.ReadAllBytesAsync(zipPath, ct), ct);
            return destination;
        }
    }

    private sealed class FakeProvider : IBtcpayInvoiceClient
    {
        public int CreateCalls;
        public bool Unavailable;
        public Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
        {
            CreateCalls++;
            return Task.FromResult(Unavailable ? new BtcpayInvoiceCreateResult(false, null, null, "offline")
                : new BtcpayInvoiceCreateResult(true, "https://pay.example.test/i/fixture", "fixture-invoice", null));
        }
        public Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BtcpayInvoiceRecoveryResult> FindInvoiceByOrderIdAsync(Config config, string orderId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeSmtp : SmtpClient
    {
        public Exception? ConnectFailure, SendFailure, DisconnectFailure;
        public Func<CancellationToken, Task>? DuringConnect, DuringSend;
        public int ConnectCalls, SendCalls;
        public string? MessageId, TextBody, HtmlBody;
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            Assert.Equal(SecureSocketOptions.StartTls, options);
            if (DuringConnect is not null) await DuringConnect(cancellationToken);
            if (ConnectFailure is not null) throw ConnectFailure;
        }
        public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override async Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress? progress = null)
        {
            SendCalls++;
            if (DuringSend is not null) await DuringSend(cancellationToken);
            if (SendFailure is not null) throw SendFailure;
            MessageId = message.MessageId; TextBody = message.TextBody; HtmlBody = message.HtmlBody;
            Delivered.TrySetResult();
            return "250 Accepted";
        }
        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
            => DisconnectFailure is null ? Task.CompletedTask : Task.FromException(DisconnectFailure);
        protected override void Dispose(bool disposing) { /* Fixture intentionally reuses the fake across restarts. */ }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
    private sealed class RecordingLog : ILogger<SubmissionConfirmationDispatcher>, ILogger<SmtpSubmissionConfirmationSender>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
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
