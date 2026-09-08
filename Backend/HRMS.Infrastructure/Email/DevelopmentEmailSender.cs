using HRMS.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Email;

public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger, bool exposeOtp = true) : IEmailSender
{
    public Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Development welcome invite prepared for {RecipientEmail} in tenant {TenantName}; invite URL is available only in the immediate Development response.",
            message.RecipientEmail, message.TenantName);
        return Task.CompletedTask;
    }

    public Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(exposeOtp ? message.Otp : null);
}
