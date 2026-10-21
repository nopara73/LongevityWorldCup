using System.Net;
using System.Text;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection("SMTP environment")]
public sealed class LongevitymaxxingEmailSenderTests
{
    [Theory]
    [InlineData("confirmation")]
    [InlineData("access")]
    [InlineData("daily")]
    [InlineData("start")]
    public async Task AcceptedMessage_WithFailedQuit_CompletesAndDisposesMultipartMessage(string kind)
    {
        var smtp = new EmailTestSmtpClient { DisconnectFailure = new IOException("QUIT response lost") };
        var logger = new EmailTestLogger<SmtpLongevitymaxxingEmailSender>();
        var sender = new SmtpLongevitymaxxingEmailSender(Config(), logger, () => smtp);

        await SendAsync(sender, kind);

        Assert.Equal(1, smtp.SendCalls);
        Assert.Equal(1, smtp.DisconnectCalls);
        Assert.True(smtp.Disposed);
        Assert.Equal(2, smtp.ContentStreams.Count);
        Assert.All(smtp.ContentStreams, stream => Assert.False(stream.CanRead));
        using var message = MimeMessage.Load(new MemoryStream(smtp.MessageBytes!));
        Assert.NotNull(message.Body);
        Assert.Equal("multipart/alternative", message.Body.ContentType.MimeType);
        var alternatives = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal("text/plain", alternatives[0].ContentType.MimeType);
        Assert.Equal("text/html", alternatives[1].ContentType.MimeType);
        Assert.False(string.IsNullOrWhiteSpace(message.TextBody));
        Assert.StartsWith("<!DOCTYPE html>", message.HtmlBody);
        Assert.Equal("athlete@example.test", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("sender@example.test", Assert.Single(message.From.Mailboxes).Address);
        Assert.Equal(30_000, smtp.Timeout);
        Assert.Contains(logger.Messages, message => message.Contains("disconnect", StringComparison.OrdinalIgnoreCase));
        Assert.All(logger.Messages, message => Assert.DoesNotContain("athlete@example.test", message));
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("authenticate")]
    [InlineData("reject")]
    [InlineData("unknown")]
    [InlineData("cancel")]
    public async Task FailureBeforeAcknowledgement_PropagatesWithoutDisconnectOrSuccessLog(string stage)
    {
        Exception failure = stage switch
        {
            "reject" => new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, (SmtpStatusCode)451, "Rejected"),
            "cancel" => new OperationCanceledException(),
            _ => new IOException("No DATA acknowledgement")
        };
        var smtp = new EmailTestSmtpClient
        {
            ConnectFailure = stage == "connect" ? failure : null,
            AuthenticateFailure = stage == "authenticate" ? failure : null,
            SendFailure = stage is "reject" or "unknown" or "cancel" ? failure : null
        };
        var logger = new EmailTestLogger<SmtpLongevitymaxxingEmailSender>();
        var sender = new SmtpLongevitymaxxingEmailSender(Config(), logger, () => smtp);

        var actual = await Record.ExceptionAsync(() => SendAsync(sender, "daily"));

        Assert.Same(failure, actual);
        Assert.Equal(0, smtp.DisconnectCalls);
        Assert.True(smtp.Disposed);
        Assert.Empty(logger.Messages);
        Assert.All(smtp.ContentStreams, stream => Assert.False(stream.CanRead));
    }

    [Fact]
    public async Task CancellationDuringQuit_DoesNotUndoAcceptanceOrLogRecipientData()
    {
        using var cancellation = new CancellationTokenSource();
        var smtp = new EmailTestSmtpClient
        {
            AfterSend = cancellation.Cancel,
            DisconnectFailure = new OperationCanceledException("private SMTP response: athlete@example.test")
        };
        var logger = new EmailTestLogger<SmtpLongevitymaxxingEmailSender>();
        var sender = new SmtpLongevitymaxxingEmailSender(Config(), logger, () => smtp);

        await sender.SendConfirmationAsync("athlete@example.test", "Ada", "https://example.test/confirm?token=private", cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, smtp.SendCalls);
        Assert.All(logger.Messages, message =>
        {
            Assert.DoesNotContain("athlete@example.test", message);
            Assert.DoesNotContain("private", message);
        });
    }

    private static Task SendAsync(SmtpLongevitymaxxingEmailSender sender, string kind) => kind switch
    {
        "confirmation" => sender.SendConfirmationAsync("athlete@example.test", "Ada", "https://example.test/confirm?token=confirm"),
        "access" => sender.SendAccessLinkAsync("athlete@example.test", "Ada", "https://example.test/?token=access"),
        "daily" => sender.SendDailyReminderAsync(TransactionalEmailFamilyTests.Reminder() with { Email = "athlete@example.test" },
            "https://example.test/?token=access", TransactionalEmailFamilyTests.StopUrl),
        "start" => sender.SendChallengeStartAsync(new("participant", "athlete@example.test", "Ada", "UTC", "access", "stop"),
            "https://example.test/?token=access", TransactionalEmailFamilyTests.StopUrl),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static Config Config() => new()
    {
        SmtpServer = "smtp.example.test", SmtpPort = 587, SmtpUser = "sender@example.test", SmtpPassword = "test-only",
        EmailFrom = "sender@example.test"
    };

    [Fact]
    public async Task DiscountReport_AcceptanceSurvivesQuitFailureAndDisposesPlainMessage()
    {
        var smtp = new EmailTestSmtpClient { DisconnectFailure = new IOException("QUIT response lost") };
        var sender = new SmtpDiscountSignupReportEmailSender(new EmailTestLogger<SmtpDiscountSignupReportEmailSender>(), () => smtp);

        await sender.SendAsync(Config(), ["reviewer@example.test"], "Report", "Exact report body");

        Assert.Equal(1, smtp.SendCalls);
        Assert.True(smtp.Disposed);
        Assert.All(smtp.ContentStreams, stream => Assert.False(stream.CanRead));
        using var message = MimeMessage.Load(new MemoryStream(smtp.MessageBytes!));
        Assert.Equal("Report", message.Subject);
        var plain = Assert.IsType<TextPart>(message.Body);
        using var decoded = new MemoryStream();
        await plain.Content!.DecodeToAsync(decoded);
        Assert.Equal("Exact report body\r\n", Encoding.UTF8.GetString(decoded.ToArray()));
        // MimeKit's TextBody getter uses the native display newline after decoding.
        Assert.Equal("Exact report body" + Environment.NewLine, message.TextBody);
        Assert.Null(message.HtmlBody);
    }
}

internal sealed class EmailTestSmtpClient : SmtpClient
{
    internal Exception? ConnectFailure;
    internal Exception? AuthenticateFailure;
    internal Exception? SendFailure;
    internal Exception? DisconnectFailure;
    internal Action? AfterSend;
    internal int SendCalls;
    internal int DisconnectCalls;
    internal bool Disposed;
    internal byte[]? MessageBytes;
    internal List<Stream> ContentStreams = [];

    public override Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
        => ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);

    public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default)
        => AuthenticateFailure is null ? Task.CompletedTask : Task.FromException(AuthenticateFailure);

    public override async Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress? progress = null)
    {
        SendCalls++;
        ContentStreams = message.BodyParts.OfType<TextPart>().Select(part => part.Content!.Stream!).ToList();
        using var buffer = new MemoryStream();
        // Match SMTP's CRLF framing regardless of host/display defaults.
        var wireFormat = options.Clone();
        wireFormat.NewLineFormat = NewLineFormat.Dos;
        wireFormat.EnsureNewLine = true;
        await message.WriteToAsync(wireFormat, buffer, cancellationToken);
        MessageBytes = buffer.ToArray();
        if (SendFailure is not null) throw SendFailure;
        AfterSend?.Invoke();
        return "250 Message accepted for delivery";
    }

    public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
    {
        DisconnectCalls++;
        return DisconnectFailure is null ? Task.CompletedTask : Task.FromException(DisconnectFailure);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class EmailTestLogger<T> : ILogger<T>
{
    internal List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception) + exception);
}
