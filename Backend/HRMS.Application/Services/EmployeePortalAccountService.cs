using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Validators.Auth;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class EmployeePortalAccountService : IEmployeePortalAccountService
{
    private const string InvalidInviteMessage = "This invitation is invalid or has expired.";
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IShardContext _shard;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<EmployeePortalAccountService> _logger;

    public EmployeePortalAccountService(
        IHrmsDbContext db,
        ITenantContext tenant,
        IShardContext shard,
        IPasswordHasher passwordHasher,
        IEmailSender emailSender,
        TimeProvider clock,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<EmployeePortalAccountService> logger)
    {
        _db = db;
        _tenant = tenant;
        _shard = shard;
        _passwordHasher = passwordHasher;
        _emailSender = emailSender;
        _clock = clock;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<Result<PortalAccountDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
            return Result<PortalAccountDto>.Unauthorized("No authenticated tenant.");
        var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<PortalAccountDto>.NotFound("Employee not found.");
        return Result<PortalAccountDto>.Success(await ReadAccountAsync(employeeId, tenantId, cancellationToken));
    }

    public Task<Result<CreatePortalAccountResponse>> CreateAsync(
        Guid employeeId,
        CreatePortalAccountRequest request,
        CancellationToken cancellationToken = default) =>
        CreateOrResendAsync(employeeId, request.LoginEmail, false, cancellationToken);

    public Task<Result<CreatePortalAccountResponse>> ResendAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        CreateOrResendAsync(employeeId, null, true, cancellationToken);

    public async Task<Result<PortalAccountDto>> RevokeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid)
            return Result<PortalAccountDto>.Unauthorized("No authenticated tenant.");
        var employee = await _db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<PortalAccountDto>.NotFound("Employee not found.");
        var link = await _db.AccountEmployeeCurrentLinks.SingleOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);
        if (link is null)
            return Result<PortalAccountDto>.Conflict("This employee has no portal account.");
        var user = await _db.Users.SingleOrDefaultAsync(x => x.Id == link.UserId, cancellationToken);
        if (user is null || user.IsActive)
            return Result<PortalAccountDto>.Conflict("Only a pending invitation can be revoked.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var invitations = await _db.UserInvitations.Where(x => x.UserId == user.Id && x.TenantId == tenantId
            && x.Purpose == UserInvitationPurpose.SetInitialPassword && x.UsedAtUtc == null && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        if (invitations.Count == 0)
            return Result<PortalAccountDto>.Conflict("There is no pending invitation to revoke.");
        foreach (var invitation in invitations) invitation.RevokedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<PortalAccountDto>.Success(await ReadAccountAsync(employeeId, tenantId, cancellationToken), "Invitation revoked.");
    }

    public async Task<Result<bool>> SetPasswordAsync(SetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (_shard.Current is not ShardDescriptor shard)
            return Result<bool>.NotFound(InvalidInviteMessage);
        if (!PasswordPolicy.IsValid(request.Password) || !string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            return Result<bool>.Invalid("Password", PasswordPolicy.Message);

        var now = _clock.GetUtcNow().UtcDateTime;
        var tokenHash = HashToken(request.Token);
        var invitation = await _db.UserInvitations.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.TenantId == shard.TenantId && x.TokenHash == tokenHash && x.Purpose == UserInvitationPurpose.SetInitialPassword,
            cancellationToken);
        if (invitation is null || invitation.UsedAtUtc is not null || invitation.RevokedAtUtc is not null || invitation.ExpiresAtUtc <= now)
            return Result<bool>.Failure(ResultStatus.Unauthorized, InvalidInviteMessage);

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var user = await _db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == invitation.UserId && x.TenantId == shard.TenantId, cancellationToken);
        if (user is null)
            return Result<bool>.Failure(ResultStatus.Unauthorized, InvalidInviteMessage);
        user.PasswordHash = _passwordHasher.Hash(request.Password);
        user.IsActive = true;
        invitation.UsedAtUtc = now;
        var otherInvitations = await _db.UserInvitations.IgnoreQueryFilters().Where(x => x.UserId == user.Id
            && x.TenantId == shard.TenantId && x.Id != invitation.Id && x.UsedAtUtc == null && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var other in otherInvitations) other.RevokedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<bool>.Success(true, "Your password has been set. You can now sign in.");
    }

    private async Task<Result<CreatePortalAccountResponse>> CreateOrResendAsync(Guid employeeId, string? requestedEmail, bool resend, CancellationToken cancellationToken)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid actorId)
            return Result<CreatePortalAccountResponse>.Unauthorized("No authenticated tenant.");
        var employee = await _db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
            return Result<CreatePortalAccountResponse>.NotFound("Employee not found.");
        var email = string.IsNullOrWhiteSpace(requestedEmail) ? employee.Email : requestedEmail;
        if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var address))
            return Result<CreatePortalAccountResponse>.Invalid("email", "A valid employee email is required.");
        email = address.Address.ToLowerInvariant();
        var tenantName = await _db.Tenants.AsNoTracking().Where(x => x.Id == tenantId).Select(x => x.TenantName).SingleOrDefaultAsync(cancellationToken) ?? "HRMS";

        var link = await _db.AccountEmployeeCurrentLinks.SingleOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);
        User? user = link is null ? null : await _db.Users.SingleOrDefaultAsync(x => x.Id == link.UserId, cancellationToken);
        if (!resend && user is not null)
        {
            if (user.IsActive)
                return Result<CreatePortalAccountResponse>.Conflict("This employee already has an active portal account.");
            return Result<CreatePortalAccountResponse>.Success(new(await ReadAccountAsync(employeeId, tenantId, cancellationToken), null, "An invitation is already pending."));
        }
        if (resend && (user is null || user.IsActive))
            return Result<CreatePortalAccountResponse>.Conflict("Only a pending portal invitation can be resent.");

        if (user is null)
        {
            var conflicting = await _db.Users.AnyAsync(x => x.Email.ToLower() == email, cancellationToken);
            if (conflicting)
                return Result<CreatePortalAccountResponse>.Conflict("A user account with this email already exists in this tenant.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var expiryHours = int.TryParse(_configuration["Invitations:InitialPasswordExpiryHours"], out var configuredExpiryHours)
            ? Math.Clamp(configuredExpiryHours, 1, 168)
            : 24;
        var expiry = now.AddHours(expiryHours);
        var token = GenerateToken();
        var inviteUrl = BuildInviteUrl(tenantId, token);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Email = email,
                FirstName = employee.FirstName, LastName = employee.LastName,
                PasswordHash = _passwordHasher.Hash(GenerateToken()), IsActive = false
            };
            _db.Users.Add(user);
            var employeeRoleId = await _db.Roles.Where(x => x.Name == RoleNames.Employee).Select(x => x.Id).SingleAsync(cancellationToken);
            _db.UserRoles.Add(new UserRole { UserId = user.Id, TenantId = tenantId, RoleId = employeeRoleId });
            var linkEvent = new AccountEmployeeLinkEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, SubjectUserId = user.Id, ActorUserId = actorId,
                Sequence = 1, Operation = "Link", NewLinkId = Guid.Empty, AfterEmployeeId = employeeId,
                OccurredAtUtc = now, Reason = "Portal account created from employee profile.", CorrelationId = Guid.NewGuid().ToString("N")
            };
            linkEvent.NewLinkId = linkEvent.Id;
            _db.AccountEmployeeLinkEvents.Add(linkEvent);
            _db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkEvent.Id, TenantId = tenantId, UserId = user.Id, EmployeeId = employeeId });
        }

        var old = await _db.UserInvitations.Where(x => x.UserId == user.Id && x.TenantId == tenantId
            && x.Purpose == UserInvitationPurpose.SetInitialPassword && x.UsedAtUtc == null && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var invitation in old) invitation.RevokedAtUtc = now;
        var created = new UserInvitation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, TokenHash = HashToken(token),
            Purpose = UserInvitationPurpose.SetInitialPassword, CreatedAtUtc = now, ExpiresAtUtc = expiry,
            CreatedByUserId = actorId, LastSentAtUtc = now
        };
        _db.UserInvitations.Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        await _emailSender.SendWelcomeInviteAsync(new(email, employee.FirstName, tenantName, inviteUrl, expiry), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Portal invitation sent by {ActorUserId} for tenant {TenantId}, employee {EmployeeId}, user {UserId}.", actorId, tenantId, employeeId, user.Id);
        return Result<CreatePortalAccountResponse>.Success(new(
            new PortalAccountDto(employeeId, "InvitationPending", user.Id, user.Email, expiry, now),
            _environment.IsDevelopment() ? inviteUrl : null,
            "Welcome invitation sent."));
    }

    private async Task<PortalAccountDto> ReadAccountAsync(Guid employeeId, Guid tenantId, CancellationToken cancellationToken)
    {
        var link = await _db.AccountEmployeeCurrentLinks.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);
        if (link is null) return new(employeeId, "NotCreated", null, null, null, null);
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == link.UserId, cancellationToken);
        if (user is null) return new(employeeId, "NotCreated", null, null, null, null);
        var pending = await _db.UserInvitations.AsNoTracking().Where(x => x.UserId == user.Id && x.TenantId == tenantId
            && x.Purpose == UserInvitationPurpose.SetInitialPassword && x.UsedAtUtc == null && x.RevokedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return new(employeeId, user.IsActive ? "Active" : pending is not null ? "InvitationPending" : "Disabled", user.Id, user.Email, pending?.ExpiresAtUtc, pending?.LastSentAtUtc);
    }

    private string BuildInviteUrl(Guid tenantId, string token)
    {
        var host = _shard.Current?.Host ?? throw new InvalidOperationException("Tenant host was not resolved.");
        var template = _configuration["Frontend:TenantWebUrlTemplate"] ?? "http://{host}";
        return $"{template.Replace("{host}", host, StringComparison.Ordinal).TrimEnd('/')}/set-password?token={Uri.EscapeDataString(token)}";
    }

    private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
