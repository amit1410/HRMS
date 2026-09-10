namespace HRMS.Application.Abstractions;

public sealed record WelcomeEmailMessage(
    string RecipientEmail,
    string RecipientFirstName,
    string TenantName,
    string InviteUrl,
    DateTime ExpiresAtUtc);

public interface IEmailSender
{
    Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default);
    Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default);
    Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default);
}

public sealed record LeaveNotificationEmailMessage(string RecipientEmail, string Subject, string Body);
