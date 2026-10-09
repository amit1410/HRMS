using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformAuth;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRMS.Infrastructure.Persistence;

public sealed class PlatformPasswordRecoveryService(
    IHrmsCatalogDbContext catalog,
    IPasswordHasher hasher,
    IEmailSender emailSender,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<PlatformPasswordRecoveryService> logger) : IPlatformPasswordRecoveryService
{
    private const string Generic = "If an eligible platform account exists, instructions have been sent to its verified email address.";
    private const string InvalidToken = "This password reset link is invalid or expired.";

    public async Task<Result<PlatformRecoveryResponse>> ForgotUsernameAsync(PlatformForgotUsernameRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        var user = await catalog.PlatformUsers.SingleOrDefaultAsync(x => x.NormalizedEmail == email && x.IsActive, ct);
        if (user is not null)
            await TrySendAsync(() => emailSender.SendPlatformUsernameReminderAsync(new(user.Email, DisplayName(user), user.Email), ct), "platform username reminder", user.Id, ct);
        return Result<PlatformRecoveryResponse>.Success(new(Generic), Generic);
    }

    public async Task<Result<PlatformRecoveryResponse>> ForgotPasswordAsync(PlatformForgotPasswordRequest request, CancellationToken ct = default)
    {
        var identifier = Normalize(request.Identifier);
        var user = await catalog.PlatformUsers.SingleOrDefaultAsync(x => x.IsActive && x.NormalizedEmail == identifier, ct);
        if (user is not null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await catalog.PlatformPasswordResetTokens.Where(x => x.PlatformUserId == user.Id && x.UsedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAtUtc, now), ct);
            var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            catalog.PlatformPasswordResetTokens.Add(new PlatformPasswordResetToken
            {
                Id = Guid.NewGuid(), PlatformUserId = user.Id, TokenHash = Hash(raw), CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(15)
            });
            await catalog.SaveChangesAsync(ct);
            var baseUrl = configuration["Frontend:PlatformWebUrl"] ?? "http://platform.localhost:5173";
            var resetUrl = $"{baseUrl.TrimEnd('/')}/platform/reset-password?token={Uri.EscapeDataString(raw)}";
            await TrySendAsync(() => emailSender.SendPlatformPasswordResetAsync(new(user.Email, DisplayName(user), resetUrl, now.AddMinutes(15)), ct), "platform password reset", user.Id, ct);
        }
        return Result<PlatformRecoveryResponse>.Success(new(Generic), Generic);
    }

    public async Task<Result<PlatformRecoveryResponse>> ResetPasswordAsync(PlatformResetPasswordRequest request, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(request.Token);
        await using var transaction = await (catalog as DbContext)!.Database.BeginTransactionAsync(ct);
        var token = await catalog.PlatformPasswordResetTokens.SingleOrDefaultAsync(x => x.TokenHash == hash && x.UsedAtUtc == null && x.ExpiresAtUtc > now, ct);
        if (token is null) return Result<PlatformRecoveryResponse>.Unauthorized(InvalidToken);
        var claimed = await catalog.PlatformPasswordResetTokens.Where(x => x.Id == token.Id && x.UsedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAtUtc, now), ct);
        if (claimed != 1) return Result<PlatformRecoveryResponse>.Unauthorized(InvalidToken);
        var user = await catalog.PlatformUsers.SingleOrDefaultAsync(x => x.Id == token.PlatformUserId && x.IsActive, ct);
        if (user is null) return Result<PlatformRecoveryResponse>.Unauthorized(InvalidToken);
        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.SecurityRevision++;
        await catalog.PlatformRefreshTokens.Where(x => x.PlatformUserId == user.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        await catalog.PlatformPasswordResetTokens.Where(x => x.PlatformUserId == user.Id && x.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAtUtc, now), ct);
        await catalog.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await TrySendAsync(() => emailSender.SendPlatformPasswordChangedAsync(new(user.Email, DisplayName(user), now), ct), "platform password changed", user.Id, ct);
        return Result<PlatformRecoveryResponse>.Success(new("Your password has been reset. You can now sign in."), "Password reset successful.");
    }

    private async Task TrySendAsync(Func<Task> send, string kind, Guid userId, CancellationToken ct)
    {
        try { await send(); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Platform recovery email delivery failed for {Kind}, platform user {PlatformUserId}.", kind, userId);
        }
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string DisplayName(PlatformUser user) => Regex.Replace($"{user.FirstName} {user.LastName}".Trim(), "\\s+", " ");
}
