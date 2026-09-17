using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Roles;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Linq.Expressions;
using System.Text;
using RoleDto = HRMS.Application.Abstractions.RoleSummary;

namespace HRMS.Application.Services;

public sealed class RoleAssignmentService : IRoleAssignmentService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly IDatabaseTransientErrorClassifier? _concurrencyClassifier;

    public RoleAssignmentService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IDatabaseTransientErrorClassifier? concurrencyClassifier = null)
    { _db = db; _tenant = tenant; _clock = clock; _concurrencyClassifier = concurrencyClassifier; }

    public async Task<Result<IReadOnlyList<RoleDto>>> GetRolesAsync(bool assignableOnly, CancellationToken ct = default)
    {
        if (!CanManage()) return Result<IReadOnlyList<RoleDto>>.Forbidden("Role assignment permission is required.");
        var roles = await _db.Roles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var items = roles.Where(x => !assignableOnly || x.Name is not (RoleNames.Employee or RoleNames.Manager))
            .Select(x => new RoleDto(x.Id, x.Name, x.Description)).ToList();
        return Result<IReadOnlyList<RoleDto>>.Success(items);
    }

    public async Task<Result<PagedResult<RoleAssignmentDto>>> GetAssignmentsAsync(RoleAssignmentQuery query, CancellationToken ct = default)
    {
        if (!CanManage()) return Result<PagedResult<RoleAssignmentDto>>.Unauthorized("An authenticated tenant and user are required.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize)
            return Result<PagedResult<RoleAssignmentDto>>.Invalid("page", "Page values are out of range.");
        if (!string.IsNullOrWhiteSpace(query.Status) && !RoleAssignmentQuery.Statuses.Contains(query.Status))
            return Result<PagedResult<RoleAssignmentDto>>.Invalid("status", "Status is not supported.");
        if (_tenant.TenantId is not Guid tenantId) return Result<PagedResult<RoleAssignmentDto>>.Unauthorized("An authenticated tenant is required.");

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        var revokedIds = _db.UserRoleAssignmentEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EventType == UserRoleAssignmentEventType.Revoked)
            .Select(x => x.AssignmentId);
        var source = _db.UserRoles.AsNoTracking().Where(x => x.TenantId == tenantId);

        if (query.RoleId is int roleId) source = source.Where(x => x.RoleId == roleId);
        if (query.Source is RoleAssignmentSource assignmentSource) source = source.Where(x => x.AssignmentSource == assignmentSource);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(x =>
                _db.Users.Any(u => u.Id == x.UserId && u.TenantId == tenantId &&
                    (u.FirstName.Contains(search) || u.LastName.Contains(search) || u.Email.Contains(search))) ||
                _db.AccountEmployeeCurrentLinks.Any(link => link.TenantId == tenantId && link.UserId == x.UserId &&
                    _db.Employees.Any(employee => employee.Id == link.EmployeeId && employee.TenantId == tenantId &&
                        ((employee.EmployeeCode ?? "").Contains(search) || employee.FirstName.Contains(search) || employee.LastName.Contains(search)))));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            source = status.Equals("Revoked", StringComparison.OrdinalIgnoreCase)
                ? source.Where(x => revokedIds.Contains(x.Id))
                : source.Where(x => !revokedIds.Contains(x.Id));
            if (status.Equals("Scheduled", StringComparison.OrdinalIgnoreCase)) source = source.Where(x => x.EffectiveFrom > today);
            if (status.Equals("Expired", StringComparison.OrdinalIgnoreCase)) source = source.Where(x => x.EffectiveTo < today);
            if (status.Equals("Active", StringComparison.OrdinalIgnoreCase)) source = source.Where(x => x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today));
        }

        var total = await source.CountAsync(ct);
        List<UserRole> assignments;
        if (_db.IsMySql)
        {
            var pageIds = await GetMySqlAssignmentPageIdsAsync(query, tenantId, today, ct);
            var pageRows = await HydrateMySqlAssignmentsAsync(pageIds, tenantId, ct);
            var byId = pageRows.ToDictionary(x => x.Id);
            assignments = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }
        else
        {
            assignments = await source.Include(x => x.Role).Include(x => x.Scopes)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        }
        if (assignments.Count == 0) return Result<PagedResult<RoleAssignmentDto>>.Success(new([], query.Page, query.PageSize, total));

        var assignmentIds = assignments.Select(x => x.Id).ToArray();
        var userIds = assignments.Select(x => x.UserId).Distinct().ToArray();
        var links = _db.IsMySql
            ? await HydrateMySqlLinksAsync(userIds, tenantId, ct)
            : await _db.AccountEmployeeCurrentLinks.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department)
                .Include(x => x.Employee).ThenInclude(x => x!.Designation)
                .Where(x => x.TenantId == tenantId && userIds.Contains(x.UserId)).ToListAsync(ct);
        var latestRevocations = _db.IsMySql
            ? await HydrateMySqlRevocationsAsync(assignmentIds, tenantId, ct)
            : await _db.UserRoleAssignmentEvents.AsNoTracking()
                .Where(x => x.TenantId == tenantId && assignmentIds.Contains(x.AssignmentId) && x.EventType == UserRoleAssignmentEventType.Revoked)
                .GroupBy(x => x.AssignmentId).Select(x => x.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).First()).ToListAsync(ct);
        var linkByUser = links.ToDictionary(x => x.UserId);
        var revokeByAssignment = latestRevocations.ToDictionary(x => x.AssignmentId);
        var items = assignments.Select(x => Map(x, linkByUser.GetValueOrDefault(x.UserId), revokeByAssignment.GetValueOrDefault(x.Id), today)).ToList();
        return Result<PagedResult<RoleAssignmentDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<RoleManagementCandidateDto>>> GetCandidatesAsync(PagedQuery query, CancellationToken ct = default)
    {
        if (!CanManage()) return Result<PagedResult<RoleManagementCandidateDto>>.Unauthorized("An authenticated tenant and user are required.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize)
            return Result<PagedResult<RoleManagementCandidateDto>>.Invalid("page", "Page values are out of range.");
        if (_tenant.TenantId is not Guid tenantId) return Result<PagedResult<RoleManagementCandidateDto>>.Unauthorized("An authenticated tenant is required.");
        var source = _db.Users.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.Id != _tenant.UserId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(u => u.FirstName.Contains(search) || u.LastName.Contains(search) || u.Email.Contains(search) ||
                _db.AccountEmployeeCurrentLinks.Any(link => link.TenantId == tenantId && link.UserId == u.Id &&
                    _db.Employees.Any(employee => employee.Id == link.EmployeeId && ((employee.EmployeeCode ?? "").Contains(search) || employee.FirstName.Contains(search) || employee.LastName.Contains(search)))));
        }
        var total = await source.CountAsync(ct);
        List<User> users;
        if (_db.IsMySql)
        {
            var pageIds = await GetMySqlCandidatePageIdsAsync(query, tenantId, ct);
            var pageRows = await HydrateMySqlCandidatesAsync(pageIds, tenantId, ct);
            var byId = pageRows.ToDictionary(x => x.Id);
            users = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }
        else
        {
            users = await source.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        }
        var userIds = users.Select(x => x.Id).ToArray();
        var links = _db.IsMySql
            ? await HydrateMySqlLinksAsync(userIds, tenantId, ct)
            : await _db.AccountEmployeeCurrentLinks.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department)
                .Include(x => x.Employee).ThenInclude(x => x!.Designation)
                .Where(x => x.TenantId == tenantId && userIds.Contains(x.UserId)).ToListAsync(ct);
        var linkByUser = links.ToDictionary(x => x.UserId);
        var items = users.Select(user =>
        {
            var employee = linkByUser.GetValueOrDefault(user.Id)?.Employee;
            return new RoleManagementCandidateDto(user.Id, employee?.Id, employee?.EmployeeCode,
                employee is null ? $"{user.FirstName} {user.LastName}".Trim() : string.Join(" ", new[] { employee.FirstName, employee.MiddleName, employee.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                employee?.Department?.Name, employee?.Designation?.Name, employee?.PayrollLocation);
        }).ToList();
        return Result<PagedResult<RoleManagementCandidateDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<RoleAssignmentDto>>> GetUserAssignmentsAsync(Guid userId, CancellationToken ct = default)
    {
        if (!CanManage()) return Result<IReadOnlyList<RoleAssignmentDto>>.Forbidden("Role assignment permission is required.");
        if (_tenant.TenantId is not Guid tenantId || !await _db.Users.AnyAsync(x => x.Id == userId && x.TenantId == tenantId, ct))
            return Result<IReadOnlyList<RoleAssignmentDto>>.NotFound("Account not found.");
        var assignments = await _db.UserRoles.AsNoTracking().Include(x => x.Role).Include(x => x.Scopes)
            .Where(x => x.TenantId == tenantId && x.UserId == userId).OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        return Result<IReadOnlyList<RoleAssignmentDto>>.Success(await Task.WhenAll(assignments.Select(x => MapAsync(x, tenantId, ct))));
    }

    public async Task<Result<RoleAssignmentDto>> AssignAsync(Guid userId, RoleAssignmentRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid actorId)
            return Result<RoleAssignmentDto>.Unauthorized("An authenticated tenant and user are required.");
        if (!CanManage()) return Result<RoleAssignmentDto>.Forbidden("Role assignment permission is required.");
        if (request.EffectiveTo is DateOnly end && end < request.EffectiveFrom)
            return Result<RoleAssignmentDto>.Invalid("effectiveTo", "EffectiveTo cannot be before EffectiveFrom.");
        var role = await _db.Roles.SingleOrDefaultAsync(x => x.Id == request.RoleId, ct);
        if (role is null) return Result<RoleAssignmentDto>.NotFound("Role not found.");
        if (role.Name is RoleNames.Employee or RoleNames.Manager)
            return Result<RoleAssignmentDto>.Invalid("roleId", "Employee and Manager roles are system-managed.");
        if (role.Name == RoleNames.SuperHR && (actorId == userId || !await HasRoleAsync(actorId, RoleNames.SuperHR, tenantId, ct)))
            return Result<RoleAssignmentDto>.Forbidden("Only an existing Super HR may assign Super HR to another account.");
        if (!await _db.Users.AnyAsync(x => x.Id == userId && x.TenantId == tenantId, ct))
            return Result<RoleAssignmentDto>.NotFound("Account not found.");
        var scopes = request.Scopes ?? [];
        if (!ScopesAllowed(role.Name, scopes))
            return Result<RoleAssignmentDto>.Invalid("scopes", "One or more scopes are not allowed for this role.");
        if (scopes.GroupBy(x => (x.ScopeType, x.ScopeEntityId)).Any(x => x.Count() > 1))
            return Result<RoleAssignmentDto>.Invalid("scopes", "Duplicate scopes are not allowed.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var tx = await _db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var overlap = await _db.UserRoles.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId && x.RoleId == role.Id &&
                    x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
                    (x.EffectiveTo == null || request.EffectiveFrom <= x.EffectiveTo), ct);
                if (overlap) return Result<RoleAssignmentDto>.Conflict("The role assignment overlaps an existing assignment.");
                var assignment = new UserRole { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, RoleId = role.Id,
                    EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, AssignmentSource = RoleAssignmentSource.Manual,
                    AssignedByUserId = actorId, AssignmentReason = request.Reason?.Trim(), CreatedAtUtc = _clock.GetUtcNow().UtcDateTime };
                _db.UserRoles.Add(assignment);
                foreach (var scope in scopes)
                {
                    if (!await ScopeExistsAsync(scope.ScopeType, scope.ScopeEntityId, tenantId, ct))
                        return Result<RoleAssignmentDto>.Invalid("scopes", "A scope does not exist in the current tenant.");
                    assignment.Scopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = tenantId, UserRoleAssignmentId = assignment.Id, ScopeType = scope.ScopeType, ScopeEntityId = scope.ScopeEntityId, Assignment = assignment });
                }
                _db.UserRoleAssignmentEvents.Add(Event(assignment, UserRoleAssignmentEventType.Assigned, actorId));
                await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Result<RoleAssignmentDto>.Success(await MapAsync(assignment, tenantId, ct));
            }
            catch (Exception exception) when (_concurrencyClassifier?.IsDeadlock(exception) == true)
            {
                await tx.RollbackAsync(ct);
                _db.ClearChangeTracker();
                if (attempt == 2)
                    return Result<RoleAssignmentDto>.Conflict("The role assignment could not be saved because of a concurrent assignment. Please retry.");
            }
        }
        return Result<RoleAssignmentDto>.Conflict("The role assignment could not be saved because of a concurrent assignment. Please retry.");
    }

    public async Task<Result<RoleAssignmentDto>> RevokeAsync(Guid assignmentId, RoleAssignmentRevokeRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId || _tenant.UserId is not Guid actorId)
            return Result<RoleAssignmentDto>.Unauthorized("An authenticated tenant and user are required.");
        if (!CanManage()) return Result<RoleAssignmentDto>.Forbidden("Role assignment permission is required.");
        var assignment = await _db.UserRoles.Include(x => x.Role).Include(x => x.Scopes).SingleOrDefaultAsync(x => x.Id == assignmentId && x.TenantId == tenantId, ct);
        if (assignment is null) return Result<RoleAssignmentDto>.NotFound("Role assignment not found.");
        if (assignment.Role?.Name is RoleNames.Employee or RoleNames.Manager)
            return Result<RoleAssignmentDto>.Invalid("assignmentId", "System-managed roles cannot be revoked manually.");
        var end = request.EffectiveTo ?? DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        if (end < assignment.EffectiveFrom) return Result<RoleAssignmentDto>.Invalid("effectiveTo", "Revocation date cannot be before assignment start.");
        if (assignment.EffectiveTo is DateOnly existingEnd && end >= existingEnd)
            return Result<RoleAssignmentDto>.Success(await MapAsync(assignment, tenantId, ct), "The role assignment was already revoked.");
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        if (assignment.Role?.Name == RoleNames.SuperHR && end <= today)
        {
            var anotherActive = await _db.UserRoles.AnyAsync(x => x.TenantId == tenantId && x.RoleId == assignment.RoleId && x.Id != assignment.Id &&
                x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today), ct);
            if (!anotherActive)
                return Result<RoleAssignmentDto>.Conflict("The tenant must retain at least one effective Super HR assignment.");
        }
        await using var tx = await _db.BeginTransactionAsync(ct);
        assignment.EffectiveTo = end; assignment.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;
        _db.UserRoleAssignmentEvents.Add(Event(assignment, UserRoleAssignmentEventType.Revoked, actorId, request.Reason));
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result<RoleAssignmentDto>.Success(await MapAsync(assignment, tenantId, ct));
    }

    public async Task<Result<IReadOnlyList<RoleAssignmentHistoryDto>>> GetAssignmentHistoryAsync(Guid assignmentId, CancellationToken ct = default)
    {
        if (!CanManage()) return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.Forbidden("Role assignment permission is required.");
        if (_tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.Unauthorized("An authenticated tenant is required.");
        if (!await _db.UserRoles.AnyAsync(x => x.Id == assignmentId && x.TenantId == tenantId, ct))
            return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.NotFound("Role assignment not found.");
        return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.Success(await HistoryQuery(tenantId, x => x.AssignmentId == assignmentId, ct));
    }

    public async Task<Result<IReadOnlyList<RoleAssignmentHistoryDto>>> GetUserHistoryAsync(Guid userId, CancellationToken ct = default)
    {
        if (!CanManage()) return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.Forbidden("Role assignment permission is required.");
        if (_tenant.TenantId is not Guid tenantId || !await _db.Users.AnyAsync(x => x.Id == userId && x.TenantId == tenantId, ct))
            return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.NotFound("Account not found.");
        return Result<IReadOnlyList<RoleAssignmentHistoryDto>>.Success(await HistoryQuery(tenantId, x => x.UserId == userId, ct));
    }

    private async Task<IReadOnlyList<RoleAssignmentHistoryDto>> HistoryQuery(Guid tenantId, System.Linq.Expressions.Expression<Func<UserRoleAssignmentEvent, bool>> predicate, CancellationToken ct)
        => await _db.UserRoleAssignmentEvents.AsNoTracking().Include(x => x.Assignment).ThenInclude(x => x!.Role)
            .Where(x => x.TenantId == tenantId).Where(predicate)
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
            .Select(x => new RoleAssignmentHistoryDto(x.Id, x.AssignmentId, x.UserId, x.RoleId, x.Assignment!.Role!.Name,
                x.EventType, x.EffectiveFrom, x.EffectiveTo, x.AssignmentSource, x.Reason,
                x.PerformedByUserId, x.OccurredAtUtc)).ToListAsync(ct);

    private bool CanManage() => _tenant.TenantId is Guid && _tenant.UserId is Guid;

    // MySql.EntityFrameworkCore 10.0.9 fails while binding parameterized LIMIT/OFFSET values.
    // Keep filtering and counting in EF, then use validated integer literals for only the bounded page-ID query.
    private async Task<List<Guid>> GetMySqlAssignmentPageIdsAsync(RoleAssignmentQuery query, Guid tenantId, DateOnly today, CancellationToken ct)
    {
        var sql = new StringBuilder("SELECT u.* FROM `UserRoles` AS u WHERE u.`TenantId` = {0}");
        var parameters = new List<object> { tenantId };
        var nextParameter = 1;
        void Add(string template, params object[] values)
        {
            var condition = template;
            foreach (var value in values)
            {
                var marker = condition.IndexOf("{p}", StringComparison.Ordinal);
                if (marker < 0) throw new InvalidOperationException("MySQL query parameter placeholder count did not match its values.");
                condition = condition[..marker] + $"{{{nextParameter}}}" + condition[(marker + 3)..];
                parameters.Add(value);
                nextParameter++;
            }
            sql.Append(" AND ").Append(condition);
        }

        if (query.RoleId is int roleId) Add("u.`RoleId` = {p}", roleId);
        if (query.Source is RoleAssignmentSource source)
        {
            // MySql.EntityFrameworkCore 10.0.9 can leave an enum supplied through
            // FromSqlRaw without a relational type mapping. The column is persisted
            // as int, so pass only the validated database representation.
            var sourceValue = source switch
            {
                RoleAssignmentSource.System => 0,
                RoleAssignmentSource.Manual => 1,
                RoleAssignmentSource.Rule => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(query.Source), source, "Unsupported assignment source.")
            };
            Add("u.`AssignmentSource` = {p}", sourceValue);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            Add("(EXISTS (SELECT 1 FROM `Users` AS su WHERE su.`TenantId` = {p} AND su.`Id` = u.`UserId` AND (su.`FirstName` LIKE CONCAT('%', {p}, '%') OR su.`LastName` LIKE CONCAT('%', {p}, '%') OR su.`Email` LIKE CONCAT('%', {p}, '%'))) OR EXISTS (SELECT 1 FROM `AccountEmployeeCurrentLinks` AS sl INNER JOIN `Employees` AS se ON se.`Id` = sl.`EmployeeId` AND se.`TenantId` = {p} WHERE sl.`TenantId` = {p} AND sl.`UserId` = u.`UserId` AND (se.`EmployeeCode` LIKE CONCAT('%', {p}, '%') OR se.`FirstName` LIKE CONCAT('%', {p}, '%') OR se.`LastName` LIKE CONCAT('%', {p}, '%'))))",
                tenantId, search, search, search, tenantId, tenantId, search, search, search);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            var revoked = "EXISTS (SELECT 1 FROM `UserRoleAssignmentEvents` AS re WHERE re.`TenantId` = {p} AND re.`AssignmentId` = u.`Id` AND re.`EventType` = {p})";
            if (status.Equals("Revoked", StringComparison.OrdinalIgnoreCase))
                Add(revoked, tenantId, (int)UserRoleAssignmentEventType.Revoked);
            else
            {
                Add("NOT " + revoked, tenantId, (int)UserRoleAssignmentEventType.Revoked);
                if (status.Equals("Scheduled", StringComparison.OrdinalIgnoreCase)) Add("u.`EffectiveFrom` > {p}", today);
                if (status.Equals("Expired", StringComparison.OrdinalIgnoreCase)) Add("u.`EffectiveTo` < {p}", today);
                if (status.Equals("Active", StringComparison.OrdinalIgnoreCase)) Add("u.`EffectiveFrom` <= {p} AND (u.`EffectiveTo` IS NULL OR u.`EffectiveTo` >= {p})", today, today);
            }
        }

        return await ExecuteMySqlPageIdsAsync<UserRole>(sql, parameters, query.Page, query.PageSize, "u.`EffectiveFrom` DESC, u.`Id` DESC", ct);
    }

    private async Task<List<Guid>> GetMySqlCandidatePageIdsAsync(PagedQuery query, Guid tenantId, CancellationToken ct)
    {
        var sql = new StringBuilder("SELECT u.* FROM `Users` AS u WHERE u.`TenantId` = {0} AND u.`IsActive` = 1 AND u.`Id` <> {1}");
        var parameters = new List<object> { tenantId, _tenant.UserId!.Value };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            sql.Append(" AND (u.`FirstName` LIKE CONCAT('%', {2}, '%') OR u.`LastName` LIKE CONCAT('%', {2}, '%') OR u.`Email` LIKE CONCAT('%', {2}, '%') OR EXISTS (SELECT 1 FROM `AccountEmployeeCurrentLinks` AS sl INNER JOIN `Employees` AS se ON se.`Id` = sl.`EmployeeId` WHERE sl.`TenantId` = {0} AND sl.`UserId` = u.`Id` AND (se.`EmployeeCode` LIKE CONCAT('%', {2}, '%') OR se.`FirstName` LIKE CONCAT('%', {2}, '%') OR se.`LastName` LIKE CONCAT('%', {2}, '%'))))");
            parameters.Add(search);
        }

        return await ExecuteMySqlPageIdsAsync<User>(sql, parameters, query.Page, query.PageSize, "u.`LastName`, u.`FirstName`, u.`Id`", ct);
    }

    private async Task<List<Guid>> ExecuteMySqlPageIdsAsync<TEntity>(StringBuilder sql, List<object> parameters, int page, int pageSize, string orderBy, CancellationToken ct)
        where TEntity : class
    {
        var offset = checked((page - 1) * pageSize);
        sql.Append(" ORDER BY ").Append(orderBy).Append(" LIMIT ").Append(pageSize).Append(" OFFSET ").Append(offset);
        var query = typeof(TEntity) == typeof(UserRole)
            ? _db.UserRoles.FromSqlRaw(sql.ToString(), parameters.ToArray()).Select(x => x.Id)
            : _db.Users.FromSqlRaw(sql.ToString(), parameters.ToArray()).Select(x => x.Id);
        return await query.ToListAsync(ct);
    }

    private async Task<List<UserRole>> HydrateMySqlAssignmentsAsync(IReadOnlyList<Guid> pageIds, Guid tenantId, CancellationToken ct)
    {
        var ids = BuildMySqlGuidLiteralList(pageIds);
        if (ids is null) return [];
        var sql = $"SELECT u.* FROM `UserRoles` AS u WHERE u.`TenantId` = {{0}} AND u.`Id` IN ({ids})";
        return await _db.UserRoles.FromSqlRaw<UserRole>(sql, tenantId).IgnoreQueryFilters().AsNoTracking().Include(x => x.Role).Include(x => x.Scopes).ToListAsync(ct);
    }

    private async Task<List<User>> HydrateMySqlCandidatesAsync(IReadOnlyList<Guid> pageIds, Guid tenantId, CancellationToken ct)
    {
        var ids = BuildMySqlGuidLiteralList(pageIds);
        if (ids is null) return [];
        var sql = $"SELECT u.* FROM `Users` AS u WHERE u.`TenantId` = {{0}} AND u.`Id` IN ({ids})";
        return await _db.Users.FromSqlRaw<User>(sql, tenantId).IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
    }

    private async Task<List<AccountEmployeeCurrentLink>> HydrateMySqlLinksAsync(IEnumerable<Guid> userIds, Guid tenantId, CancellationToken ct)
    {
        var ids = BuildMySqlGuidLiteralList(userIds);
        if (ids is null) return [];
        var sql = $"SELECT l.* FROM `AccountEmployeeCurrentLinks` AS l WHERE l.`TenantId` = {{0}} AND l.`UserId` IN ({ids})";
        return await _db.AccountEmployeeCurrentLinks.FromSqlRaw<AccountEmployeeCurrentLink>(sql, tenantId)
            .IgnoreQueryFilters().AsNoTracking().Include(x => x.Employee).ThenInclude(x => x!.Department)
            .Include(x => x.Employee).ThenInclude(x => x!.Designation).ToListAsync(ct);
    }

    private async Task<List<UserRoleAssignmentEvent>> HydrateMySqlRevocationsAsync(IEnumerable<Guid> assignmentIds, Guid tenantId, CancellationToken ct)
    {
        var ids = BuildMySqlGuidLiteralList(assignmentIds);
        if (ids is null) return [];
        var sql = $"SELECT e.* FROM `UserRoleAssignmentEvents` AS e WHERE e.`TenantId` = {{0}} AND e.`AssignmentId` IN ({ids}) AND e.`EventType` = {{1}}";
        var events = await _db.UserRoleAssignmentEvents.FromSqlRaw<UserRoleAssignmentEvent>(sql, tenantId, (int)UserRoleAssignmentEventType.Revoked)
            .IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        return events.GroupBy(x => x.AssignmentId)
            .Select(x => x.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).First()).ToList();
    }

    /// <summary>
    /// Oracle's MySQL EF provider cannot bind multi-element Guid collection parameters. These values are
    /// database-derived typed Guid identities, never request text, so only their canonical D-form is
    /// placed in this narrow MySQL hydration predicate. Scalar tenant parameters remain parameterized.
    /// </summary>
    private static string? BuildMySqlGuidLiteralList(IEnumerable<Guid> values)
    {
        var ids = values.Where(x => x != Guid.Empty).Distinct().Select(x => $"'{x:D}'").ToArray();
        return ids.Length == 0 ? null : string.Join(", ", ids);
    }

    private async Task<bool> HasRoleAsync(Guid userId, string roleName, Guid tenantId, CancellationToken ct) => await (from ur in _db.UserRoles join r in _db.Roles on ur.RoleId equals r.Id where ur.TenantId == tenantId && ur.UserId == userId && r.Name == roleName && ur.EffectiveFrom <= DateOnly.FromDateTime(_clock.GetUtcNow().DateTime) && (ur.EffectiveTo == null || ur.EffectiveTo >= DateOnly.FromDateTime(_clock.GetUtcNow().DateTime)) select ur.Id).AnyAsync(ct);
    private async Task<bool> ScopeExistsAsync(RoleScopeType type, Guid id, Guid tenantId, CancellationToken ct) => type switch
    {
        RoleScopeType.HoldingCompany => await _db.HoldingCompanies.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Lob => await _db.LinesOfBusiness.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Organisation => await _db.Organisations.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Department => await _db.Departments.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.SubDepartment => await _db.SubDepartments.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Section => await _db.Sections.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.SubSection => await _db.SubSections.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Function => await _db.Functions.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.SubFunction => await _db.SubFunctions.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Country => await _db.Countries.AnyAsync(x => x.Id == id, ct),
        RoleScopeType.Location => await _db.WorkLocations.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.WorkLocation => await _db.WorkLocations.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.CostCenter => await _db.CostCenters.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Grade => await _db.Grades.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.Designation => await _db.Designations.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        RoleScopeType.EmployeeType => await _db.EmployeeTypes.AnyAsync(x => x.Id == id && x.TenantId == tenantId, ct),
        _ => false
    };
    private static bool ScopesAllowed(string roleName, IReadOnlyList<RoleAssignmentScopeDto> scopes)
    {
        if (scopes.Count == 0) return roleName is not (RoleNames.Employee or RoleNames.Manager);
        var allowed = roleName switch
        {
            RoleNames.HRBP => Enum.GetValues<RoleScopeType>(),
            RoleNames.EmployeeRelationshipOfficer => Enum.GetValues<RoleScopeType>(),
            RoleNames.TimeManager => Enum.GetValues<RoleScopeType>(),
            RoleNames.IT or RoleNames.Accounts or RoleNames.SuperHR => Array.Empty<RoleScopeType>(),
            _ => Array.Empty<RoleScopeType>()
        };
        return scopes.All(x => allowed.Contains(x.ScopeType));
    }
    private static UserRoleAssignmentEvent Event(UserRole x, UserRoleAssignmentEventType type, Guid actor, string? reason = null) => new() { Id = Guid.NewGuid(), TenantId = x.TenantId, AssignmentId = x.Id, UserId = x.UserId, RoleId = x.RoleId, EventType = type, EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo, AssignmentSource = x.AssignmentSource, Reason = reason ?? x.AssignmentReason, PerformedByUserId = actor, OccurredAtUtc = DateTime.UtcNow };
    private async Task<RoleAssignmentDto> MapAsync(UserRole x, Guid tenantId, CancellationToken ct)
    {
        var link = await _db.AccountEmployeeCurrentLinks.AsNoTracking().Include(l => l.Employee)
            .SingleOrDefaultAsync(l => l.TenantId == tenantId && l.UserId == x.UserId, ct);
        var revoked = await _db.UserRoleAssignmentEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.AssignmentId == x.Id && e.EventType == UserRoleAssignmentEventType.Revoked)
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct);
        return Map(x, link, revoked, DateOnly.FromDateTime(_clock.GetUtcNow().DateTime));
    }

    private static RoleAssignmentDto Map(UserRole x, AccountEmployeeCurrentLink? link, UserRoleAssignmentEvent? revoked, DateOnly today)
    {
        var employee = link?.Employee;
        var name = employee is null ? null : string.Join(" ", new[] { employee.FirstName, employee.MiddleName, employee.LastName }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var scopes = (x.Scopes ?? []).Select(s => new RoleAssignmentScopeDto(s.ScopeType, s.ScopeEntityId)).ToList();
        var status = revoked is not null ? "Revoked" : x.EffectiveFrom > today ? "Scheduled" : x.EffectiveTo is DateOnly end && end < today ? "Expired" : "Active";
        return new(x.Id, x.UserId, link?.EmployeeId, employee?.EmployeeCode, name, x.RoleId, x.Role?.Name ?? "", x.AssignmentSource,
            x.EffectiveFrom, x.EffectiveTo, status == "Active", x.AssignmentReason, x.AssignedByUserId, scopes,
            x.AssignmentSource == RoleAssignmentSource.System, x.AssignmentSource != RoleAssignmentSource.System,
            status, revoked is not null, revoked?.EffectiveTo, ScopeSummary(scopes));
    }

    private static string ScopeSummary(IReadOnlyList<RoleAssignmentScopeDto> scopes)
    {
        if (scopes.Count == 0) return "Tenant-wide";
        static string Label(RoleScopeType type) => type switch
        {
            RoleScopeType.HoldingCompany => "Head Company",
            RoleScopeType.Lob => "LOB",
            RoleScopeType.Organisation => "Organization",
            RoleScopeType.WorkLocation => "Work Location",
            _ => type.ToString()
        };
        var first = $"{Label(scopes[0].ScopeType)}: {scopes[0].ScopeEntityId.ToString()[..8]}";
        return scopes.Count == 1 ? first : $"{first} +{scopes.Count - 1}";
    }
}
