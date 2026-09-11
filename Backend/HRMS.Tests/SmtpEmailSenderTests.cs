using System.Net.Mail;
using System.Net.Sockets;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;

namespace HRMS.Tests;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task Password_reset_uses_alias_from_name_and_separate_smtp_credentials()
    {
        var transport = new RecordingTransport();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = "smtp.hostinger.com",
            ["Email:SmtpPort"] = "587",
            ["Email:EnableSsl"] = "true",
            ["Email:SmtpUsername"] = "amit@anevratechnologies.com",
            ["Email:SmtpPassword"] = "mailbox-password",
            ["Email:FromEmail"] = "amit@anevratechnologies.com",
            ["Email:FromName"] = "Anevra Technologies"
        }).Build();
        var sender = new SmtpEmailSender(configuration, transport);

        await sender.SendPasswordResetOtpAsync(new OtpDeliveryMessage(
            "employee@example.test", "123456", "Tenant", "Person", DateTime.UtcNow, HRMS.Domain.Enums.PasswordRecoveryChannel.Email));

        var mail = Assert.IsType<MailMessage>(transport.Mail);
        Assert.Equal("amit@anevratechnologies.com", mail.From.Address);
        Assert.Equal("Anevra Technologies", mail.From.DisplayName);
        Assert.Equal("amit@anevratechnologies.com", transport.Username);
        Assert.Equal("mailbox-password", transport.Password);
        Assert.Equal("smtp.hostinger.com", transport.Host);
        Assert.Equal(587, transport.Port);
        Assert.True(transport.EnableSsl);
    }

    [Fact]
    public async Task Non_request_cancellation_is_classified_as_a_timeout()
    {
        var sender = CreateSender(new ThrowingTransport(new OperationCanceledException("SMTP timeout")));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendPasswordResetOtpAsync(Message()));

        Assert.Equal(EmailDeliveryFailureKind.Timeout, exception.Kind);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sender = CreateSender(new ThrowingTransport(new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendPasswordResetOtpAsync(Message(), cancellation.Token));
    }

    [Fact]
    public async Task SMTP_authentication_failure_is_classified_separately()
    {
        var sender = CreateSender(new ThrowingTransport(new SmtpException((SmtpStatusCode)535, "authentication failed")));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendPasswordResetOtpAsync(Message()));

        Assert.Equal(EmailDeliveryFailureKind.Authentication, exception.Kind);
    }

    [Fact]
    public async Task Network_and_server_failures_are_classified_separately()
    {
        var networkSender = CreateSender(new ThrowingTransport(new SmtpException("network", new SocketException((int)SocketError.ConnectionRefused))));
        var serverSender = CreateSender(new ThrowingTransport(new SmtpException("server rejected")));

        var network = await Assert.ThrowsAsync<EmailDeliveryException>(() => networkSender.SendPasswordResetOtpAsync(Message()));
        var server = await Assert.ThrowsAsync<EmailDeliveryException>(() => serverSender.SendPasswordResetOtpAsync(Message()));

        Assert.Equal(EmailDeliveryFailureKind.Network, network.Kind);
        Assert.Equal(EmailDeliveryFailureKind.Server, server.Kind);
    }

    private static SmtpEmailSender CreateSender(ISmtpEmailTransport transport) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Email:SmtpHost"] = "smtp.example.test",
        ["Email:SmtpPort"] = "587",
        ["Email:SmtpUsername"] = "user",
        ["Email:SmtpPassword"] = "password",
        ["Email:FromEmail"] = "from@example.test",
        ["Email:FromName"] = "HRMS"
    }).Build(), transport);

    private static OtpDeliveryMessage Message() => new("employee@example.test", "123456", "Tenant", "Person", DateTime.UtcNow, HRMS.Domain.Enums.PasswordRecoveryChannel.Email);

    private sealed class ThrowingTransport(Exception exception) : ISmtpEmailTransport
    {
        public Task SendAsync(SmtpEmailSettings settings, MailMessage mail, CancellationToken cancellationToken) => Task.FromException(exception);
    }

    private sealed class RecordingTransport : ISmtpEmailTransport
    {
        public MailMessage? Mail { get; private set; }
        public string? Host { get; private set; }
        public int Port { get; private set; }
        public bool EnableSsl { get; private set; }
        public string? Username { get; private set; }
        public string? Password { get; private set; }

        public Task SendAsync(SmtpEmailSettings settings, MailMessage mail, CancellationToken cancellationToken)
        {
            Mail = mail;
            Host = settings.Host;
            Port = settings.Port;
            EnableSsl = settings.EnableSsl;
            Username = settings.Username;
            Password = settings.Password;
            return Task.CompletedTask;
        }
    }
}
