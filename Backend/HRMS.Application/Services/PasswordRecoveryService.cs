using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Tenants;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class PasswordRecoveryService(
    IHrmsDbContext db,
    ITenantContext tenantContext,
    ITenantBrandingService branding,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    ISmsOtpSender smsSender,
    TimeProvider clock,
    IShardContext shardContext,
    ILogger<PasswordRecoveryService>? logger = null) : IPasswordRecoveryService
{
    private const string Generic = "If the account is eligible for password recovery, you can continue with the available verification method.";
    private const string SendGeneric = "If the account is eligible and the selected method is available, a verification code has been sent.";
    private const string InvalidCode = "Invalid or expired verification code.";

    public async Task<Result<RecoveryChallengeDto>> IdentifyAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var settings = await branding.GetRecoverySettingsAsync(ct);
        var challenge = Token();
        if (!settings.Succeeded || settings.Value is null || !settings.Value.PasswordRecoveryEnabled)
            return Result<RecoveryChallengeDto>.Success(new(challenge, [], Generic));

        // Channel choices are configuration-derived only. Returning account-specific availability or
        // masked destinations here would allow anonymous callers to enumerate users.
        var channels = new List<RecoveryChannelDto>();
        if (settings.Value.AllowEmailOtp) channels.Add(new(PasswordRecoveryChannel.Email, string.Empty));
        if (settings.Value.AllowSmsOtp) channels.Add(new(PasswordRecoveryChannel.Sms, string.Empty));
        var user = await ResolveUserAsync(request.Identifier, settings.Value, ct);
        if (user is null || !user.IsActive || channels.Count == 0)
            return Result<RecoveryChallengeDto>.Success(new(challenge, channels, Generic));
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var option in channels)
        {
            var destination = option.Channel == PasswordRecoveryChannel.Email
                ? user.Email
                : await TrustedMobileAsync(user.Id, user.TenantId, ct);
            db.PasswordResetOtps.Add(new PasswordResetOtp
            {
                Id = Guid.NewGuid(), TenantId = user.TenantId, UserId = user.Id, Channel = option.Channel,
                ChallengeIdHash = Hash(challenge), OtpHash = hasher.Hash(Token()),
                DestinationHash = string.IsNullOrWhiteSpace(destination) ? null : Hash(destination),
                MaskedDestination = string.Empty, ExpiresAtUtc = now, LastSentAtUtc = now,
                CorrelationId = Guid.NewGuid()
            });
        }
        await db.SaveChangesAsync(ct);
        return Result<RecoveryChallengeDto>.Success(new(challenge, channels, Generic));
    }

    public Task<Result<RecoverySendOtpDto>> SendOtpAsync(SendRecoveryOtpRequest request, CancellationToken ct = default) => SendCoreAsync(request.ChallengeId, request.Channel, false, ct);
    public Task<Result<RecoverySendOtpDto>> ResendOtpAsync(ResendRecoveryOtpRequest request, CancellationToken ct = default) => SendCoreAsync(request.ChallengeId, null, true, ct);

    private async Task<Result<RecoverySendOtpDto>> SendCoreAsync(string challengeId, PasswordRecoveryChannel? requestedChannel, bool resend, CancellationToken ct)
    {
        var settingsResult = await branding.GetRecoverySettingsAsync(ct);
        if (!settingsResult.Succeeded || settingsResult.Value is not { PasswordRecoveryEnabled: true } settings) return Result<RecoverySendOtpDto>.Invalid("Password recovery is unavailable.");
        var tenantId = CurrentTenantId;
        var existing = await db.PasswordResetOtps.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.ChallengeIdHash == Hash(challengeId) && (!requestedChannel.HasValue || x.Channel == requestedChannel.Value)).OrderByDescending(x => x.CreatedDate).FirstOrDefaultAsync(ct);
        var channel = requestedChannel ?? existing?.Channel;
        var userId = existing?.UserId;
        if (existing is null)
            return Result<RecoverySendOtpDto>.Success(new(challengeId, requestedChannel ?? PasswordRecoveryChannel.Email, string.Empty, null, SendGeneric), SendGeneric);
        if (userId is not Guid resolvedUserId) return Result<RecoverySendOtpDto>.Invalid("Password recovery is unavailable.");
        var userRecord = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == resolvedUserId && x.IsActive, ct);
        if (userRecord is null) return Result<RecoverySendOtpDto>.Success(new(challengeId, channel ?? PasswordRecoveryChannel.Email, string.Empty, null, SendGeneric), SendGeneric);
        var destination = channel == PasswordRecoveryChannel.Email ? userRecord.Email : await TrustedMobileAsync(userRecord.Id, userRecord.TenantId, ct);
        if (channel is null || (channel == PasswordRecoveryChannel.Email && !settings.AllowEmailOtp) || (channel == PasswordRecoveryChannel.Sms && !settings.AllowSmsOtp) || string.IsNullOrWhiteSpace(destination)) return Result<RecoverySendOtpDto>.Success(new(challengeId, channel ?? PasswordRecoveryChannel.Email, string.Empty, null, SendGeneric), SendGeneric);
        var now = clock.GetUtcNow().UtcDateTime;
        var wasSent = existing is not null && existing.ExpiresAtUtc > existing.LastSentAtUtc;
        if (wasSent && !resend) return Result<RecoverySendOtpDto>.Success(new(challengeId, channel!.Value, string.Empty, null, SendGeneric), SendGeneric);
        if (wasSent && now < existing!.LastSentAtUtc.AddSeconds(settings.OtpResendCooldownSeconds)) return Result<RecoverySendOtpDto>.Invalid("Please wait before requesting another code.");
        if (wasSent && existing!.ResendCount >= settings.OtpMaxResends) return Result<RecoverySendOtpDto>.Invalid("Too many code requests. Please try again later.");
        if (existing is not null) existing.RevokedAtUtc = now;
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var row = existing ?? new PasswordResetOtp { Id = Guid.NewGuid(), TenantId = userRecord.TenantId, UserId = userRecord.Id, Channel = channel.Value, ChallengeIdHash = Hash(challengeId), CorrelationId = Guid.NewGuid() };
        row.OtpHash = hasher.Hash(otp); row.DestinationHash = Hash(destination); row.MaskedDestination = channel == PasswordRecoveryChannel.Email ? MaskEmail(destination) : MaskMobile(destination); row.ExpiresAtUtc = now.AddMinutes(settings.OtpExpiryMinutes); row.LastSentAtUtc = now; row.ResendCount = existing is null ? 0 : existing.ResendCount + 1; row.VerifiedAtUtc = null; row.ConsumedAtUtc = null; row.RevokedAtUtc = null;
        if (existing is null) db.PasswordResetOtps.Add(row);
        var tenantName = (await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userRecord.TenantId, ct))?.TenantName ?? "HRMS";
        var message = new OtpDeliveryMessage(destination, otp, tenantName, userRecord.FirstName, row.ExpiresAtUtc, channel.Value);
        string? developmentOtp;
        try
        {
            developmentOtp = channel == PasswordRecoveryChannel.Email
                ? await emailSender.SendPasswordResetOtpAsync(message, ct)
                : await smsSender.SendAsync(message, ct);
        }
        catch (EmailDeliveryException exception) when (!ct.IsCancellationRequested)
        {
            logger?.LogError(exception, "Password recovery email delivery failed. Kind {FailureKind}, Host {Host}, Port {Port}.", exception.Kind, exception.Host, exception.Port);
            return Result<RecoverySendOtpDto>.Unavailable(SendGeneric);
        }
        await db.PasswordResetOtps.IgnoreQueryFilters().Where(x => x.TenantId == row.TenantId && x.ChallengeIdHash == row.ChallengeIdHash && x.Id != row.Id && x.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.SaveChangesAsync(ct);
        return Result<RecoverySendOtpDto>.Success(new(challengeId, channel.Value, string.Empty, developmentOtp, SendGeneric), SendGeneric);
    }

    public async Task<Result<RecoveryVerificationDto>> VerifyOtpAsync(VerifyRecoveryOtpRequest request, CancellationToken ct = default)
    {
        var row = await db.PasswordResetOtps.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == CurrentTenantId && x.ChallengeIdHash == Hash(request.ChallengeId) && x.VerifiedAtUtc == null && x.ConsumedAtUtc == null && x.RevokedAtUtc == null, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var settings = await branding.GetRecoverySettingsAsync(ct);
        var maxAttempts = settings.Value?.OtpMaxAttempts ?? 5;
        if (row is null || row.ExpiresAtUtc <= now || row.AttemptCount >= maxAttempts) return Result<RecoveryVerificationDto>.Unauthorized(InvalidCode);
        if (!hasher.Verify(row.OtpHash, request.Otp)) { row.AttemptCount++; await db.SaveChangesAsync(ct); return Result<RecoveryVerificationDto>.Unauthorized(InvalidCode); }
        row.VerifiedAtUtc = now;
        var resetToken = Token();
        row.DestinationHash = Hash(resetToken);
        await db.SaveChangesAsync(ct);
        return Result<RecoveryVerificationDto>.Success(new(resetToken, "Verification successful."));
    }

    public async Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var row = await db.PasswordResetOtps.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == CurrentTenantId && x.DestinationHash == Hash(request.ResetToken) && x.VerifiedAtUtc != null && x.ConsumedAtUtc == null && x.RevokedAtUtc == null, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (row is null || row.ExpiresAtUtc <= now) return Result<bool>.Unauthorized("Password reset is invalid or expired.");
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == row.TenantId && x.Id == row.UserId && x.IsActive, ct);
        if (user is null) return Result<bool>.Unauthorized("Password reset is invalid or expired.");
        user.PasswordHash = hasher.Hash(request.NewPassword); row.ConsumedAtUtc = now;
        await db.RefreshTokens.IgnoreQueryFilters().Where(x => x.TenantId == row.TenantId && x.UserId == row.UserId && x.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.PasswordResetOtps.IgnoreQueryFilters().Where(x => x.TenantId == row.TenantId && x.UserId == row.UserId && x.Id != row.Id && x.ConsumedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true, "Password reset successful.");
    }

    public async Task<Result<bool>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not Guid tenantId || tenantContext.UserId is not Guid userId) return Result<bool>.Unauthorized("Authentication is required.");
        var user = await db.Users.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == userId, ct);
        if (user is null || !user.IsActive || !hasher.Verify(user.PasswordHash, request.CurrentPassword)) return Result<bool>.Unauthorized("Current password is incorrect.");
        if (hasher.Verify(user.PasswordHash, request.NewPassword)) return Result<bool>.Invalid("New password must be different from the current password.");
        user.PasswordHash = hasher.Hash(request.NewPassword);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens.Where(x => x.TenantId == tenantId && x.UserId == userId && x.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true, "Password changed successfully.");
    }

    private async Task<User?> ResolveUserAsync(string identifier, TenantRecoverySettingsDto settings, CancellationToken ct)
    {
        var value = identifier.Trim().ToLowerInvariant();
        var modeResult = await branding.GetLoginIdentifierModeAsync(ct);
        var mode = modeResult.Succeeded ? modeResult.Value : TenantLoginIdentifierMode.EmailOrEmployeeCode;
        var user = settings is not null && mode is TenantLoginIdentifierMode.EmailOnly or TenantLoginIdentifierMode.EmailOrEmployeeCode
            ? await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == CurrentTenantId && x.Email.ToLower() == value, ct) : null;
        if (user is not null) return user;
        if (settings is not null && mode is TenantLoginIdentifierMode.EmployeeCodeOnly or TenantLoginIdentifierMode.EmailOrEmployeeCode)
            return await FindByCodeAsync(value, ct);
        return null;
    }

    private Task<User?> FindByCodeAsync(string code, CancellationToken ct) => (from link in db.AccountEmployeeCurrentLinks.IgnoreQueryFilters() join employee in db.Employees.IgnoreQueryFilters() on new { link.TenantId, link.EmployeeId } equals new { employee.TenantId, EmployeeId = employee.Id } join user in db.Users.IgnoreQueryFilters() on new { link.TenantId, link.UserId } equals new { user.TenantId, UserId = user.Id } where link.TenantId == CurrentTenantId && employee.EmployeeCode != null && employee.EmployeeCode.ToLower() == code select user).FirstOrDefaultAsync(ct);
    private async Task<string?> TrustedMobileAsync(Guid userId, Guid tenantId, CancellationToken ct) => await (from link in db.AccountEmployeeCurrentLinks.IgnoreQueryFilters() join employee in db.Employees.IgnoreQueryFilters() on new { link.TenantId, link.EmployeeId } equals new { employee.TenantId, EmployeeId = employee.Id } join contact in db.EmployeeContacts.IgnoreQueryFilters() on new { link.TenantId, link.EmployeeId } equals new { contact.TenantId, EmployeeId = contact.EmployeeId } where link.TenantId == tenantId && link.UserId == userId select contact.OfficialPhone ?? contact.PersonalPhone ?? employee.Phone).FirstOrDefaultAsync(ct);
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string MaskEmail(string value) { var p = value.Split('@'); return p.Length != 2 ? "***" : $"{(p[0].Length > 1 ? p[0][..1] : "*")}***{p[0][^1]}@{p[1]}"; }
    private static string MaskMobile(string value) { var digits = new string(value.Where(char.IsDigit).ToArray()); return digits.Length < 4 ? "****" : $"******{digits[^4..]}"; }
    private Guid? CurrentTenantId => tenantContext.TenantId ?? shardContext.Current?.TenantId;
}
