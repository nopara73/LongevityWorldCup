using System.Net;
using System.Text;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationPaymentEmailSenderTests
{
    [Fact]
    public async Task DisconnectFailureAfterAcknowledgement_DoesNotCauseRetry()
    {
        var smtp = new FakeSmtpClient { DisconnectFailure = new IOException("QUIT response lost") };
        var marked = false;
        await Sender(smtp).SendAsync(Config(), Payment(), () => marked = true, CancellationToken.None);
        Assert.True(marked);
        Assert.Equal(1, smtp.SendCalls);
    }

    [Fact]
    public async Task DefiniteSmtpRejection_IsDistinguishableFromUnknownDelivery()
    {
        var rejection = new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)451, "Temporarily unavailable");
        var smtp = new FakeSmtpClient { SendFailure = rejection };
        var ex = await Assert.ThrowsAsync<PaymentEmailNotAcceptedException>(() =>
            Sender(smtp).SendAsync(Config(), Payment(), () => { }, CancellationToken.None));
        Assert.Same(rejection, ex.InnerException);

        var unknown = new IOException("DATA acknowledgement lost");
        smtp = new FakeSmtpClient { SendFailure = unknown };
        var actual = await Assert.ThrowsAsync<IOException>(() =>
            Sender(smtp).SendAsync(Config(), Payment(), () => { }, CancellationToken.None));
        Assert.Same(unknown, actual);
    }

    [Fact]
    public async Task ConnectionFailure_DoesNotRecordDispatchIntent()
    {
        var smtp = new FakeSmtpClient { ConnectFailure = new IOException("Connection unavailable") };
        var marked = false;
        await Assert.ThrowsAsync<IOException>(() => Sender(smtp).SendAsync(Config(), Payment(), () => marked = true, CancellationToken.None));
        Assert.False(marked);
        Assert.Equal(0, smtp.SendCalls);
    }

    [Fact]
    public async Task FailedDurableDispatchMarker_PreventsTransmission()
    {
        var smtp = new FakeSmtpClient();
        await Assert.ThrowsAsync<IOException>(() => Sender(smtp).SendAsync(Config(), Payment(),
            () => throw new IOException("Could not save outbox state"), CancellationToken.None));
        Assert.Equal(0, smtp.SendCalls);
    }

    private static SmtpApplicationPaymentEmailSender Sender(FakeSmtpClient smtp)
        => new(NullLogger<SmtpApplicationPaymentEmailSender>.Instance, () => smtp);

    private static Config Config() => new()
    {
        SmtpServer = "smtp.example.test", SmtpPort = 587, SmtpUser = "sender@example.test", SmtpPassword = "test-only",
        EmailFrom = "sender@example.test", EmailTo = "reviewer@example.test"
    };

    private static ApplicationPayment Payment() => new("order-1", "invoice-1", "https://pay.example.test", "store",
        "Applicant Ada", "athlete@example.test", "application", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        PaidInvoice: BtcpayInvoiceClient.ParseInvoiceJson("""{"status":"Settled","amount":"100","currency":"USD"}"""));

    private sealed class FakeSmtpClient : SmtpClient
    {
        public Exception? ConnectFailure;
        public Exception? SendFailure;
        public Exception? DisconnectFailure;
        public int SendCalls;

        public override Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
            => ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);

        public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress? progress = null)
        {
            SendCalls++;
            return SendFailure is null ? Task.FromResult("250 OK") : Task.FromException<string>(SendFailure);
        }

        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
            => DisconnectFailure is null ? Task.CompletedTask : Task.FromException(DisconnectFailure);
    }
}
