using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Tenants;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PasswordRecoveryServiceTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid User = SeedData.Users[1].Id;
    private static readonly Guid Employee = OrganizationTestHarness.EmployeeId(Tenant, "EMP-001");

    [Fact]
    public async Task Employee_code_flow_resolves_through_link_hashes_otp_and_consumes_reset()
    {
        using var harness = await ArrangeAsync();
        var email = new RecordingEmailSender();
        var sms = new RecordingSmsSender();
        var service = CreateService(harness, email, sms);

        var identified = await service.IdentifyAsync(new ForgotPasswordRequest("EMP-001"));

        Assert.True(identified.Succeeded, identified.Message);
        var challenge = identified.Value!;
        Assert.Contains(challenge.AvailableChannels, x => x.Channel == PasswordRecoveryChannel.Email);
        Assert.DoesNotContain(User.ToString("N"), challenge.ChallengeId);

        var sent = await service.SendOtpAsync(new SendRecoveryOtpRequest(challenge.ChallengeId, PasswordRecoveryChannel.Email));

        Assert.True(sent.Succeeded, sent.Message);
        Assert.NotNull(sent.Value!.DevelopmentOtp);
        Assert.Equal("employee@example.test", email.Destination);

        using (var db = harness.CreateUnscopedContext())
        {
            var row = await db.PasswordResetOtps.IgnoreQueryFilters().SingleAsync(x => x.Channel == PasswordRecoveryChannel.Email);
            Assert.DoesNotContain(sent.Value.DevelopmentOtp!, row.OtpHash, StringComparison.Ordinal);
            Assert.Equal(User, row.UserId);
            Assert.Equal(Tenant, row.TenantId);
        }

        var verified = await service.VerifyOtpAsync(new VerifyRecoveryOtpRequest(challenge.ChallengeId, sent.Value.DevelopmentOtp!));
        Assert.True(verified.Succeeded, verified.Message);

        var reset = await service.ResetPasswordAsync(new ResetPasswordRequest(verified.Value!.ResetToken, "New-password-123!", "New-password-123!"));
        Assert.True(reset.Succeeded, reset.Message);

        using var assertion = harness.CreateUnscopedContext();
        var user = await assertion.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == User);
        Assert.True(new IdentityPasswordHasher().Verify(user.PasswordHash, "New-password-123!"));
        Assert.True(await assertion.PasswordResetOtps.IgnoreQueryFilters().AnyAsync(x => x.ConsumedAtUtc != null));
        Assert.False(await assertion.PasswordResetOtps.IgnoreQueryFilters().AnyAsync(x => x.VerifiedAtUtc != null && x.ConsumedAtUtc == null && x.RevokedAtUtc == null));
    }

    [Fact]
    public async Task Tenant_context_prevents_a_challenge_from_being_used_for_another_tenant()
    {
        using var harness = await ArrangeAsync();
        var service = CreateService(harness, new RecordingEmailSender(), new RecordingSmsSender());
        var identified = await service.IdentifyAsync(new ForgotPasswordRequest("employee@example.test"));
        var challenge = identified.Value!;
        var sent = await service.SendOtpAsync(new SendRecoveryOtpRequest(challenge.ChallengeId, PasswordRecoveryChannel.Email));

        harness.ActAs(SeedData.TenantIds.Demo02);
        var result = await service.VerifyOtpAsync(new VerifyRecoveryOtpRequest(challenge.ChallengeId, sent.Value!.DevelopmentOtp!));

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    private static async Task<OrganizationTestHarness> ArrangeAsync()
    {
        var harness = await OrganizationTestHarness.CreateAsync();
        using var db = harness.CreateContext();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == User);
        user.Email = "employee@example.test";
        var linkId = Guid.NewGuid();
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
        {
            Id = linkId, TenantId = Tenant, SubjectUserId = User, ActorUserId = User,
            Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = Employee,
            OccurredAtUtc = DateTime.UtcNow, Reason = "password recovery test", CorrelationId = Guid.NewGuid().ToString("N")
        });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink
        {
            LinkId = linkId, TenantId = Tenant, UserId = User, EmployeeId = Employee
        });
        await db.SaveChangesAsync();
        return harness;
    }

    private static PasswordRecoveryService CreateService(OrganizationTestHarness harness, IEmailSender email, ISmsOtpSender sms)
    {
        var shard = new ShardContext();
        shard.Use(new ShardDescriptor(Tenant, "DEMO01", "demo01.localhost", "demo01", TenantStatus.Active, DatabaseProviderType.MySql));
        return new PasswordRecoveryService(
            harness.CreateContext(), harness.TenantContext, new FixedBranding(), new IdentityPasswordHasher(), email, sms, harness.Clock, shard);
    }

    private sealed class FixedBranding : ITenantBrandingService
    {
        public Task<Result<TenantBrandingDto>> GetForCurrentOrganizationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantBrandingDto>.Success(TenantBrandingDto.Neutral));

        public Task<Result<TenantLoginIdentifierMode>> GetLoginIdentifierModeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantLoginIdentifierMode>.Success(TenantLoginIdentifierMode.EmailOrEmployeeCode));

        public Task<Result<TenantLoginIdentifierMode>> SetLoginIdentifierModeAsync(TenantLoginIdentifierMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantLoginIdentifierMode>.Success(mode));

        public Task<Result<TenantRecoverySettingsDto>> GetRecoverySettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantRecoverySettingsDto>.Success(new(true, true, true, 5, 5, 60, 5)));

        public Task<Result<TenantRecoverySettingsDto>> SetRecoverySettingsAsync(UpdateTenantRecoverySettingsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantRecoverySettingsDto>.Success(new(request.PasswordRecoveryEnabled, request.AllowEmailOtp, request.AllowSmsOtp, request.OtpExpiryMinutes, request.OtpMaxAttempts, request.OtpResendCooldownSeconds, request.OtpMaxResends)));
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public string? Destination { get; private set; }

        public Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default)
        {
            Destination = message.Destination;
            return Task.FromResult<string?>(message.Otp);
        }
    }

    private sealed class RecordingSmsSender : ISmsOtpSender
    {
        public Task<string?> SendAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) => Task.FromResult<string?>(message.Otp);
    }
}
