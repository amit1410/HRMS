using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed record OtpDeliveryMessage(
    string Destination,
    string Otp,
    string TenantName,
    string RecipientFirstName,
    DateTime ExpiresAtUtc,
    PasswordRecoveryChannel Channel);

public interface ISmsOtpSender
{
    Task<string?> SendAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default);
}
