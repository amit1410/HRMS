using System.Net.Mail;
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
            ["Email:FromEmail"] = "no-reply@anevratechnologies.com",
            ["Email:FromName"] = "Anevra Technologies"
        }).Build();
        var sender = new SmtpEmailSender(configuration, transport);

        await sender.SendPasswordResetOtpAsync(new OtpDeliveryMessage(
            "employee@example.test", "123456", "Tenant", "Person", DateTime.UtcNow, HRMS.Domain.Enums.PasswordRecoveryChannel.Email));

        var mail = Assert.IsType<MailMessage>(transport.Mail);
        Assert.Equal("no-reply@anevratechnologies.com", mail.From.Address);
        Assert.Equal("Anevra Technologies", mail.From.DisplayName);
        Assert.Equal("amit@anevratechnologies.com", transport.Username);
        Assert.Equal("mailbox-password", transport.Password);
        Assert.Equal("smtp.hostinger.com", transport.Host);
        Assert.Equal(587, transport.Port);
        Assert.True(transport.EnableSsl);
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
