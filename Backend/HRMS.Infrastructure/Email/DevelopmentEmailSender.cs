using HRMS.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Email;

public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger, bool exposeOtp = true) : IEmailSender
{
    public Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "Welcome invitation delivery is disabled because the Development email provider does not send mail. Tenant {TenantName}.",
            message.TenantName);
        throw new EmailDeliveryException(
            EmailDeliveryFailureKind.Server,
            "Welcome invitation delivery is disabled for the Development email provider.",
            null,
            0,
            new InvalidOperationException("Development email provider does not deliver welcome invitations."));
    }

    public Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(exposeOtp ? message.Otp : null);
    public Task SendPlatformUsernameReminderAsync(PlatformUsernameReminderMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendPlatformPasswordResetAsync(PlatformPasswordResetEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendPlatformPasswordChangedAsync(PlatformPasswordChangedEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
