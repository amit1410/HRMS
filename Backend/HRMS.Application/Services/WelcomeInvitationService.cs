using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Communication;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HRMS.Application.Services;

public sealed class WelcomeInvitationService : IWelcomeInvitationService
{
    private sealed record EmployeeListItem(Guid Id, string? EmployeeCode, string FirstName, string LastName, string Email, DateOnly DateOfJoining, EmployeeStatus Status);
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEmployeePortalAccountService _accounts;
    private readonly TimeProvider _clock;
    private readonly ILogger<WelcomeInvitationService>? _logger;

    public WelcomeInvitationService(IHrmsDbContext db, ITenantContext tenant, IEmployeePortalAccountService accounts, TimeProvider clock, ILogger<WelcomeInvitationService>? logger = null)
    {
        _db = db;
        _tenant = tenant;
        _accounts = accounts;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<WelcomeInvitationList>> ListAsync(WelcomeInvitationQuery query, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<WelcomeInvitationList>.Unauthorized("No authenticated tenant.");
        var employees = _db.Employees.AsNoTracking().Where(x => x.TenantId == _tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            employees = employees.Where(x => x.EmployeeCode!.ToLower().Contains(search) ||
                (x.FirstName + " " + x.LastName).ToLower().Contains(search) || x.Email.ToLower().Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(query.EmployeeCode)) employees = employees.Where(x => x.EmployeeCode != null && x.EmployeeCode.ToLower().Contains(query.EmployeeCode.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(query.WorkEmail)) employees = employees.Where(x => x.Email.ToLower().Contains(query.WorkEmail.Trim().ToLower()));
        if (query.DepartmentId is Guid departmentId) employees = employees.Where(x => x.DepartmentId == departmentId);
        try
        {
        if (query.HoldingCompanyId is Guid holdingCompanyId) employees = employees.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.HoldingCompanyId == holdingCompanyId));
        if (query.LobId is Guid lobId) employees = employees.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.LobId == lobId));
        if (query.OrganisationId is Guid organisationId) employees = employees.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.OrganisationId == organisationId));
        if (query.WorkLocationId is Guid workLocationId) employees = employees.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.WorkLocationId == workLocationId));
        if (string.Equals(query.PortalStatus, "Active", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => _db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == x.Id && _db.Users.Any(u => u.Id == l.UserId && u.IsActive)));
        if (string.Equals(query.PortalStatus, "InvitationPending", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => _db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == x.Id && _db.Users.Any(u => u.Id == l.UserId && !u.IsActive)));
        if (string.Equals(query.PortalStatus, "NotCreated", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => !_db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == x.Id));
        if (string.Equals(query.InvitationStatus, "NotSent", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => !_db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id));
        if (string.Equals(query.InvitationStatus, "Failed", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id && a.DeliveryStatus == WelcomeInvitationStatus.Failed));
        if (string.Equals(query.InvitationStatus, "Pending", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id && (a.DeliveryStatus == WelcomeInvitationStatus.Queued || a.DeliveryStatus == WelcomeInvitationStatus.Processing)));
        if (string.Equals(query.InvitationStatus, "Sent", StringComparison.OrdinalIgnoreCase)) employees = employees.Where(x => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id && a.DeliveryStatus == WelcomeInvitationStatus.Sent));
        if (query.JoiningDateFrom is DateOnly from) employees = employees.Where(x => x.DateOfJoining >= from);
        if (query.JoiningDateTo is DateOnly to) employees = employees.Where(x => x.DateOfJoining <= to);
        }
        catch (Exception ex) { LogFailure("Employee history filtering", ex, null); throw; }
        var total = await RunStageAsync("Employee count", () => employees.CountAsync(ct), employees);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var employeeQuery = employees.OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EmployeeListItem(x.Id, x.EmployeeCode, x.FirstName, x.LastName, x.Email, x.DateOfJoining, x.Status));
        var employeeRows = await RunStageAsync("Sorted/paginated employee retrieval", () => employeeQuery.ToListAsync(ct), employeeQuery);
        var rows = await BuildRowsAsync(employeeRows, ct, LogFailure);
        if (!string.IsNullOrWhiteSpace(query.InvitationStatus)) rows = rows.Where(x => x.InvitationStatus.Equals(query.InvitationStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(query.PortalStatus)) rows = rows.Where(x => x.PortalAccountStatus.Equals(query.PortalStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        var summary = await BuildSummaryAsync(ct, LogFailure);
        return Result<WelcomeInvitationList>.Success(new(new PagedResult<WelcomeInvitationRow>(rows, page, pageSize, total), summary));
    }

    public async Task<Result<WelcomeInvitationPreview>> PreviewAsync(WelcomeInvitationSelectionRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<WelcomeInvitationPreview>.Unauthorized("No authenticated tenant.");
        var rows = request.SelectAllMatching && request.Filter is not null
            ? await BuildRowsForFilterAsync(request.Filter, ct)
            : await BuildRowsForIdsAsync(request.EmployeeIds.Distinct().ToArray(), ct);
        var missing = rows.Count(x => string.IsNullOrWhiteSpace(x.WorkEmail));
        var active = rows.Count(x => x.PortalAccountStatus == "Active");
        var already = rows.Count(x => x.InvitationStatus is "Sent" or "Pending");
        var eligible = rows.Count(x => request.Resend ? x.CanResend || x.CanRetry : x.CanSend);
        return Result<WelcomeInvitationPreview>.Success(new(request.EmployeeIds.Count, eligible, rows.Count - eligible, missing, active, already, []));
    }

    public async Task<Result<WelcomeInvitationSendResult>> SendAsync(WelcomeInvitationSelectionRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null || _tenant.UserId is null) return Result<WelcomeInvitationSendResult>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) return Result<WelcomeInvitationSendResult>.Invalid("idempotencyKey", "An idempotency key is required.");
        if (request.Filter is not null && !request.Confirmed) return Result<WelcomeInvitationSendResult>.Invalid("confirmed", "Explicit confirmation is required for filter-based selection.");
        var ids = request.EmployeeIds.Distinct().ToArray();
        if (request.SelectAllMatching && request.Filter is not null)
            ids = await FilteredEmployees(request.Filter).OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id).Take(500).Select(x => x.Id).ToArrayAsync(ct);
        if (ids.Length == 0 || ids.Length > 500) return Result<WelcomeInvitationSendResult>.Invalid("employeeIds", "Select between 1 and 500 employees.");
        var existing = await _db.WelcomeInvitationBatches.SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return Result<WelcomeInvitationSendResult>.Success(new(existing.Id, existing.TotalRequested, existing.TotalAccepted, existing.TotalRejected, []), "The request was already accepted.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var batch = new WelcomeInvitationBatch { Id = Guid.NewGuid(), TenantId = _tenant.TenantId.Value, RequestedByUserId = _tenant.UserId.Value, RequestedAtUtc = now, TotalRequested = ids.Length, Status = WelcomeInvitationBatchStatus.Queued, CorrelationId = Guid.NewGuid().ToString("N"), IdempotencyKey = request.IdempotencyKey.Trim() };
        var employees = await _db.Employees.AsNoTracking().Where(x => x.TenantId == _tenant.TenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var rows = await BuildRowsForIdsAsync(ids, ct);
        var rowById = rows.ToDictionary(x => x.EmployeeId);
        var messages = new List<string>();
        var accepted = 0;
        foreach (var employeeId in ids)
        {
            if (!employees.TryGetValue(employeeId, out var employee)) { messages.Add($"{employeeId}: employee not found in this tenant."); continue; }
            if (string.IsNullOrWhiteSpace(employee.Email)) { messages.Add($"{employee.EmployeeCode}: work email is required."); continue; }
            if (!rowById.TryGetValue(employeeId, out var row) || (!request.Resend && !row.CanSend) || (request.Resend && !(row.CanResend || row.CanRetry)))
            { messages.Add($"{employee.EmployeeCode}: employee is not eligible for this invitation operation."); continue; }
            _db.WelcomeInvitationAttempts.Add(new WelcomeInvitationAttempt { Id = Guid.NewGuid(), TenantId = _tenant.TenantId.Value, EmployeeId = employeeId, BatchId = batch.Id, RequestedByUserId = _tenant.UserId.Value, RequestedAtUtc = now, RecipientEmailSnapshot = employee.Email, AttemptType = request.Resend ? WelcomeInvitationAttemptType.Resend : WelcomeInvitationAttemptType.Initial, DeliveryStatus = WelcomeInvitationStatus.Queued, CorrelationId = batch.CorrelationId, IdempotencyKey = $"{request.IdempotencyKey.Trim()}:{employeeId:N}", NextAttemptAtUtc = now });
            accepted++;
        }
        batch.TotalAccepted = accepted; batch.TotalRejected = ids.Length - accepted;
        _db.WelcomeInvitationBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        return Result<WelcomeInvitationSendResult>.Success(new(batch.Id, ids.Length, accepted, batch.TotalRejected, messages), "Welcome invitations queued for delivery.");
    }

    public async Task<Result<WelcomeInvitationBatchStatusDto>> GetBatchStatusAsync(Guid batchId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<WelcomeInvitationBatchStatusDto>.Unauthorized("No authenticated tenant.");
        var batch = await _db.WelcomeInvitationBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId, ct);
        if (batch is null) return Result<WelcomeInvitationBatchStatusDto>.NotFound("Batch not found.");
        var states = await _db.WelcomeInvitationAttempts.AsNoTracking().Where(x => x.BatchId == batchId).GroupBy(x => x.DeliveryStatus).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        int Count(WelcomeInvitationStatus status) => states.SingleOrDefault(x => x.Status == status)?.Count ?? 0;
        return Result<WelcomeInvitationBatchStatusDto>.Success(new(batch.Id, batch.TotalRequested, batch.TotalAccepted, batch.TotalRejected, Count(WelcomeInvitationStatus.Queued), Count(WelcomeInvitationStatus.Processing), Count(WelcomeInvitationStatus.Sent), Count(WelcomeInvitationStatus.Failed), Count(WelcomeInvitationStatus.Sent) + Count(WelcomeInvitationStatus.Failed), batch.Status.ToString(), batch.RequestedAtUtc, batch.CompletedAtUtc));
    }

    public async Task<Result<WelcomeInvitationSendResult>> RetryAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null || _tenant.UserId is null) return Result<WelcomeInvitationSendResult>.Unauthorized("No authenticated tenant.");
        var attempt = await _db.WelcomeInvitationAttempts.Where(x => x.EmployeeId == employeeId && x.DeliveryStatus == WelcomeInvitationStatus.Failed)
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(ct);
        if (attempt is null) return Result<WelcomeInvitationSendResult>.NotFound("No failed invitation delivery exists for this employee.");
        if (attempt.RetryCount >= attempt.MaxAttempts) return Result<WelcomeInvitationSendResult>.Conflict("The invitation retry limit has been exhausted.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var batch = new WelcomeInvitationBatch { Id = Guid.NewGuid(), TenantId = _tenant.TenantId.Value, RequestedByUserId = _tenant.UserId.Value, RequestedAtUtc = now, TotalRequested = 1, TotalAccepted = 1, Status = WelcomeInvitationBatchStatus.Queued, CorrelationId = Guid.NewGuid().ToString("N"), IdempotencyKey = $"retry:{attempt.Id:N}:{now.Ticks}" };
        attempt.DeliveryStatus = WelcomeInvitationStatus.Queued; attempt.NextAttemptAtUtc = now; attempt.BatchId = batch.Id; attempt.RequestedByUserId = _tenant.UserId.Value; attempt.ErrorSummary = null; attempt.ErrorCategory = null; attempt.CompletedAtUtc = null;
        _db.WelcomeInvitationBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        return Result<WelcomeInvitationSendResult>.Success(new(batch.Id, 1, 1, 0, []), "Invitation retry queued.");
    }

    public async Task<Result<PagedResult<WelcomeInvitationHistoryRow>>> HistoryAsync(Guid employeeId, PagedQuery query, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(x => x.TenantId == _tenant.TenantId && x.Id == employeeId, ct)) return Result<PagedResult<WelcomeInvitationHistoryRow>>.NotFound("Employee not found.");
        var source = _db.WelcomeInvitationAttempts.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.RequestedAtUtc);
        var total = await source.CountAsync(ct); var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var items = await source.Skip((page - 1) * size).Take(size).Select(x => new WelcomeInvitationHistoryRow(x.Id, x.RequestedAtUtc, x.AttemptType.ToString(), x.DeliveryStatus.ToString(), x.RecipientEmailSnapshot, x.RequestedByUserId, x.SentAtUtc, x.ErrorSummary)).ToListAsync(ct);
        return Result<PagedResult<WelcomeInvitationHistoryRow>>.Success(new(items, page, size, total));
    }

    private async Task<List<WelcomeInvitationRow>> BuildRowsForIdsAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        if (_db.IsMySql)
        {
            var rows = new List<WelcomeInvitationRow>();
            foreach (var chunk in ids.Distinct().Chunk(500))
            {
                var literals = BuildMySqlGuidLiteralList(chunk);
                var employeeSource = await _db.Employees.FromSqlRaw<Employee>("SELECT e.* FROM `Employees` AS e WHERE e.`Id` IN (" + (literals.Length == 0 ? "NULL" : literals) + ")")
                    .AsNoTracking().Where(x => x.TenantId == _tenant.TenantId).Select(x => new EmployeeListItem(x.Id, x.EmployeeCode, x.FirstName, x.LastName, x.Email, x.DateOfJoining, x.Status)).ToListAsync(ct);
                rows.AddRange(await BuildRowsAsync(employeeSource, ct));
            }
            return rows;
        }
        var selectedIds = ids.ToList();
        var source = await _db.Employees.AsNoTracking().Where(x => x.TenantId == _tenant.TenantId && selectedIds.Contains(x.Id)).Select(x => new EmployeeListItem(x.Id, x.EmployeeCode, x.FirstName, x.LastName, x.Email, x.DateOfJoining, x.Status)).ToListAsync(ct);
        return await BuildRowsAsync(source, ct);
    }

    private async Task<List<WelcomeInvitationRow>> BuildRowsForFilterAsync(WelcomeInvitationFilterContract filter, CancellationToken ct)
    {
        var ids = await FilteredEmployees(filter).OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id).Select(x => x.Id).ToArrayAsync(ct);
        return await BuildRowsForIdsAsync(ids, ct);
    }

    private IQueryable<Employee> FilteredEmployees(WelcomeInvitationFilterContract filter)
    {
        var selected = _db.Employees.AsNoTracking().Where(x => x.TenantId == _tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var search = filter.Search.Trim().ToLower(); selected = selected.Where(x => (x.FirstName + " " + x.LastName).ToLower().Contains(search) || (x.EmployeeCode ?? "").ToLower().Contains(search) || x.Email.ToLower().Contains(search)); }
        if (!string.IsNullOrWhiteSpace(filter.EmployeeCode)) selected = selected.Where(x => (x.EmployeeCode ?? "").ToLower().Contains(filter.EmployeeCode.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(filter.WorkEmail)) selected = selected.Where(x => x.Email.ToLower().Contains(filter.WorkEmail.Trim().ToLower()));
        if (filter.DepartmentId is Guid departmentId) selected = selected.Where(x => x.DepartmentId == departmentId);
        if (filter.HoldingCompanyId is Guid holdingCompanyId) selected = selected.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.HoldingCompanyId == holdingCompanyId));
        if (filter.LobId is Guid lobId) selected = selected.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.LobId == lobId));
        if (filter.OrganisationId is Guid organisationId) selected = selected.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.OrganisationId == organisationId));
        if (filter.WorkLocationId is Guid workLocationId) selected = selected.Where(x => _db.EmployeeEmploymentHistory.Any(h => h.EmployeeId == x.Id && h.EffectiveTo == null && h.WorkLocationId == workLocationId));
        if (filter.JoiningDateFrom is DateOnly from) selected = selected.Where(x => x.DateOfJoining >= from);
        if (filter.JoiningDateTo is DateOnly to) selected = selected.Where(x => x.DateOfJoining <= to);
        if (string.Equals(filter.PortalStatus, "Active", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => _db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == x.Id && _db.Users.Any(u => u.Id == l.UserId && u.IsActive)));
        if (string.Equals(filter.PortalStatus, "InvitationPending", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => _db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == x.Id && _db.Users.Any(u => u.Id == l.UserId && !u.IsActive)));
        if (string.Equals(filter.PortalStatus, "NotCreated", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => !_db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == x.Id));
        if (string.Equals(filter.InvitationStatus, "NotSent", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => !_db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id));
        if (string.Equals(filter.InvitationStatus, "Failed", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id && a.DeliveryStatus == WelcomeInvitationStatus.Failed));
        if (string.Equals(filter.InvitationStatus, "Pending", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id && (a.DeliveryStatus == WelcomeInvitationStatus.Queued || a.DeliveryStatus == WelcomeInvitationStatus.Processing)));
        if (string.Equals(filter.InvitationStatus, "Sent", StringComparison.OrdinalIgnoreCase)) selected = selected.Where(x => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == x.Id && a.DeliveryStatus == WelcomeInvitationStatus.Sent));
        return selected;
    }

    private async Task<List<WelcomeInvitationRow>> BuildRowsAsync(IReadOnlyList<EmployeeListItem> source, CancellationToken ct, Action<string, Exception, IQueryable?>? logFailure = null)
    {
        if (source.Count == 0) return [];
        logFailure ??= (_, _, _) => { };
        var ids = source.Select(x => x.Id).ToList();
        ids = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        var mysqlIds = _db.IsMySql ? BuildMySqlGuidLiteralList(ids) : null;
        // MySql.EntityFrameworkCore 10.0.9 throws in RelationalCommand.CreateDbCommand when
        // binding captured GUID collections in Contains, including List<Guid>. Keep only the
        // bounded ID lists as literals: BuildMySqlGuidLiteralList accepts Guid values, removes
        // duplicates and Guid.Empty, and formats each value with Guid.ToString("D"). Lists are
        // capped at 500. Employee tenant scope below stays a scalar EF parameter; related entities
        // keep their composed tenant query filters. No user-provided SQL text enters these lists.
        var links = ids.Count == 0 ? [] : await RunStageAsync("Portal account enrichment", () =>
        {
            var linksQuery = _db.IsMySql
                ? _db.AccountEmployeeCurrentLinks.FromSqlRaw<AccountEmployeeCurrentLink>("SELECT l.* FROM `AccountEmployeeCurrentLinks` AS l WHERE l.`EmployeeId` IN (" + mysqlIds + ")")
                : _db.AccountEmployeeCurrentLinks.Where(x => ids.Contains(x.EmployeeId));
            return linksQuery.AsNoTracking().ToListAsync(ct);
        }, null, logFailure);
        var userIds = links.Select(l => l.UserId).ToList();
        userIds = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var mysqlUserIds = _db.IsMySql ? BuildMySqlGuidLiteralList(userIds) : null;
        var users = userIds.Count == 0 ? [] : await RunStageAsync("Portal account enrichment", () =>
        {
            var usersQuery = _db.IsMySql
                ? _db.Users.FromSqlRaw<User>("SELECT u.* FROM `Users` AS u WHERE u.`Id` IN (" + mysqlUserIds + ")")
                : _db.Users.Where(x => userIds.Contains(x.Id));
            return usersQuery.AsNoTracking().ToListAsync(ct);
        }, null, logFailure);
        var invitations = userIds.Count == 0 ? [] : await RunStageAsync("Invitation enrichment", () =>
        {
            var invitationsQuery = _db.IsMySql
                ? _db.UserInvitations.FromSqlRaw<UserInvitation>("SELECT i.* FROM `UserInvitations` AS i WHERE i.`UserId` IN (" + mysqlUserIds + ")")
                : _db.UserInvitations.Where(x => userIds.Contains(x.UserId));
            return invitationsQuery.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        }, null, logFailure);
        var attempts = ids.Count == 0 ? [] : await RunStageAsync("Invitation enrichment", () =>
        {
            var attemptsQuery = _db.IsMySql
                ? _db.WelcomeInvitationAttempts.FromSqlRaw<WelcomeInvitationAttempt>("SELECT a.* FROM `WelcomeInvitationAttempts` AS a WHERE a.`EmployeeId` IN (" + mysqlIds + ")")
                : _db.WelcomeInvitationAttempts.Where(x => ids.Contains(x.EmployeeId));
            return attemptsQuery.AsNoTracking().OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        }, null, logFailure);
        return source.Select(x =>
        {
            var link = links.FirstOrDefault(l => l.EmployeeId == x.Id); var user = link is null ? null : users.FirstOrDefault(u => u.Id == link.UserId); var latest = link is null ? null : invitations.FirstOrDefault(i => i.UserId == link.UserId); var attempt = attempts.FirstOrDefault(a => a.EmployeeId == x.Id); var status = user?.IsActive == true ? "Activated" : attempt?.DeliveryStatus == WelcomeInvitationStatus.Failed ? "Failed" : latest is null ? "NotSent" : latest.ExpiresAtUtc < _clock.GetUtcNow().UtcDateTime ? "Failed" : "Pending";
            return new WelcomeInvitationRow(x.Id, x.EmployeeCode ?? string.Empty, $"{x.FirstName} {x.LastName}".Trim(), x.Email, x.DateOfJoining, user is null ? "NotCreated" : user.IsActive ? "Active" : "InvitationPending", status, latest?.LastSentAtUtc ?? attempt?.SentAtUtc, user?.IsActive == true ? latest?.UsedAtUtc : null, user is null && !string.IsNullOrWhiteSpace(x.Email), user is not null && !user.IsActive, attempt?.DeliveryStatus == WelcomeInvitationStatus.Failed || latest is not null && latest.UsedAtUtc is null && latest.ExpiresAtUtc < _clock.GetUtcNow().UtcDateTime);
        }).ToList();
    }

    private static string BuildMySqlGuidLiteralList(IEnumerable<Guid> values)
        => string.Join(", ", values.Where(x => x != Guid.Empty).Distinct().Select(x => $"'{x:D}'"));

    private async Task<WelcomeInvitationSummary> BuildSummaryAsync(CancellationToken ct, Action<string, Exception, IQueryable?> logFailure)
    {
        var baseQuery = _db.Employees.AsNoTracking().Where(e => e.TenantId == _tenant.TenantId);
        var total = await SummaryCount("Summary total employees", baseQuery, () => baseQuery.CountAsync(ct), logFailure);
        var withQuery = baseQuery.Where(e => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == e.Id));
        var withAttempts = await SummaryCount("Summary employees with attempts", withQuery, () => withQuery.CountAsync(ct), logFailure);
        var pendingQuery = baseQuery.Where(e => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == e.Id && (a.DeliveryStatus == WelcomeInvitationStatus.Queued || a.DeliveryStatus == WelcomeInvitationStatus.Processing)));
        var pending = await SummaryCount("Summary pending invitations", pendingQuery, () => pendingQuery.CountAsync(ct), logFailure);
        var sentQuery = baseQuery.Where(e => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == e.Id && a.DeliveryStatus == WelcomeInvitationStatus.Sent));
        var sent = await SummaryCount("Summary sent invitations", sentQuery, () => sentQuery.CountAsync(ct), logFailure);
        var failedQuery = baseQuery.Where(e => _db.WelcomeInvitationAttempts.Any(a => a.EmployeeId == e.Id && a.DeliveryStatus == WelcomeInvitationStatus.Failed));
        var failed = await SummaryCount("Summary failed invitations", failedQuery, () => failedQuery.CountAsync(ct), logFailure);
        var activeQuery = baseQuery.Where(e => _db.AccountEmployeeCurrentLinks.Any(l => l.EmployeeId == e.Id && _db.Users.Any(u => u.Id == l.UserId && u.IsActive)));
        var active = await SummaryCount("Summary active accounts", activeQuery, () => activeQuery.CountAsync(ct), logFailure);
        return new(total - withAttempts, pending, sent, failed, active);
    }

    private Task<int> SummaryCount(string stage, IQueryable query, Func<Task<int>> execute, Action<string, Exception, IQueryable?> logFailure)
        => RunStageAsync(stage, execute, query, logFailure);

    private Task<T> RunStageAsync<T>(string stage, Func<Task<T>> execute, IQueryable? query)
        => RunStageAsync(stage, execute, query, LogFailure);

    private static async Task<T> RunStageAsync<T>(string stage, Func<Task<T>> execute, IQueryable? query, Action<string, Exception, IQueryable?> logFailure)
    {
        try { return await execute(); }
        catch (Exception ex) { logFailure(stage, ex, query); throw; }
    }

    private void LogFailure(string stage, Exception exception, IQueryable? query)
    {
        if (_logger is null) return;
        string? sql = null;
        try { if (query is not null) sql = RedactSql(query.ToQueryString()); } catch { }
        var mysqlNumber = FindMySqlErrorNumber(exception);
        // Do not attach the exception object: provider messages can include SQL parameters or values.
        _logger.LogError("Welcome invitation query failed at stage {QueryStage}; ExceptionType={ExceptionType}; MySqlErrorNumber={MySqlErrorNumber}; CorrelationId={CorrelationId}; Sql={Sql}", stage, exception.GetType().FullName, mysqlNumber, Activity.Current?.TraceId.ToString() ?? Activity.Current?.Id ?? "unavailable", sql ?? "unavailable");
    }

    private static int? FindMySqlErrorNumber(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var property = current.GetType().GetProperty("Number");
            if (property?.PropertyType == typeof(int) && property.GetValue(current) is int number) return number;
        }
        return null;
    }

    private static string RedactSql(string sql)
    {
        // Retain statement shape while removing values, including GUID literals in bounded raw SQL.
        sql = Regex.Replace(sql, "'([^']|'')*'", "'<redacted>'");
        sql = Regex.Replace(sql, "(?im)(=\\s*)([^,;\\r\\n]+)", "$1<redacted>");
        return sql;
    }
}
