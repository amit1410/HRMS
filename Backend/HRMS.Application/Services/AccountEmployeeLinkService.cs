using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.AccountEmployeeLinks;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AccountEmployeeLinkService : IAccountEmployeeLinkService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly Func<Task>? _beforeSaveHook;

    public AccountEmployeeLinkService(
        IHrmsDbContext db,
        ITenantContext tenant,
        TimeProvider? clock = null,
        Func<Task>? beforeSaveHook = null)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock ?? TimeProvider.System;
        _beforeSaveHook = beforeSaveHook;
    }

    public Task<Result<AccountEmployeeCurrentStateDto>> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        ReadStateAsync(userId, Permissions.AccountEmployeeLink.View, ct);

    public async Task<Result<AccountEmployeeCurrentStateDto>> GetEmployeeAsync(Guid employeeId, CancellationToken ct = default)
    {
        var permission = await CheckAsync(Permissions.AccountEmployeeLink.View, ct);
        if (permission is not null) return Result<AccountEmployeeCurrentStateDto>.Failure(permission.Value.Status, permission.Value.Message);
        var link = await _db.AccountEmployeeCurrentLinks.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        if (link is null) return Result<AccountEmployeeCurrentStateDto>.Success(new(employeeId, "Unlinked", null, null));
        return await ReadStateAsync(link.UserId, null, ct);
    }

    public Task<Result<PagedResult<AccountEmployeeCandidateDto>>> GetUserCandidatesAsync(AccountEmployeeQuery q, CancellationToken ct = default) =>
        CandidatesAsync(q, true, ct);

    public Task<Result<PagedResult<AccountEmployeeCandidateDto>>> GetEmployeeCandidatesAsync(AccountEmployeeQuery q, CancellationToken ct = default) =>
        CandidatesAsync(q, false, ct);

    public async Task<Result<PagedResult<AccountEmployeeLinkEventDto>>> GetHistoryAsync(Guid userId, AccountEmployeeHistoryQuery q, CancellationToken ct = default)
    {
        var permission = await CheckAsync(Permissions.AccountEmployeeLink.ViewHistory, ct);
        if (permission is not null) return Result<PagedResult<AccountEmployeeLinkEventDto>>.Failure(permission.Value.Status, permission.Value.Message);
        if (!ValidPage(q.Page, q.PageSize)) return Result<PagedResult<AccountEmployeeLinkEventDto>>.Invalid("page", "Page values are out of range.");
        var events = _db.AccountEmployeeLinkEvents.AsNoTracking().Where(x => x.SubjectUserId == userId).OrderByDescending(x => x.Sequence);
        var total = await events.CountAsync(ct);
        var items = await events.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(x => new AccountEmployeeLinkEventDto(x.Id, x.Sequence, x.Operation, x.ActorUserId, x.BeforeEmployeeId, x.AfterEmployeeId, x.Reason, x.OccurredAtUtc)).ToListAsync(ct);
        return Result<PagedResult<AccountEmployeeLinkEventDto>>.Success(new(items, q.Page, q.PageSize, total));
    }

    public Task<Result<AccountEmployeeCurrentStateDto>> LinkAsync(Guid userId, AccountEmployeeLinkRequest request, CancellationToken ct = default) =>
        MutateAsync(userId, request.ExpectedRevision, request.Reason, request.EmployeeId, null, "Link", ct);

    public Task<Result<AccountEmployeeCurrentStateDto>> UnlinkAsync(Guid userId, AccountEmployeeUnlinkRequest request, CancellationToken ct = default) =>
        MutateAsync(userId, request.ExpectedRevision, request.Reason, null, request.ExpectedEmployeeId, "Unlink", ct, request.ExpectedLinkId);

    public Task<Result<AccountEmployeeCurrentStateDto>> ReplaceAsync(Guid userId, AccountEmployeeReplaceRequest request, CancellationToken ct = default) =>
        MutateAsync(userId, request.ExpectedRevision, request.Reason, request.NewEmployeeId, request.ExpectedEmployeeId, "Replace", ct, request.ExpectedLinkId);

    private async Task<Result<AccountEmployeeCurrentStateDto>> MutateAsync(Guid userId, Guid? revision, string reason, Guid? newEmployeeId, Guid? expectedEmployeeId, string operation, CancellationToken ct, Guid? expectedLinkId = null)
    {
        var viewPermission = await CheckAsync(Permissions.AccountEmployeeLink.View, ct);
        if (viewPermission is not null) return Result<AccountEmployeeCurrentStateDto>.Failure(viewPermission.Value.Status, viewPermission.Value.Message);
        var permission = await CheckAsync(Permissions.AccountEmployeeLink.Manage, ct);
        if (permission is not null) return Result<AccountEmployeeCurrentStateDto>.Failure(permission.Value.Status, permission.Value.Message);
        if (_tenant.UserId == userId) return Result<AccountEmployeeCurrentStateDto>.Forbidden("Operators cannot change their own account link.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return Result<AccountEmployeeCurrentStateDto>.Invalid("reason", "A reason between 1 and 500 characters is required.");
        if (operation == "Link" && revision is null && expectedLinkId is not null) return Result<AccountEmployeeCurrentStateDto>.Conflict("Link state changed.");

        await using var tx = await _db.BeginTransactionAsync(ct);
        var subject = await _db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (subject is null) return Result<AccountEmployeeCurrentStateDto>.NotFound("Account not found.");
        if (!subject.IsActive) return Result<AccountEmployeeCurrentStateDto>.Conflict("The account is inactive.");
        var current = await _db.AccountEmployeeCurrentLinks.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var latest = await _db.AccountEmployeeLinkEvents.Where(x => x.SubjectUserId == userId).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        var actualRevision = latest?.Id;
        if (actualRevision != revision || (operation != "Link" && (current is null || current.LinkId != expectedLinkId || current.EmployeeId != expectedEmployeeId)))
            return Result<AccountEmployeeCurrentStateDto>.Conflict("Link state changed.");
        if (operation == "Link" && current is not null) return Result<AccountEmployeeCurrentStateDto>.Conflict("The account is already linked.");

        Employee? target = null;
        if (newEmployeeId is Guid targetId)
        {
            target = await _db.Employees.SingleOrDefaultAsync(x => x.Id == targetId, ct);
            if (target is null) return Result<AccountEmployeeCurrentStateDto>.NotFound("Employee not found.");
            if (!await EligibleForNewLinkAsync(target, ct)) return Result<AccountEmployeeCurrentStateDto>.Conflict("Employee is not eligible for a new link.");
            if (await _db.AccountEmployeeCurrentLinks.AnyAsync(x => x.EmployeeId == targetId, ct)) return Result<AccountEmployeeCurrentStateDto>.Conflict("Employee is already linked.");
            if (operation == "Replace" && current?.EmployeeId == targetId) return Result<AccountEmployeeCurrentStateDto>.Conflict("Replacement target must differ.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var evt = new AccountEmployeeLinkEvent { Id = Guid.NewGuid(), TenantId = subject.TenantId, SubjectUserId = userId, ActorUserId = _tenant.UserId!.Value, Sequence = (latest?.Sequence ?? 0) + 1, Operation = operation, PreviousEventId = latest?.Id, PreviousLinkId = current?.LinkId, NewLinkId = operation == "Unlink" ? null : null, BeforeEmployeeId = current?.EmployeeId, AfterEmployeeId = target?.Id, OccurredAtUtc = now, Reason = reason.Trim(), CorrelationId = Guid.NewGuid().ToString("N") };
        evt.NewLinkId = operation == "Unlink" ? null : evt.Id;
        if (operation == "Link") evt.BeforeEmployeeId = null;
        _db.AccountEmployeeLinkEvents.Add(evt);
        if (current is not null) _db.AccountEmployeeCurrentLinks.Remove(current);
        if (target is not null) _db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = evt.Id, TenantId = subject.TenantId, UserId = userId, EmployeeId = target.Id });
        if (_beforeSaveHook is not null) await _beforeSaveHook();
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ReadStateAsync(userId, null, ct);
    }

    private async Task<Result<AccountEmployeeCurrentStateDto>> ReadStateAsync(Guid userId, string? requiredPermission, CancellationToken ct)
    {
        if (requiredPermission is not null) { var p = await CheckAsync(requiredPermission, ct); if (p is not null) return Result<AccountEmployeeCurrentStateDto>.Failure(p.Value.Status, p.Value.Message); }
        var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return Result<AccountEmployeeCurrentStateDto>.NotFound("Account not found.");
        var current = await _db.AccountEmployeeCurrentLinks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var latest = await _db.AccountEmployeeLinkEvents.AsNoTracking().Where(x => x.SubjectUserId == userId).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        if (current is null) return Result<AccountEmployeeCurrentStateDto>.Success(new(userId, "Unlinked", null, latest?.Id));
        var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == current.EmployeeId, ct);
        if (employee is null) return Result<AccountEmployeeCurrentStateDto>.Success(new(userId, "Invalid", null, latest?.Id));
        var creation = await _db.AccountEmployeeLinkEvents.AsNoTracking().SingleAsync(x => x.Id == current.LinkId, ct);
        return Result<AccountEmployeeCurrentStateDto>.Success(new(userId, "Linked", new(current.LinkId, employee.Id, DisplayName(employee), employee.EmployeeCode, creation.ActorUserId, creation.OccurredAtUtc), latest?.Id));
    }

    private async Task<Result<PagedResult<AccountEmployeeCandidateDto>>> CandidatesAsync(AccountEmployeeQuery q, bool users, CancellationToken ct)
    {
        var p = await CheckAsync(Permissions.AccountEmployeeLink.View, ct); if (p is not null) return Result<PagedResult<AccountEmployeeCandidateDto>>.Failure(p.Value.Status, p.Value.Message);
        var m = await CheckAsync(Permissions.AccountEmployeeLink.Manage, ct); if (m is not null) return Result<PagedResult<AccountEmployeeCandidateDto>>.Failure(m.Value.Status, m.Value.Message);
        if (!ValidPage(q.Page, q.PageSize)) return Result<PagedResult<AccountEmployeeCandidateDto>>.Invalid("page", "Page values are out of range.");
        var search = q.Search?.Trim(); if (!string.IsNullOrEmpty(search) && search.Length < 2) return Result<PagedResult<AccountEmployeeCandidateDto>>.Invalid("search", "Search must contain at least two characters.");
        if (users) { var x = _db.Users.AsNoTracking().Where(u => u.IsActive && u.Id != _tenant.UserId); if (!string.IsNullOrEmpty(search)) x = x.Where(u => u.Email.Contains(search) || u.FirstName.Contains(search) || u.LastName.Contains(search)); var total = await x.CountAsync(ct); var items = await x.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).Select(u => new AccountEmployeeCandidateDto(u.Id, u.FirstName + " " + u.LastName, u.Email, null, null)).ToListAsync(ct); return Result<PagedResult<AccountEmployeeCandidateDto>>.Success(new(items,q.Page,q.PageSize,total)); }
        var e = _db.Employees.AsNoTracking().Where(e => !_db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == e.Id)); if (!string.IsNullOrEmpty(search)) e = e.Where(x => (x.EmployeeCode ?? "").Contains(search) || x.FirstName.Contains(search) || x.LastName.Contains(search)); var count = await e.CountAsync(ct); var employees = await e.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).Select(x => new AccountEmployeeCandidateDto(x.Id, x.FirstName + " " + x.LastName, x.Email, x.EmployeeCode, x.Status == EmployeeStatus.Active ? "Eligible" : "Ineligible")).ToListAsync(ct); return Result<PagedResult<AccountEmployeeCandidateDto>>.Success(new(employees,q.Page,q.PageSize,count));
    }

    private async Task<bool> EligibleForNewLinkAsync(Employee e, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        if (e.DateOfJoining > date) return e.Status == EmployeeStatus.Active;
        if (e.Status != EmployeeStatus.Active) return false;
        var history = await _db.EmployeeEmploymentHistory.Where(h => h.EmployeeId == e.Id && h.EffectiveFrom <= date && (h.EffectiveTo == null || h.EffectiveTo >= date)).ToListAsync(ct);
        return history.Count == 1 && history[0].EmploymentStatus == EmployeeStatus.Active;
    }

    private async Task<(ResultStatus Status, string Message)?> CheckAsync(string permission, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid actorId) return (ResultStatus.Unauthorized, "No authenticated tenant.");
        var actor = await _db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actorId, ct); if (actor is null) return (ResultStatus.Unauthorized, "Authenticated account not found."); if (!actor.IsActive) return (ResultStatus.Forbidden, "This account has been deactivated.");
        var allowed = await (from ur in _db.UserRoles where ur.UserId == actorId && ur.TenantId == tenantId join rp in _db.RolePermissions on ur.RoleId equals rp.RoleId join p in _db.Permissions on rp.PermissionId equals p.Id where p.Name == permission select p.Id).AnyAsync(ct);
        return allowed ? null : (ResultStatus.Forbidden, "You do not have permission to perform this action.");
    }

    private static bool ValidPage(int page, int size) => page > 0 && size is >= 1 and <= 50;
    private static string DisplayName(Employee e) => string.Join(" ", new[] { e.FirstName, e.MiddleName, e.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
