namespace HRMS.Application.Abstractions;

public sealed record WelcomeEmailMessage(
    string RecipientEmail,
    string RecipientFirstName,
    string TenantName,
    string InviteUrl,
    DateTime ExpiresAtUtc);

public interface IEmailSender
{
    Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default);
    Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default);
}
