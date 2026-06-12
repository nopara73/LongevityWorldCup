using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using MimeKit;

namespace LongevityWorldCup.Website.Business;

public interface IDiscountSignupReportEmailSender
{
    Task SendAsync(Config config, string recipientEmail, string subject, string textBody, CancellationToken ct = default);
}

public sealed class SmtpDiscountSignupReportEmailSender : IDiscountSignupReportEmailSender
{
    public async Task SendAsync(Config config, string recipientEmail, string subject, string textBody, CancellationToken ct = default)
    {
        var smtpServer = RequireConfiguredValue(config.SmtpServer, nameof(config.SmtpServer));
        var smtpUser = RequireConfiguredValue(config.SmtpUser, nameof(config.SmtpUser));
        var smtpPort = RequireConfiguredPort(config.SmtpPort);
        var smtpPassword = GetConfiguredSecret(config.SmtpPassword, "LWC_SMTP_PASSWORD");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Longevity World Cup", RequireConfiguredValue(config.EmailFrom, nameof(config.EmailFrom))));
        message.To.Add(new MailboxAddress(string.Empty, RequireConfiguredValue(recipientEmail, nameof(recipientEmail))));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = textBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(smtpPassword))
        {
            await client.AuthenticateAsync(smtpUser, smtpPassword, ct).ConfigureAwait(false);
        }
        else
        {
            var accessToken = await GmailAuth.GetAccessTokenAsync(config).ConfigureAwait(false);
            client.AuthenticationMechanisms.Remove("LOGIN");
            client.AuthenticationMechanisms.Remove("PLAIN");
            var oauth2 = new SaslMechanismOAuth2(smtpUser, accessToken);
            await client.AuthenticateAsync(oauth2, ct).ConfigureAwait(false);
        }

        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }

    private static string RequireConfiguredValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{name} is not configured.");

        return value.Trim();
    }

    private static int RequireConfiguredPort(int value)
    {
        if (value <= 0)
            throw new InvalidOperationException($"{nameof(Config.SmtpPort)} must be configured with a positive port.");

        return value;
    }

    private static string? GetConfiguredSecret(string? configValue, string environmentVariableName)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue.Trim();

        return string.IsNullOrWhiteSpace(configValue) ? null : configValue.Trim();
    }
}

public sealed class DiscountSignupReportService
{
    public const string DefaultRecipientEmail = "longevityworldcup@gmail.com";
    private const string ApplicationsTable = "DiscountSignupApplications";
    private const string ReportsTable = "DiscountSignupMonthlyReportLog";
    private const string FullApplicationSubmissionKind = "full-application";

    private readonly DatabaseManager _db;
    private readonly IAthleteSnapshotProvider _athletes;
    private readonly IBtcpayInvoiceClient _btcpay;
    private readonly IDiscountSignupReportEmailSender _email;
    private readonly Config _config;
    private readonly ILogger<DiscountSignupReportService> _logger;

    public DiscountSignupReportService(
        DatabaseManager db,
        IAthleteSnapshotProvider athletes,
        IBtcpayInvoiceClient btcpay,
        IDiscountSignupReportEmailSender email,
        Config config,
        ILogger<DiscountSignupReportService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _athletes = athletes ?? throw new ArgumentNullException(nameof(athletes));
        _btcpay = btcpay ?? throw new ArgumentNullException(nameof(btcpay));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        EnsureTables();
    }

    public static bool ShouldTrackDiscountSignup(string? discountCode, string submissionKind)
    {
        return string.Equals(DiscountCodes.Normalize(discountCode), DiscountCodes.MightyKlaus, StringComparison.Ordinal)
            && string.Equals(submissionKind, FullApplicationSubmissionKind, StringComparison.OrdinalIgnoreCase);
    }

    public async Task RecordApplicationAsync(DiscountSignupApplication application, CancellationToken ct = default)
    {
        if (!ShouldTrackDiscountSignup(application.DiscountCode, application.SubmissionKind))
            return;

        var now = DateTimeOffset.UtcNow;
        var submittedAt = application.SubmittedAtUtc.ToUniversalTime();
        await _db.RunAsync(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                INSERT INTO {ApplicationsTable} (
                    Id,
                    SubmissionId,
                    DiscountCode,
                    SubmittedAtUtc,
                    ApplicantName,
                    DisplayName,
                    AccountEmail,
                    ExpectedAthleteSlug,
                    SubmissionKind,
                    RequestedAmount,
                    RequestedCurrency,
                    PaymentRequired,
                    InvoiceId,
                    CheckoutLink,
                    UpdatedAtUtc
                )
                VALUES (
                    @id,
                    @submissionId,
                    @discountCode,
                    @submittedAtUtc,
                    @applicantName,
                    @displayName,
                    @accountEmail,
                    @expectedAthleteSlug,
                    @submissionKind,
                    @requestedAmount,
                    @requestedCurrency,
                    @paymentRequired,
                    @invoiceId,
                    @checkoutLink,
                    @updatedAtUtc
                )
                ON CONFLICT(SubmissionId) DO UPDATE SET
                    DiscountCode = excluded.DiscountCode,
                    SubmittedAtUtc = excluded.SubmittedAtUtc,
                    ApplicantName = excluded.ApplicantName,
                    DisplayName = excluded.DisplayName,
                    AccountEmail = excluded.AccountEmail,
                    ExpectedAthleteSlug = excluded.ExpectedAthleteSlug,
                    SubmissionKind = excluded.SubmissionKind,
                    RequestedAmount = excluded.RequestedAmount,
                    RequestedCurrency = excluded.RequestedCurrency,
                    PaymentRequired = excluded.PaymentRequired,
                    InvoiceId = COALESCE(excluded.InvoiceId, {ApplicationsTable}.InvoiceId),
                    CheckoutLink = COALESCE(excluded.CheckoutLink, {ApplicationsTable}.CheckoutLink),
                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                """;
            Add(cmd, "@id", Guid.NewGuid().ToString("N"));
            Add(cmd, "@submissionId", application.SubmissionId);
            Add(cmd, "@discountCode", DiscountCodes.MightyKlaus);
            Add(cmd, "@submittedAtUtc", submittedAt.ToString("o"));
            Add(cmd, "@applicantName", TrimToNull(application.ApplicantName));
            Add(cmd, "@displayName", TrimToNull(application.DisplayName));
            Add(cmd, "@accountEmail", TrimToNull(application.AccountEmail));
            Add(cmd, "@expectedAthleteSlug", TrimToNull(application.ExpectedAthleteSlug));
            Add(cmd, "@submissionKind", application.SubmissionKind);
            Add(cmd, "@requestedAmount", FormatDecimal(application.RequestedAmount));
            Add(cmd, "@requestedCurrency", NormalizeCurrency(application.RequestedCurrency));
            Add(cmd, "@paymentRequired", application.PaymentRequired ? 1 : 0);
            Add(cmd, "@invoiceId", TrimToNull(application.InvoiceId));
            Add(cmd, "@checkoutLink", TrimToNull(application.CheckoutLink));
            Add(cmd, "@updatedAtUtc", now.ToString("o"));
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }, ct).ConfigureAwait(false);
    }

    public async Task UpdatePaymentStatusForInvoiceAsync(string invoiceId, BtcpayInvoiceLookupResult invoice, DateTimeOffset? observedAtUtc = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId) || !invoice.Success)
            return;

        var observedAt = (observedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        await _db.RunAsync(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                UPDATE {ApplicationsTable}
                SET InvoiceStatus = @status,
                    InvoiceAdditionalStatus = @additionalStatus,
                    InvoiceAmount = @invoiceAmount,
                    InvoiceCurrency = @invoiceCurrency,
                    PaidAmount = @paidAmount,
                    PaidAtUtc = CASE
                        WHEN @isPaid = 1 THEN COALESCE(PaidAtUtc, @paidAtUtc)
                        ELSE PaidAtUtc
                    END,
                    UpdatedAtUtc = @updatedAtUtc
                WHERE InvoiceId = @invoiceId;
                """;
            Add(cmd, "@status", TrimToNull(invoice.Status));
            Add(cmd, "@additionalStatus", TrimToNull(invoice.AdditionalStatus));
            Add(cmd, "@invoiceAmount", FormatNullableDecimal(invoice.Amount) ?? TrimToNull(invoice.AmountText));
            Add(cmd, "@invoiceCurrency", TrimToNull(invoice.Currency));
            Add(cmd, "@paidAmount", FormatNullableDecimal(invoice.PaidAmount) ?? TrimToNull(invoice.PaidAmountText));
            Add(cmd, "@isPaid", invoice.IsPaid ? 1 : 0);
            Add(cmd, "@paidAtUtc", observedAt.ToString("o"));
            Add(cmd, "@updatedAtUtc", observedAt.ToString("o"));
            Add(cmd, "@invoiceId", invoiceId.Trim());
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }, ct).ConfigureAwait(false);
    }

    public Task<DiscountSignupReportResult> SendPreviousMonthReportAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var utc = nowUtc.ToUniversalTime();
        var currentMonthStart = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var periodStart = currentMonthStart.AddMonths(-1);
        return SendReportForPeriodAsync(DiscountCodes.MightyKlaus, periodStart, currentMonthStart, utc, ct);
    }

    public async Task<DiscountSignupReportResult> SendReportForPeriodAsync(
        string discountCode,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset generatedAtUtc,
        CancellationToken ct = default)
    {
        var normalizedDiscount = DiscountCodes.Normalize(discountCode);
        if (normalizedDiscount is null)
            throw new ArgumentException("Unsupported discount code.", nameof(discountCode));

        var periodStart = periodStartUtc.ToUniversalTime();
        var periodEnd = periodEndUtc.ToUniversalTime();
        if (periodEnd <= periodStart)
            throw new ArgumentException("Report period end must be after period start.", nameof(periodEndUtc));

        if (HasReportBeenSent(normalizedDiscount, periodStart))
        {
            return DiscountSignupReportResult.NotSent(normalizedDiscount, periodStart, periodEnd, "already sent");
        }

        await RefreshPaymentStatusesAsync(normalizedDiscount, periodStart, periodEnd, ct).ConfigureAwait(false);

        var records = GetApplications(normalizedDiscount, periodStart, periodEnd);
        var report = BuildReport(normalizedDiscount, periodStart, periodEnd, generatedAtUtc.ToUniversalTime(), records, _athletes.GetAthletesSnapshot());
        var recipient = string.IsNullOrWhiteSpace(_config.DiscountSignupReportEmailTo)
            ? DefaultRecipientEmail
            : _config.DiscountSignupReportEmailTo.Trim();

        await _email.SendAsync(_config, recipient, report.Subject, report.Body, ct).ConfigureAwait(false);
        MarkReportSent(normalizedDiscount, periodStart, periodEnd, generatedAtUtc.ToUniversalTime(), recipient, report.Subject, report.Body);

        _logger.LogInformation(
            "Sent {DiscountCode} signup report for {PeriodStart:yyyy-MM} to {RecipientEmail}. Completed={CompletedCount} Paid={PaidCount} Accepted={AcceptedCount}",
            normalizedDiscount,
            periodStart,
            recipient,
            report.CompletedCount,
            report.PaidCount,
            report.AcceptedCount);

        return new DiscountSignupReportResult(
            Sent: true,
            DiscountCode: normalizedDiscount,
            PeriodStartUtc: periodStart,
            PeriodEndUtc: periodEnd,
            Reason: null,
            CompletedCount: report.CompletedCount,
            PaidCount: report.PaidCount,
            AcceptedCount: report.AcceptedCount,
            PaidTotals: report.PaidTotals);
    }

    internal static DiscountSignupReport BuildReport(
        string discountCode,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<DiscountSignupStoredApplication> records,
        JsonArray athleteSnapshot)
    {
        var acceptedIndex = BuildAcceptedAthleteIndex(athleteSnapshot);
        var rows = records
            .OrderBy(r => r.SubmittedAtUtc)
            .ThenBy(r => r.ApplicantName, StringComparer.OrdinalIgnoreCase)
            .Select(r => BuildReportRow(r, acceptedIndex))
            .ToList();

        var paidRows = rows.Where(r => r.IsPaid).ToList();
        var paidTotals = paidRows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.PaymentCurrency) ? "USD" : r.PaymentCurrency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DiscountSignupPaidTotal(g.Key.ToUpperInvariant(), g.Sum(r => r.PaymentAmountForTotals.GetValueOrDefault())))
            .ToList();

        var periodLabel = periodStartUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var subject = $"[LWC] MightyKlaus discount signup report: {periodLabel}";
        var body = BuildReportBody(discountCode, periodStartUtc, periodEndUtc, generatedAtUtc, rows, paidTotals);

        return new DiscountSignupReport(
            subject,
            body,
            rows.Count,
            paidRows.Count,
            rows.Count(r => r.Accepted),
            paidTotals);
    }

    private async Task RefreshPaymentStatusesAsync(string discountCode, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, CancellationToken ct)
    {
        var invoices = GetApplications(discountCode, periodStartUtc, periodEndUtc)
            .Where(r => !string.IsNullOrWhiteSpace(r.InvoiceId))
            .Select(r => r.InvoiceId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var invoiceId in invoices)
        {
            ct.ThrowIfCancellationRequested();

            BtcpayInvoiceLookupResult invoice;
            try
            {
                invoice = await _btcpay.GetInvoiceAsync(_config, invoiceId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh BTCPay invoice {InvoiceId} for discount signup report.", invoiceId);
                continue;
            }

            if (!invoice.Success)
            {
                _logger.LogWarning("Failed to refresh BTCPay invoice {InvoiceId} for discount signup report: {Error}", invoiceId, invoice.Error);
                continue;
            }

            await UpdatePaymentStatusForInvoiceAsync(invoiceId, invoice, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<DiscountSignupStoredApplication> GetApplications(string discountCode, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc)
    {
        return _db.Run(sqlite =>
        {
            var rows = new List<DiscountSignupStoredApplication>();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                SELECT
                    Id,
                    SubmissionId,
                    DiscountCode,
                    SubmittedAtUtc,
                    ApplicantName,
                    DisplayName,
                    AccountEmail,
                    ExpectedAthleteSlug,
                    SubmissionKind,
                    RequestedAmount,
                    RequestedCurrency,
                    PaymentRequired,
                    InvoiceId,
                    CheckoutLink,
                    InvoiceStatus,
                    InvoiceAdditionalStatus,
                    InvoiceAmount,
                    InvoiceCurrency,
                    PaidAmount,
                    PaidAtUtc,
                    UpdatedAtUtc
                FROM {ApplicationsTable}
                WHERE DiscountCode = @discountCode
                  AND SubmissionKind = @submissionKind
                  AND SubmittedAtUtc >= @periodStartUtc
                  AND SubmittedAtUtc < @periodEndUtc
                ORDER BY SubmittedAtUtc ASC;
                """;
            Add(cmd, "@discountCode", discountCode);
            Add(cmd, "@submissionKind", FullApplicationSubmissionKind);
            Add(cmd, "@periodStartUtc", periodStartUtc.ToString("o"));
            Add(cmd, "@periodEndUtc", periodEndUtc.ToString("o"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new DiscountSignupStoredApplication(
                    Id: reader.GetString(0),
                    SubmissionId: reader.GetString(1),
                    DiscountCode: reader.GetString(2),
                    SubmittedAtUtc: ParseDateTimeOffset(reader.GetString(3)),
                    ApplicantName: ReadNullableString(reader, 4),
                    DisplayName: ReadNullableString(reader, 5),
                    AccountEmail: ReadNullableString(reader, 6),
                    ExpectedAthleteSlug: ReadNullableString(reader, 7),
                    SubmissionKind: reader.GetString(8),
                    RequestedAmount: ParseDecimal(reader.GetString(9)).GetValueOrDefault(),
                    RequestedCurrency: reader.GetString(10),
                    PaymentRequired: reader.GetInt32(11) == 1,
                    InvoiceId: ReadNullableString(reader, 12),
                    CheckoutLink: ReadNullableString(reader, 13),
                    InvoiceStatus: ReadNullableString(reader, 14),
                    InvoiceAdditionalStatus: ReadNullableString(reader, 15),
                    InvoiceAmount: ParseDecimal(ReadNullableString(reader, 16)),
                    InvoiceCurrency: ReadNullableString(reader, 17),
                    PaidAmount: ParseDecimal(ReadNullableString(reader, 18)),
                    PaidAtUtc: ParseNullableDateTimeOffset(ReadNullableString(reader, 19)),
                    UpdatedAtUtc: ParseDateTimeOffset(reader.GetString(20))));
            }

            return rows;
        });
    }

    private bool HasReportBeenSent(string discountCode, DateTimeOffset periodStartUtc)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                SELECT 1
                FROM {ReportsTable}
                WHERE DiscountCode = @discountCode
                  AND PeriodStartUtc = @periodStartUtc
                LIMIT 1;
                """;
            Add(cmd, "@discountCode", discountCode);
            Add(cmd, "@periodStartUtc", periodStartUtc.ToString("o"));
            return cmd.ExecuteScalar() is not null;
        });
    }

    private void MarkReportSent(
        string discountCode,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset sentAtUtc,
        string recipientEmail,
        string subject,
        string body)
    {
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                INSERT INTO {ReportsTable} (
                    DiscountCode,
                    PeriodStartUtc,
                    PeriodEndUtc,
                    SentAtUtc,
                    RecipientEmail,
                    Subject,
                    Body
                )
                VALUES (
                    @discountCode,
                    @periodStartUtc,
                    @periodEndUtc,
                    @sentAtUtc,
                    @recipientEmail,
                    @subject,
                    @body
                );
                """;
            Add(cmd, "@discountCode", discountCode);
            Add(cmd, "@periodStartUtc", periodStartUtc.ToString("o"));
            Add(cmd, "@periodEndUtc", periodEndUtc.ToString("o"));
            Add(cmd, "@sentAtUtc", sentAtUtc.ToString("o"));
            Add(cmd, "@recipientEmail", recipientEmail);
            Add(cmd, "@subject", subject);
            Add(cmd, "@body", body);
            cmd.ExecuteNonQuery();
        });
    }

    private void EnsureTables()
    {
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                $"""
                CREATE TABLE IF NOT EXISTS {ApplicationsTable} (
                    Id TEXT PRIMARY KEY,
                    SubmissionId TEXT NOT NULL COLLATE NOCASE,
                    DiscountCode TEXT NOT NULL COLLATE NOCASE,
                    SubmittedAtUtc TEXT NOT NULL,
                    ApplicantName TEXT NULL,
                    DisplayName TEXT NULL,
                    AccountEmail TEXT NULL COLLATE NOCASE,
                    ExpectedAthleteSlug TEXT NULL COLLATE NOCASE,
                    SubmissionKind TEXT NOT NULL,
                    RequestedAmount TEXT NOT NULL,
                    RequestedCurrency TEXT NOT NULL,
                    PaymentRequired INTEGER NOT NULL,
                    InvoiceId TEXT NULL COLLATE NOCASE,
                    CheckoutLink TEXT NULL,
                    InvoiceStatus TEXT NULL,
                    InvoiceAdditionalStatus TEXT NULL,
                    InvoiceAmount TEXT NULL,
                    InvoiceCurrency TEXT NULL,
                    PaidAmount TEXT NULL,
                    PaidAtUtc TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_{ApplicationsTable}_SubmissionId
                    ON {ApplicationsTable}(SubmissionId);

                CREATE INDEX IF NOT EXISTS IX_{ApplicationsTable}_Discount_SubmittedAtUtc
                    ON {ApplicationsTable}(DiscountCode, SubmittedAtUtc);

                CREATE INDEX IF NOT EXISTS IX_{ApplicationsTable}_InvoiceId
                    ON {ApplicationsTable}(InvoiceId);

                CREATE TABLE IF NOT EXISTS {ReportsTable} (
                    DiscountCode TEXT NOT NULL COLLATE NOCASE,
                    PeriodStartUtc TEXT NOT NULL,
                    PeriodEndUtc TEXT NOT NULL,
                    SentAtUtc TEXT NOT NULL,
                    RecipientEmail TEXT NOT NULL,
                    Subject TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    PRIMARY KEY (DiscountCode, PeriodStartUtc)
                );
                """;
            cmd.ExecuteNonQuery();
        });
    }

    private static DiscountSignupReportRow BuildReportRow(
        DiscountSignupStoredApplication record,
        IReadOnlyDictionary<string, AcceptedAthlete> acceptedIndex)
    {
        var accepted = TryFindAcceptedAthlete(record, acceptedIndex);
        var isPaid = record.PaidAtUtc.HasValue || record.PaidAmount.GetValueOrDefault() > 0m;
        var paymentAmount = isPaid
            ? record.PaidAmount ?? record.InvoiceAmount ?? record.RequestedAmount
            : (decimal?)null;
        var paymentCurrency = record.InvoiceCurrency ?? record.RequestedCurrency;

        return new DiscountSignupReportRow(
            record.SubmittedAtUtc,
            record.ApplicantName,
            record.DisplayName,
            record.AccountEmail,
            record.InvoiceId,
            isPaid,
            paymentAmount,
            paymentCurrency,
            accepted is not null,
            accepted?.Slug,
            record.InvoiceStatus,
            record.InvoiceAdditionalStatus,
            record.PaymentRequired);
    }

    private static string BuildReportBody(
        string discountCode,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<DiscountSignupReportRow> rows,
        IReadOnlyList<DiscountSignupPaidTotal> paidTotals)
    {
        var sb = new StringBuilder();
        var paidRows = rows.Where(r => r.IsPaid).ToList();
        var acceptedCount = rows.Count(r => r.Accepted);

        sb.AppendLine("MightyKlaus discount signup report")
            .AppendLine()
            .AppendLine($"Discount code: {discountCode}")
            .AppendLine($"Period: {periodStartUtc:yyyy-MM-dd} through {periodEndUtc.AddTicks(-1):yyyy-MM-dd} UTC")
            .AppendLine($"Generated: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC")
            .AppendLine("Scope: full athlete applications only; result uploads and profile edits are excluded.")
            .AppendLine()
            .AppendLine("Summary")
            .AppendLine($"- Full applications completed: {rows.Count}")
            .AppendLine($"- Paid applications: {paidRows.Count}")
            .AppendLine($"- Total paid: {FormatPaidTotals(paidTotals)}")
            .AppendLine($"- Accepted into leaderboard: {acceptedCount}")
            .AppendLine()
            .AppendLine("Applications");

        if (rows.Count == 0)
        {
            sb.AppendLine("- No full applications were completed with this discount in the report period.");
            return sb.ToString();
        }

        foreach (var row in rows)
        {
            var name = string.IsNullOrWhiteSpace(row.DisplayName)
                ? row.ApplicantName
                : row.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = "unknown applicant";

            var email = string.IsNullOrWhiteSpace(row.AccountEmail) ? "no email" : row.AccountEmail;
            var invoice = string.IsNullOrWhiteSpace(row.InvoiceId) ? "no invoice" : row.InvoiceId;
            var payment = row.IsPaid
                ? $"paid {row.PaymentCurrency} {FormatDecimal(row.PaymentAmountForTotals.GetValueOrDefault())}"
                : row.PaymentRequired
                    ? $"not paid ({FormatInvoiceStatus(row.InvoiceStatus, row.InvoiceAdditionalStatus)})"
                    : "no payment required";
            var accepted = row.Accepted
                ? $"accepted as {row.AcceptedAthleteSlug}"
                : "not accepted yet";

            sb.AppendLine($"- {row.SubmittedAtUtc:yyyy-MM-dd}: {name} <{email}>; {invoice}; {payment}; {accepted}");
        }

        return sb.ToString();
    }

    private static string FormatInvoiceStatus(string? status, string? additionalStatus)
    {
        if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(additionalStatus))
            return "invoice status unknown";
        if (string.IsNullOrWhiteSpace(additionalStatus))
            return status!.Trim();
        if (string.IsNullOrWhiteSpace(status))
            return additionalStatus.Trim();
        return $"{status.Trim()} / {additionalStatus.Trim()}";
    }

    private static string FormatPaidTotals(IReadOnlyList<DiscountSignupPaidTotal> totals)
    {
        if (totals.Count == 0)
            return "none";

        return string.Join(", ", totals.Select(t => $"{t.Currency} {FormatDecimal(t.Amount)}"));
    }

    private static IReadOnlyDictionary<string, AcceptedAthlete> BuildAcceptedAthleteIndex(JsonArray athleteSnapshot)
    {
        var accepted = new Dictionary<string, AcceptedAthlete>(StringComparer.OrdinalIgnoreCase);

        foreach (var athlete in athleteSnapshot.OfType<JsonObject>())
        {
            var slug = GetString(athlete, "AthleteSlug");
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var row = new AcceptedAthlete(slug.Trim(), GetString(athlete, "Name"), GetString(athlete, "DisplayName"));
            AddAcceptedKey(accepted, $"slug:{NormalizeSlug(row.Slug)}", row);
            AddAcceptedKey(accepted, $"name:{NormalizeIdentity(row.Name)}", row);
            AddAcceptedKey(accepted, $"name:{NormalizeIdentity(row.DisplayName)}", row);
        }

        return accepted;
    }

    private static AcceptedAthlete? TryFindAcceptedAthlete(
        DiscountSignupStoredApplication record,
        IReadOnlyDictionary<string, AcceptedAthlete> acceptedIndex)
    {
        var slug = NormalizeSlug(record.ExpectedAthleteSlug);
        if (!string.IsNullOrWhiteSpace(slug) && acceptedIndex.TryGetValue($"slug:{slug}", out var bySlug))
            return bySlug;

        var name = NormalizeIdentity(record.ApplicantName);
        if (!string.IsNullOrWhiteSpace(name) && acceptedIndex.TryGetValue($"name:{name}", out var byName))
            return byName;

        var displayName = NormalizeIdentity(record.DisplayName);
        if (!string.IsNullOrWhiteSpace(displayName) && acceptedIndex.TryGetValue($"name:{displayName}", out var byDisplayName))
            return byDisplayName;

        return null;
    }

    private static void AddAcceptedKey(Dictionary<string, AcceptedAthlete> accepted, string key, AcceptedAthlete row)
    {
        if (string.IsNullOrWhiteSpace(key) || key.EndsWith(':'))
            return;

        accepted.TryAdd(key, row);
    }

    private static string NormalizeSlug(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().Replace('-', '_').ToLowerInvariant();
    }

    private static string NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Trim().ToLowerInvariant();
    }

    private static string? GetString(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node))
            return null;
        return node is null ? null : node.GetValue<string?>();
    }

    private static string NormalizeCurrency(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "USD" : trimmed.ToUpperInvariant();
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, null, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.MinValue;
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParseDateTimeOffset(value);
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string? FormatNullableDecimal(decimal? value)
    {
        return value.HasValue ? FormatDecimal(value.Value) : null;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static void Add(SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private sealed record AcceptedAthlete(string Slug, string? Name, string? DisplayName);
}

public sealed record DiscountSignupApplication(
    string SubmissionId,
    string DiscountCode,
    DateTimeOffset SubmittedAtUtc,
    string? ApplicantName,
    string? DisplayName,
    string? AccountEmail,
    string? ExpectedAthleteSlug,
    string SubmissionKind,
    decimal RequestedAmount,
    string RequestedCurrency,
    bool PaymentRequired,
    string? InvoiceId,
    string? CheckoutLink);

public sealed record DiscountSignupStoredApplication(
    string Id,
    string SubmissionId,
    string DiscountCode,
    DateTimeOffset SubmittedAtUtc,
    string? ApplicantName,
    string? DisplayName,
    string? AccountEmail,
    string? ExpectedAthleteSlug,
    string SubmissionKind,
    decimal RequestedAmount,
    string RequestedCurrency,
    bool PaymentRequired,
    string? InvoiceId,
    string? CheckoutLink,
    string? InvoiceStatus,
    string? InvoiceAdditionalStatus,
    decimal? InvoiceAmount,
    string? InvoiceCurrency,
    decimal? PaidAmount,
    DateTimeOffset? PaidAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DiscountSignupPaidTotal(string Currency, decimal Amount);

public sealed record DiscountSignupReport(
    string Subject,
    string Body,
    int CompletedCount,
    int PaidCount,
    int AcceptedCount,
    IReadOnlyList<DiscountSignupPaidTotal> PaidTotals);

public sealed record DiscountSignupReportResult(
    bool Sent,
    string DiscountCode,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    string? Reason,
    int CompletedCount,
    int PaidCount,
    int AcceptedCount,
    IReadOnlyList<DiscountSignupPaidTotal> PaidTotals)
{
    public static DiscountSignupReportResult NotSent(
        string discountCode,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        string reason)
    {
        return new(false, discountCode, periodStartUtc, periodEndUtc, reason, 0, 0, 0, Array.Empty<DiscountSignupPaidTotal>());
    }
}

internal sealed record DiscountSignupReportRow(
    DateTimeOffset SubmittedAtUtc,
    string? ApplicantName,
    string? DisplayName,
    string? AccountEmail,
    string? InvoiceId,
    bool IsPaid,
    decimal? PaymentAmountForTotals,
    string PaymentCurrency,
    bool Accepted,
    string? AcceptedAthleteSlug,
    string? InvoiceStatus,
    string? InvoiceAdditionalStatus,
    bool PaymentRequired);
