using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

// Both invoice creation intent and the notification outbox live in the existing, backed-up database.
// Nothing is imported from old submissions or the provider's invoice history.
public sealed class ApplicationPaymentStore
{
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly DatabaseManager _database;

    public ApplicationPaymentStore(DatabaseManager database)
    {
        _database = database;
        Write(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ApplicationPayments (
                    OrderId TEXT PRIMARY KEY,
                    InvoiceId TEXT UNIQUE,
                    State TEXT NOT NULL,
                    NextAttemptUtc INTEGER NOT NULL,
                    LeaseToken TEXT,
                    LeaseUntilUtc INTEGER,
                    Data TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ApplicationPayments_Due
                    ON ApplicationPayments (State, NextAttemptUtc);
                """;
            command.ExecuteNonQuery();
        });
    }

    public void Register(Config config, BtcpayInvoiceCreateRequest request, DateTimeOffset now)
    {
        var payment = new ApplicationPayment(
            request.OrderId, null, config.BTCPayBaseUrl!.TrimEnd('/'), config.BTCPayStoreId!,
            request.BuyerName, request.BuyerEmail, request.Metadata["submissionType"]?.ToString(),
            now, now.AddMinutes(1), SubmissionId: request.Metadata.GetValueOrDefault("submissionId")?.ToString());
        Write(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                INSERT INTO ApplicationPayments (OrderId, State, NextAttemptUtc, Data)
                VALUES ($order, $state, $next, $data);
                """;
            BindPayment(command, payment);
            command.ExecuteNonQuery();
        });
    }

    public void AttachInvoice(string orderId, string invoiceId)
    {
        // May race recovery by orderId after a slow provider response. Never replace a different invoice.
        Write(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                UPDATE ApplicationPayments
                SET InvoiceId = $invoice, Data = json_set(Data, '$.InvoiceId', $invoice)
                WHERE OrderId = $order AND (InvoiceId IS NULL OR InvoiceId = $invoice);
                """;
            command.Parameters.AddWithValue("$order", orderId);
            command.Parameters.AddWithValue("$invoice", invoiceId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The payment intent is missing or has a different invoice.");
        });
    }

    public ApplicationPayment? GetByInvoice(string invoiceId) => Read("InvoiceId", invoiceId);
    internal ApplicationPayment? GetByOrder(string orderId) => Read("OrderId", orderId);

    internal static async Task<bool> WasLegacyNotificationSentAsync(string contentRoot, string invoiceId, CancellationToken ct)
    {
        var path = Path.Combine(contentRoot, "AppData", "paid-invoice-email-sent.txt");
        return File.Exists(path) && (await File.ReadAllLinesAsync(path, ct))
            .Any(id => string.Equals(id.Trim(), invoiceId, StringComparison.OrdinalIgnoreCase));
    }

    private ApplicationPayment? Read(string column, string value) => _database.Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT Data FROM ApplicationPayments WHERE {column} = $value;";
        command.Parameters.AddWithValue("$value", value);
        return command.ExecuteScalar() is string json ? Deserialize(json) : null;
    });

    internal IReadOnlyList<string> GetDueOrders(DateTimeOffset now) => _database.Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT OrderId FROM ApplicationPayments
            WHERE State IN ('Pending', 'Sending') AND NextAttemptUtc <= $now
                AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= $now)
            ORDER BY NextAttemptUtc, OrderId LIMIT 25;
            """;
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        var orders = new List<string>();
        while (reader.Read()) orders.Add(reader.GetString(0));
        return (IReadOnlyList<string>)orders;
    });

    internal ApplicationPaymentLease? TryClaim(string orderId, DateTimeOffset now) => Write(db =>
    {
        using var command = db.CreateCommand();
        var token = Guid.NewGuid().ToString("N");
        command.CommandText = """
            UPDATE ApplicationPayments SET LeaseToken = $token, LeaseUntilUtc = $until
            WHERE OrderId = $order AND State IN ('Pending', 'Sending') AND NextAttemptUtc <= $now
                AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= $now)
            RETURNING Data;
            """;
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$until", now.Add(LeaseDuration).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$order", orderId);
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        return command.ExecuteScalar() is string json
            ? new ApplicationPaymentLease(token, Deserialize(json)) : null;
    });

    internal void Save(ApplicationPaymentLease lease, ApplicationPayment payment, DateTimeOffset now, bool release = true)
        => Write(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                UPDATE ApplicationPayments SET InvoiceId = COALESCE(InvoiceId, $invoice), State = $state,
                    NextAttemptUtc = $next, Data = json_set($data, '$.InvoiceId', COALESCE(InvoiceId, $invoice)),
                    LeaseToken = CASE WHEN $release THEN NULL ELSE LeaseToken END,
                    LeaseUntilUtc = CASE WHEN $release THEN NULL ELSE LeaseUntilUtc END
                WHERE OrderId = $order AND LeaseToken = $token AND LeaseUntilUtc > $now
                    AND (InvoiceId IS NULL OR $invoice IS NULL OR InvoiceId = $invoice);
                """;
            BindPayment(command, payment);
            command.Parameters.AddWithValue("$invoice", (object?)payment.InvoiceId ?? DBNull.Value);
            command.Parameters.AddWithValue("$release", release);
            command.Parameters.AddWithValue("$token", lease.Token);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Application payment lease expired or changed; refusing to dispatch or overwrite it.");
        });

    private void Write(Action<SqliteConnection> work) => Write(db => { work(db); return 0; });

    private T Write<T>(Func<SqliteConnection, T> work) => _database.Run(db =>
    {
        // NORMAL WAL commits can disappear on power loss. Flush payment/dispatch state before
        // exposing checkout or contacting SMTP, without changing other services' write policy.
        using var pragma = db.CreateCommand();
        pragma.CommandText = "PRAGMA synchronous;";
        var previous = Convert.ToInt32(pragma.ExecuteScalar());
        pragma.CommandText = "PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        try { return work(db); }
        finally
        {
            pragma.CommandText = $"PRAGMA synchronous={previous};";
            pragma.ExecuteNonQuery();
        }
    });

    private static void BindPayment(SqliteCommand command, ApplicationPayment payment)
    {
        command.Parameters.AddWithValue("$order", payment.OrderId);
        command.Parameters.AddWithValue("$state", payment.State);
        command.Parameters.AddWithValue("$next", payment.NextAttemptUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(payment));
    }

    private static ApplicationPayment Deserialize(string json) => JsonSerializer.Deserialize<ApplicationPayment>(json)
        ?? throw new InvalidDataException("Invalid application payment record.");
}

public sealed record ApplicationPayment(
    string OrderId,
    string? InvoiceId,
    string ProviderBaseUrl,
    string StoreId,
    string? ApplicantName,
    string? AccountEmail,
    string? SubmissionType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset NextAttemptUtc,
    string State = "Pending",
    BtcpayInvoiceLookupResult? PaidInvoice = null,
    int Failures = 0,
    string? LastError = null,
    DateTimeOffset? SentAtUtc = null,
    string? SubmissionId = null,
    DateTimeOffset? PaymentObservedAtUtc = null)
{
    public string MessageId => $"lwc-payment-{OrderId}@longevityworldcup.com";
}

internal sealed record ApplicationPaymentLease(string Token, ApplicationPayment Payment);
