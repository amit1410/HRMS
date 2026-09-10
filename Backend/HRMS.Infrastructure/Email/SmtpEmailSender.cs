using System.Net;
using System.Net.Mail;
using HRMS.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Email;

/// <summary>Hostinger-ready SMTP adapter. Credentials are read only from configuration.</summary>
public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    private readonly ISmtpEmailTransport transport = new SmtpClientEmailTransport();

    internal SmtpEmailSender(IConfiguration configuration, ISmtpEmailTransport transport)
        : this(configuration)
    {
        this.transport = transport;
    }

    public async Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings(configuration);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromEmail))
            throw new InvalidOperationException("Email SMTP configuration is incomplete.");

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = $"Welcome to {message.TenantName} HRMS",
            Body = $"Hello {message.RecipientFirstName},\n\nSet your password here:\n{message.InviteUrl}\n\nThis link expires at {message.ExpiresAtUtc:O} UTC.\n\nPlease contact your administrator if you need help.",
            IsBodyHtml = false
        };
        mail.To.Add(message.RecipientEmail);
        await transport.SendAsync(settings, mail, cancellationToken);
    }

    public async Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings(configuration);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromEmail)) throw new InvalidOperationException("Email SMTP configuration is incomplete.");
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = $"{message.TenantName} HRMS Password Reset Code",
            Body = $"Hello {message.RecipientFirstName},\n\nYour verification code is: {message.Otp}\n\nThis code expires at {message.ExpiresAtUtc:O} UTC.\n\nIf you did not request a password reset, you can ignore this email.",
            IsBodyHtml = false
        };
        mail.To.Add(message.Destination);
        await transport.SendAsync(settings, mail, cancellationToken);
        return null;
    }

    private static SmtpEmailSettings ReadSettings(IConfiguration configuration) => new(
        configuration["Email:SmtpHost"],
        configuration.GetValue("Email:SmtpPort", 587),
        configuration.GetValue("Email:EnableSsl", true),
        configuration["Email:SmtpUsername"],
        configuration["Email:SmtpPassword"],
        configuration["Email:FromEmail"],
        configuration["Email:FromName"] ?? string.Empty);
}

internal sealed record SmtpEmailSettings(
    string? Host,
    int Port,
    bool EnableSsl,
    string? Username,
    string? Password,
    string? FromEmail,
    string FromName);

internal interface ISmtpEmailTransport
{
    Task SendAsync(SmtpEmailSettings settings, MailMessage mail, CancellationToken cancellationToken);
}

internal sealed class SmtpClientEmailTransport : ISmtpEmailTransport
{
    public async Task SendAsync(SmtpEmailSettings settings, MailMessage mail, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };
        if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
