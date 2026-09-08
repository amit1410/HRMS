using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class PasswordResetOtp : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public PasswordRecoveryChannel Channel { get; set; }
    public PasswordRecoveryPurpose Purpose { get; set; } = PasswordRecoveryPurpose.ForgotPassword;
    public string ChallengeIdHash { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public string? DestinationHash { get; set; }
    public string MaskedDestination { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public int ResendCount { get; set; }
    public DateTime LastSentAtUtc { get; set; }
    public Guid? CorrelationId { get; set; }

    public Tenant? Tenant { get; set; }
}
