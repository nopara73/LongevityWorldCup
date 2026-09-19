# Application payment reconciliation

Payment tracking begins with a durable order record before BTCPay invoice creation. A successful response attaches the invoice before checkout is returned to the applicant. If the response is lost, the worker recovers only that exact order with BTCPay's authenticated `orderId` query. Multiple or mismatched matches fail closed. The original store and provider URL must still match configuration. Payment writes use SQLite `synchronous=FULL` under the database gate and restore the previous setting afterward, so dispatch intent is flushed before the external side effect without changing other services' write policy.

The worker scans at startup and every minute, processing up to 25 due orders per pass. It observes new invoices every minute for the first day, hourly through day 25, then daily, including expired/invalid invoices that may be paid or settled manually later. Provider and definite mail failures retry with exponential delay capped at one hour. External operations each have a 30-second cancellation deadline. Two-minute database leases and conditional updates prevent concurrent workers from dispatching the same notification. No database lock is held during network operations.

Paid evidence is saved before mail dispatch, so subsequent mail retries work during provider outages. New applications and paid Amateur-to-Pro result submissions share this path. Free applications, ordinary result updates, and profile edits do not create payment work. Email subject, body, internal recipient, and Reply-To behavior are preserved. The review endpoint still returns the existing status contract, but never sends or registers notification work from browser-supplied identity fields.

## Delivery states

- `Pending`: awaiting invoice discovery/payment, or retrying a definite provider/mail failure. `Data.PaidInvoice` retains the first paid observation.
- `Sending`: durable dispatch intent, written after SMTP connection/authentication and immediately before transmission.
- `Sent`: SMTP acknowledged the message, or the preserved legacy sent-marker file already contained the invoice.
- `Uncertain`: the process stopped during dispatch, or SMTP acceptance could not be established/recorded. The worker logs an error containing the invoice and stable Message-ID and does not resend automatically.

SMTP cannot atomically commit an email and a local SQLite record. Message-ID alone does not guarantee recipient deduplication. A timeout/disconnect during DATA or a process crash after acceptance can therefore require operator reconciliation. A connection/authentication failure or explicit SMTP rejection retries automatically; a failed QUIT after acknowledged DATA counts as sent. Payment remains durably recorded even if its notification is uncertain.

## Deployment and verification

Deploy through the normal workflow. Preserve/back up the existing database and `publish/AppData/paid-invoice-email-sent.txt`; the table is created additively. Production defaults to reconciliation enabled; verify it has not been overridden. `EnableApplicationPaymentReconciliation` is an ASP.NET configuration setting (environment variable or `appsettings.json`, not private `config.json`). The existing BTCPay key needs `btcpay.store.cancreateinvoice` and `btcpay.store.canviewinvoices`. Existing SMTP settings are reused. Tests disable the hosted worker and use fake provider/mail dependencies with isolated databases.

Startup does **not** scan historical invoice lists, import `ApplicationSubmissionResponses`, or replay legacy notifications. Only orders registered by this implementation enter the queue. Any pre-deployment omissions need separately approved recovery. Do not change legacy sent markers to force a replay. If rolling back to a binary that sends directly from browser callbacks, disable that old notification path or reconcile its sent-marker file first; the old binary cannot consult this outbox.

Inspect production read-only (do not print the full JSON payload, which contains applicant contact details):

```sh
sqlite3 'file:/var/www/.longevityworldcup/LongevityWorldCup.db?mode=ro' \
  "PRAGMA query_only=ON; SELECT OrderId, InvoiceId, State, datetime(NextAttemptUtc / 1000, 'unixepoch') AS NextAttemptUtc, json_extract(Data, '$.Failures') AS Failures, json_extract(Data, '$.LastError') AS LastError FROM ApplicationPayments ORDER BY NextAttemptUtc;"
```

After an authorized new test payment, verify the row reaches `Sent`, the exact Message-ID exists in the internal mailbox, and provider/payment identity matches. Closing the checkout browser must not affect this. Check service logs for repeated retries or `needs reconciliation`. Historical paid applicants must be checked against provider evidence even when their mail confirmation is absent.

## Resolving uncertain delivery

Inspect the sender's Sent mail and internal recipient mailbox for `rfc822msgid:lwc-payment-{OrderId}@longevityworldcup.com` and compare invoice ID and body. If found, an authorized repair can mark the outbox row `Sent`. If absent, absence from search alone is not proof of nondelivery: verify SMTP/server evidence before approving a resend. Stop the worker while repairing a row, back up the database, update both indexed `State` and JSON `Data.State` in one transaction, clear its lease, and preserve the Message-ID. To authorize retry, also set both `NextAttemptUtc` fields to a due time. Restart and verify the resulting row and mailbox. Never automatically reset `Sending`/`Uncertain` to `Pending` on restart or deployment.

References: [BTCPay invoice API](https://docs.btcpayserver.org/API/Greenfield/v1/#tag/Invoices), [MailKit send contract](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_SendAsync_1.htm).
