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
    Task SendPlatformUsernameReminderAsync(PlatformUsernameReminderMessage message, CancellationToken cancellationToken = default);
    Task SendPlatformPasswordResetAsync(PlatformPasswordResetEmailMessage message, CancellationToken cancellationToken = default);
    Task SendPlatformPasswordChangedAsync(PlatformPasswordChangedEmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record LeaveNotificationEmailMessage(string RecipientEmail, string Subject, string Body);
public sealed record PlatformUsernameReminderMessage(string RecipientEmail, string DisplayName, string Username);
public sealed record PlatformPasswordResetEmailMessage(string RecipientEmail, string DisplayName, string ResetUrl, DateTime ExpiresAtUtc);
public sealed record PlatformPasswordChangedEmailMessage(string RecipientEmail, string DisplayName, DateTime ChangedAtUtc);
