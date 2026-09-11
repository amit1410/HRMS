using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
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
        await SendAsync(settings, mail, cancellationToken);
    }

    public async Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings(configuration);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromEmail))
            throw new InvalidOperationException("Email SMTP configuration is incomplete.");
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false
        };
        mail.To.Add(message.RecipientEmail);
        await SendAsync(settings, mail, cancellationToken);
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
        await SendAsync(settings, mail, cancellationToken);
        return null;
    }

    private async Task SendAsync(SmtpEmailSettings settings, MailMessage mail, CancellationToken cancellationToken)
    {
        try
        {
            await transport.SendAsync(settings, mail, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailDeliveryException(EmailDeliveryFailureKind.Timeout, "SMTP delivery timed out.", settings.Host, settings.Port, exception);
        }
        catch (SmtpException exception)
        {
            var kind = HasTimeoutFailure(exception) ? EmailDeliveryFailureKind.Timeout : HasNetworkFailure(exception) ? EmailDeliveryFailureKind.Network : IsAuthenticationFailure(exception) ? EmailDeliveryFailureKind.Authentication : EmailDeliveryFailureKind.Server;
            var message = kind switch
            {
                EmailDeliveryFailureKind.Authentication => "SMTP authentication failed.",
                EmailDeliveryFailureKind.Network => "SMTP delivery failed due to a network error.",
                _ => "SMTP server rejected or could not complete delivery."
            };
            throw new EmailDeliveryException(kind, message, settings.Host, settings.Port, exception);
        }
        catch (SocketException exception)
        {
            throw new EmailDeliveryException(EmailDeliveryFailureKind.Network, "SMTP delivery failed due to a network error.", settings.Host, settings.Port, exception);
        }
        catch (TimeoutException exception)
        {
            throw new EmailDeliveryException(EmailDeliveryFailureKind.Timeout, "SMTP delivery timed out.", settings.Host, settings.Port, exception);
        }
    }

    private static bool IsAuthenticationFailure(SmtpException exception)
    {
        var status = (int)exception.StatusCode;
        return status is 530 or 534 or 535 or 538
            || exception.ToString().Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || exception.ToString().Contains("5.7.8", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTimeoutFailure(Exception exception) => exception switch
    {
        WebException { Status: WebExceptionStatus.Timeout } => true,
        _ when exception.InnerException is not null => HasTimeoutFailure(exception.InnerException),
        _ => false
    };

    private static bool HasNetworkFailure(Exception exception) => exception switch
    {
        SocketException => true,
        IOException => true,
        WebException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(HasNetworkFailure),
        _ when exception.InnerException is not null => HasNetworkFailure(exception.InnerException),
        _ => false
    };

    private static SmtpEmailSettings ReadSettings(IConfiguration configuration) => new(
        configuration["Email:Smtp:Host"] ?? configuration["Email:SmtpHost"],
        configuration.GetValue("Email:Smtp:Port", configuration.GetValue("Email:SmtpPort", 587)),
        configuration.GetValue("Email:Smtp:EnableSsl", configuration.GetValue("Email:EnableSsl", true)),
        configuration["Email:Smtp:Username"] ?? configuration["Email:SmtpUsername"],
        configuration["Email:Smtp:Password"] ?? configuration["Email:SmtpPassword"],
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
