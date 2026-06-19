using System.Text.Json.Nodes;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DiscountSignupReportServiceTests
{
    [Fact]
    public async Task SendReportForPeriodCountsCompletedPaidAndAcceptedApplications()
    {
        using var fixture = DiscountReportFixture.Create();
        var periodStart = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var periodEnd = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var generatedAt = DateTimeOffset.Parse("2026-06-04T08:00:00Z");

        fixture.Athletes.Snapshot.Add(new JsonObject
        {
            ["AthleteSlug"] = "accepted_alice",
            ["Name"] = "Accepted Alice",
            ["DisplayName"] = "Alice Leader"
        });
        fixture.Btcpay.Results["inv-paid"] = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Settled",
              "additionalStatus": "PaidLate",
              "amount": "60.00",
              "currency": "USD",
              "paidAmount": "42.50",
              "checkoutLink": "https://btcpay.example.test/i/inv-paid",
              "buyer": { "email": "alice@example.test" },
              "metadata": { "athleteName": "Accepted Alice" }
            }
            """);
        fixture.Btcpay.Results["inv-open"] = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "New",
              "amount": "60.00",
              "currency": "USD",
              "paidAmount": "0",
              "checkoutLink": "https://btcpay.example.test/i/inv-open"
            }
            """);

        await fixture.Service.RecordApplicationAsync(new DiscountSignupApplication(
            "sub-paid",
            DiscountCodes.MightyKlaus,
            DateTimeOffset.Parse("2026-05-12T10:00:00Z"),
            "Accepted Alice",
            "Alice Leader",
            "alice@example.test",
            "accepted_alice",
            "full-application",
            60m,
            "USD",
            PaymentRequired: true,
            "inv-paid",
            "https://btcpay.example.test/i/inv-paid"));
        await fixture.Service.RecordApplicationAsync(new DiscountSignupApplication(
            "sub-open",
            DiscountCodes.MightyKlaus,
            DateTimeOffset.Parse("2026-05-20T10:00:00Z"),
            "Pending Bob",
            null,
            "bob@example.test",
            "pending_bob",
            "full-application",
            60m,
            "USD",
            PaymentRequired: true,
            "inv-open",
            "https://btcpay.example.test/i/inv-open"));
        await fixture.Service.RecordApplicationAsync(new DiscountSignupApplication(
            "sub-result-upload",
            DiscountCodes.MightyKlaus,
            DateTimeOffset.Parse("2026-05-22T10:00:00Z"),
            "Existing Athlete",
            null,
            "existing@example.test",
            "existing_athlete",
            "result-upload",
            60m,
            "USD",
            PaymentRequired: true,
            "inv-result",
            "https://btcpay.example.test/i/inv-result"));

        var result = await fixture.Service.SendReportForPeriodAsync(
            DiscountCodes.MightyKlaus,
            periodStart,
            periodEnd,
            generatedAt);

        Assert.True(result.Sent);
        Assert.Equal(2, result.CompletedCount);
        Assert.Equal(1, result.PaidCount);
        Assert.Equal(1, result.AcceptedCount);
        var paidTotal = Assert.Single(result.PaidTotals);
        Assert.Equal("USD", paidTotal.Currency);
        Assert.Equal(42.50m, paidTotal.Amount);
        Assert.Equal(["inv-paid", "inv-open"], fixture.Btcpay.RequestedInvoiceIds);

        var sent = Assert.Single(fixture.Email.Sent);
        Assert.Equal(
            [DiscountSignupReportService.DefaultRecipientEmail, DiscountSignupReportService.MightyKlausRecipientEmail],
            sent.RecipientEmails);
        Assert.Contains("[LWC] MightyKlaus discount signup report: 2026-05", sent.Subject);
        Assert.Contains("- Full applications completed: 2", sent.Body);
        Assert.Contains("- Paid applications: 1", sent.Body);
        Assert.Contains("- Total paid: USD 42.5", sent.Body);
        Assert.Contains("- Accepted into leaderboard: 1", sent.Body);
        Assert.Contains("Alice Leader <alice@example.test>; inv-paid; paid USD 42.5; accepted as accepted_alice", sent.Body);
        Assert.Contains("Pending Bob <bob@example.test>; inv-open; not paid (New); not accepted yet", sent.Body);
        Assert.DoesNotContain("Existing Athlete", sent.Body);

        var duplicate = await fixture.Service.SendReportForPeriodAsync(
            DiscountCodes.MightyKlaus,
            periodStart,
            periodEnd,
            generatedAt);

        Assert.False(duplicate.Sent);
        Assert.Equal("already sent", duplicate.Reason);
        Assert.Single(fixture.Email.Sent);
    }

    [Fact]
    public void GetReportRecipientEmailsAddsMightyKlausRecipientWithoutDuplicatingConfiguredRecipients()
    {
        var recipients = DiscountSignupReportService.GetReportRecipientEmails(
            "owner@example.test; klaus@klaustownsend.com, OWNER@example.test");

        Assert.Equal(
            ["owner@example.test", DiscountSignupReportService.MightyKlausRecipientEmail],
            recipients);
    }

    [Theory]
    [InlineData("mightyklaus", "full-application", true)]
    [InlineData("MIGHTYKLAUS", "full-application", true)]
    [InlineData("mightyklaus", "result-upload", false)]
    [InlineData("mightyklaus", "edit-request", false)]
    [InlineData("other", "full-application", false)]
    [InlineData(null, "full-application", false)]
    public void ShouldTrackDiscountSignupTracksOnlyMightyKlausFullApplications(string? discountCode, string submissionKind, bool expected)
    {
        Assert.Equal(expected, DiscountSignupReportService.ShouldTrackDiscountSignup(discountCode, submissionKind));
    }

    [Fact]
    public void BtcpayInvoiceParserTreatsPositivePaidAmountAsPaid()
    {
        var invoice = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Processing",
              "amount": "60",
              "currency": "USD",
              "paidAmount": "10.25"
            }
            """);

        Assert.True(invoice.Success);
        Assert.True(invoice.IsPaid);
        Assert.Equal(60m, invoice.Amount);
        Assert.Equal(10.25m, invoice.PaidAmount);
    }

    private sealed class DiscountReportFixture : IDisposable
    {
        private readonly string _root;

        private DiscountReportFixture(
            string root,
            DatabaseManager database,
            FakeAthleteSnapshotProvider athletes,
            FakeBtcpayInvoiceClient btcpay,
            RecordingReportEmailSender email,
            DiscountSignupReportService service)
        {
            _root = root;
            Database = database;
            Athletes = athletes;
            Btcpay = btcpay;
            Email = email;
            Service = service;
        }

        public DatabaseManager Database { get; }
        public FakeAthleteSnapshotProvider Athletes { get; }
        public FakeBtcpayInvoiceClient Btcpay { get; }
        public RecordingReportEmailSender Email { get; }
        public DiscountSignupReportService Service { get; }

        public static DiscountReportFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-discount-report-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var database = new DatabaseManager(dbPath: Path.Combine(root, "test.db"));
            var athletes = new FakeAthleteSnapshotProvider();
            var btcpay = new FakeBtcpayInvoiceClient();
            var email = new RecordingReportEmailSender();
            var config = new Config
            {
                EmailFrom = "from@example.test",
                SmtpServer = "smtp.example.test",
                SmtpPort = 587,
                SmtpUser = "smtp-user",
                SmtpPassword = "smtp-password"
            };
            var service = new DiscountSignupReportService(
                database,
                athletes,
                btcpay,
                email,
                config,
                NullLogger<DiscountSignupReportService>.Instance);

            return new DiscountReportFixture(root, database, athletes, btcpay, email, service);
        }

        public void Dispose()
        {
            Database.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeAthleteSnapshotProvider : IAthleteSnapshotProvider
    {
        public JsonArray Snapshot { get; } = [];

        public JsonArray GetAthletesSnapshot() => (JsonArray)Snapshot.DeepClone();
    }

    private sealed class FakeBtcpayInvoiceClient : IBtcpayInvoiceClient
    {
        public Dictionary<string, BtcpayInvoiceLookupResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RequestedInvoiceIds { get; } = [];

        public Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
            => Task.FromResult(BtcpayInvoiceCreateResult.Failure("not used"));

        public Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default)
        {
            RequestedInvoiceIds.Add(invoiceId);
            return Task.FromResult(
                Results.TryGetValue(invoiceId, out var result)
                    ? result
                    : BtcpayInvoiceLookupResult.Failure("missing fake invoice"));
        }
    }

    private sealed class RecordingReportEmailSender : IDiscountSignupReportEmailSender
    {
        public List<SentEmail> Sent { get; } = [];

        public Task SendAsync(Config config, IReadOnlyList<string> recipientEmails, string subject, string textBody, CancellationToken ct = default)
        {
            Sent.Add(new SentEmail([.. recipientEmails], subject, textBody));
            return Task.CompletedTask;
        }
    }

    private sealed record SentEmail(IReadOnlyList<string> RecipientEmails, string Subject, string Body);
}
