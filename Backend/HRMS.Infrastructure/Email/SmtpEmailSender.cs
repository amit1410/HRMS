using System.Net;
using System.Net.Mail;
using HRMS.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Email;

/// <summary>Hostinger-ready SMTP adapter. Credentials are read only from configuration.</summary>
public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Email SMTP configuration is incomplete.");

        using var client = new SmtpClient(host, configuration.GetValue("Email:SmtpPort", 587))
        {
            EnableSsl = configuration.GetValue("Email:EnableSsl", true)
        };
        var username = configuration["Email:SmtpUsername"];
        var password = configuration["Email:SmtpPassword"];
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        using var mail = new MailMessage(from, message.RecipientEmail)
        {
            Subject = $"Welcome to {message.TenantName} HRMS",
            Body = $"Hello {message.RecipientFirstName},\n\nSet your password here:\n{message.InviteUrl}\n\nThis link expires at {message.ExpiresAtUtc:O} UTC.\n\nPlease contact your administrator if you need help.",
            IsBodyHtml = false
        };
        await client.SendMailAsync(mail, cancellationToken);
    }

    public async Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:SmtpHost"];
        var from = configuration["Email:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) throw new InvalidOperationException("Email SMTP configuration is incomplete.");
        using var client = new SmtpClient(host, configuration.GetValue("Email:SmtpPort", 587)) { EnableSsl = configuration.GetValue("Email:EnableSsl", true) };
        var username = configuration["Email:SmtpUsername"]; var password = configuration["Email:SmtpPassword"];
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        using var mail = new MailMessage(from, message.Destination) { Subject = $"{message.TenantName} HRMS Password Reset Code", Body = $"Hello {message.RecipientFirstName},\n\nYour verification code is: {message.Otp}\n\nThis code expires at {message.ExpiresAtUtc:O} UTC.\n\nIf you did not request a password reset, you can ignore this email.", IsBodyHtml = false };
        await client.SendMailAsync(mail, cancellationToken);
        return null;
    }
}
