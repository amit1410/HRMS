using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HRMS.Application.Services;

/// <summary>
/// Resolves employment scope centrally. Scope rows are grouped as OR within a dimension and AND across
/// dimensions; effective assignments are unioned. The resulting predicate stays in SQL and uses equality
/// disjunctions rather than Guid.Contains, which is important for the Oracle MySQL provider.
/// </summary>
public sealed class EmployeeAccessScopeService(
    IHrmsDbContext db,
    ITenantContext tenantContext,
    ICurrentAuthorizationContext? currentAuthorization = null) : IEmployeeAccessScopeService
{
    public async Task<Expression<Func<Employee, bool>>> BuildPredicateAsync(DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        if (tenantContext.UserId is not Guid userId || tenantContext.TenantId is not Guid tenantId)
            return _ => false;

        var assignments = await db.UserRoles.AsNoTracking().Include(x => x.Scopes)
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate))
            .ToListAsync(cancellationToken);
        var linkedEmployeeId = await db.AccountEmployeeCurrentLinks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => (Guid?)x.EmployeeId).SingleOrDefaultAsync(cancellationToken);
        var systemRoleIds = assignments
            .Where(a => a.AssignmentSource == RoleAssignmentSource.System)
            .Select(a => a.RoleId)
            .Distinct()
            .ToArray();
        var managerPermission = systemRoleIds.Length > 0 && await db.RolePermissions.AsNoTracking()
            .Where(x => systemRoleIds.Contains(x.RoleId))
            .AnyAsync(x => x.Permission!.Name == Permissions.Attendance.MonthlyViewTeam, cancellationToken);
        var broadAccessRoleIds = await GetBroadAccessRoleIdsAsync(assignments, cancellationToken);

        // Some trusted callers (including the API's signed test/integration tokens) establish
        // permissions in the principal without materializing role rows in the test store. The
        // endpoint has already enforced one of these permissions; preserve that authorization
        // contract while still requiring the tenant context and keeping all employee queries
        // tenant-filtered by the DbContext.
        if (assignments.Count == 0)
        {
            if (currentAuthorization?.HasAnyPermission(Permissions.Attendance.MonthlyViewAll) == true)
                return _ => true;
            if (currentAuthorization?.HasAnyPermission(Permissions.Attendance.MonthlyViewTeam) == true && linkedEmployeeId is Guid managerId)
                return employee => employee.Id == managerId || employee.EmploymentHistory.Any(history => !history.IsSuperseded && history.ManagerId == managerId);
            var hasEmployeeAccessPermission = currentAuthorization?.HasAnyPermission(
                Permissions.Employee.View,
                Permissions.Employee.Create,
                Permissions.Employee.Edit,
                Permissions.Employee.Delete,
                Permissions.Employee.Export,
                Permissions.Employee.Import,
                Permissions.EmployeeSensitive.View,
                Permissions.EmployeeSensitive.Edit,
                Permissions.EmploymentHistory.View,
                Permissions.EmploymentHistory.Change) == true;
            return hasEmployeeAccessPermission ? _ => true : _ => false;
        }
        // An unscoped assignment whose role actually carries tenant-wide employee-access permissions
        // (TenantAdmin, SuperAdmin, HRAdmin, ...) is tenant-wide, regardless of how the assignment was
        // recorded (AssignmentSource is provenance/audit metadata — who/what created the row — not an
        // authorization scope signal; a System-seeded TenantAdmin assignment, e.g. from tenant
        // provisioning or the QA automation seed, must grant the same access a manually-assigned one
        // does). An unscoped assignment whose role has no such broad permission (for example Manager,
        // whose reach comes from the org hierarchy, not from a dimensional UserRoleScope row) is
        // intentionally not tenant-wide; it is handled by the self/direct-report branches below. Keep
        // those two cases separate so a manager-capable user who also holds a tenant-wide assignment
        // does not produce a null expression.
        if (assignments.Any(x => x.Scopes.Count == 0 && broadAccessRoleIds.Contains(x.RoleId))) return _ => true;

        var employee = Expression.Parameter(typeof(Employee), "employee");
        Expression? body = linkedEmployeeId is Guid self ? Expression.Equal(Expression.Property(employee, nameof(Employee.Id)), Expression.Constant(self)) : null;
        if (managerPermission && linkedEmployeeId is Guid manager)
        {
            var directReport = Expression.Equal(Expression.Property(employee, nameof(Employee.ReportingManagerId)), Expression.Constant(manager, typeof(Guid?)));
            body = body is null ? directReport : Expression.OrElse(body, directReport);
        }
        foreach (var assignment in assignments)
        {
            if (assignment.Scopes.Count == 0)
                continue;

            var history = Expression.Parameter(typeof(EmployeeEmploymentHistory), "history");
            Expression? dimensions = null;
            foreach (var dimension in assignment.Scopes.GroupBy(x => x.ScopeType))
            {
                Expression? alternatives = null;
                foreach (var scope in dimension)
                {
                    var property = PropertyFor(dimension.Key);
                    var equality = Expression.Equal(Expression.Property(history, property), Expression.Constant(scope.ScopeEntityId, typeof(Guid?)));
                    alternatives = alternatives is null ? equality : Expression.OrElse(alternatives, equality);
                }
                dimensions = dimensions is null ? alternatives : Expression.AndAlso(dimensions, alternatives!);
            }
            var effective = Expression.AndAlso(
                Expression.Not(Expression.Property(history, nameof(EmployeeEmploymentHistory.IsSuperseded))),
                Expression.AndAlso(Expression.LessThanOrEqual(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveFrom)), Expression.Constant(effectiveDate)),
                    Expression.OrElse(Expression.Equal(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveTo)), Expression.Constant(null, typeof(DateOnly?))),
                        Expression.GreaterThanOrEqual(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveTo)), Expression.Convert(Expression.Constant(effectiveDate), typeof(DateOnly?))))));
            var predicate = Expression.AndAlso(effective,
                managerPermission && linkedEmployeeId is Guid managerId
                    ? Expression.OrElse(dimensions!, Expression.Equal(Expression.Property(history, nameof(EmployeeEmploymentHistory.ManagerId)), Expression.Constant(managerId, typeof(Guid?))))
                    : dimensions!);
            var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(EmployeeEmploymentHistory)], Expression.Property(employee, nameof(Employee.EmploymentHistory)), Expression.Lambda<Func<EmployeeEmploymentHistory, bool>>(predicate, history));
            body = body is null ? any : Expression.OrElse(body, any);
        }
        return Expression.Lambda<Func<Employee, bool>>(body ?? Expression.Constant(false), employee);
    }

    public async Task<Expression<Func<Employee, bool>>> BuildRoleScopePredicateAsync(DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        if (tenantContext.UserId is not Guid userId || tenantContext.TenantId is not Guid tenantId)
            return _ => false;

        if (currentAuthorization?.HasAnyPermission(Permissions.Attendance.MonthlyViewAll) == true)
            return _ => true;
        if (currentAuthorization?.HasAnyPermission(Permissions.Attendance.MonthlyViewTeam) == true)
        {
            var managerId = await db.AccountEmployeeCurrentLinks.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.UserId == userId)
                .Select(x => (Guid?)x.EmployeeId)
                .SingleOrDefaultAsync(cancellationToken);
            if (managerId is Guid linkedManager)
                return employee => employee.Id == linkedManager || employee.EmploymentHistory.Any(history => !history.IsSuperseded && history.ManagerId == linkedManager && history.EffectiveFrom <= effectiveDate && (history.EffectiveTo == null || history.EffectiveTo >= effectiveDate));
        }

        var assignments = await db.UserRoles.AsNoTracking().Include(x => x.Scopes)
            .Where(x => x.TenantId == tenantId && x.UserId == userId &&
                        x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate))
            .ToListAsync(cancellationToken);

        if (assignments.Any(x => x.Scopes.Count == 0 && x.AssignmentSource != RoleAssignmentSource.System))
            return _ => true;

        var employee = Expression.Parameter(typeof(Employee), "employee");
        Expression? body = null;
        foreach (var assignment in assignments.Where(x => x.Scopes.Count > 0))
        {
            var history = Expression.Parameter(typeof(EmployeeEmploymentHistory), "history");
            Expression? dimensions = null;
            foreach (var dimension in assignment.Scopes.GroupBy(x => x.ScopeType))
            {
                Expression? alternatives = null;
                foreach (var scope in dimension)
                {
                    var property = PropertyFor(dimension.Key);
                    var equality = Expression.Equal(Expression.Property(history, property), Expression.Constant(scope.ScopeEntityId, typeof(Guid?)));
                    alternatives = alternatives is null ? equality : Expression.OrElse(alternatives, equality);
                }

                dimensions = dimensions is null ? alternatives : Expression.AndAlso(dimensions, alternatives!);
            }

            var effective = Expression.AndAlso(
                Expression.Not(Expression.Property(history, nameof(EmployeeEmploymentHistory.IsSuperseded))),
                Expression.AndAlso(
                    Expression.LessThanOrEqual(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveFrom)), Expression.Constant(effectiveDate)),
                    Expression.OrElse(
                        Expression.Equal(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveTo)), Expression.Constant(null, typeof(DateOnly?))),
                        Expression.GreaterThanOrEqual(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveTo)), Expression.Convert(Expression.Constant(effectiveDate), typeof(DateOnly?))))));
            var predicate = Expression.AndAlso(effective, dimensions!);
            var any = Expression.Call(
                typeof(Enumerable), nameof(Enumerable.Any), [typeof(EmployeeEmploymentHistory)],
                Expression.Property(employee, nameof(Employee.EmploymentHistory)),
                Expression.Lambda<Func<EmployeeEmploymentHistory, bool>>(predicate, history));
            body = body is null ? any : Expression.OrElse(body, any);
        }

        return Expression.Lambda<Func<Employee, bool>>(body ?? Expression.Constant(false), employee);
    }

    public async Task<bool> CanAccessEmployeeAsync(Guid employeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        var predicate = await BuildPredicateAsync(effectiveDate, cancellationToken);
        return await db.Employees.AsNoTracking().Where(predicate).AnyAsync(x => x.Id == employeeId, cancellationToken);
    }

    /// <summary>
    /// Role ids (from <paramref name="assignments"/>) that hold at least one permission implying
    /// tenant-wide employee-directory reach. Used only to decide whether an *unscoped* assignment
    /// (no <see cref="UserRoleScope"/> rows) should be treated as tenant-wide — an assignment that
    /// does carry explicit scope rows is always resolved dimensionally regardless of this set.
    /// <para>
    /// A role that holds <see cref="Permissions.Attendance.MonthlyViewTeam"/> but not
    /// <see cref="Permissions.Attendance.MonthlyViewAll"/> is excluded even if it also holds one of the
    /// other broad permissions below: in this codebase <c>MonthlyViewTeam</c> without <c>MonthlyViewAll</c>
    /// is exclusive to Manager (confirmed against <c>SeedData.RolePermissionMap</c>), which is
    /// deliberately granted <see cref="Permissions.Employee.View"/> too (to read its own team's records)
    /// but whose reach must come from the org hierarchy (self + direct reports, handled separately),
    /// never from an unscoped assignment alone. A role that holds both (TenantAdmin holds every
    /// permission) is not excluded — <c>MonthlyViewAll</c> takes precedence, the same ordering the
    /// no-materialized-assignment branch above already uses.
    /// </para>
    /// </summary>
    private async Task<HashSet<int>> GetBroadAccessRoleIdsAsync(IReadOnlyCollection<UserRole> assignments, CancellationToken cancellationToken)
    {
        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToArray();
        if (roleIds.Length == 0)
            return [];

        var broadPermissions = new[]
        {
            Permissions.Employee.View,
            Permissions.Employee.Create,
            Permissions.Employee.Edit,
            Permissions.Employee.Delete,
            Permissions.Employee.Export,
            Permissions.Employee.Import,
            Permissions.EmployeeSensitive.View,
            Permissions.EmployeeSensitive.Edit,
            Permissions.EmploymentHistory.View,
            Permissions.EmploymentHistory.Change,
            Permissions.Attendance.MonthlyViewAll,
        };

        var matches = await db.RolePermissions.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId) &&
                (broadPermissions.Contains(x.Permission!.Name) || x.Permission!.Name == Permissions.Attendance.MonthlyViewTeam))
            .Select(x => new { x.RoleId, x.Permission!.Name })
            .ToListAsync(cancellationToken);

        var broadRoleIds = matches.Where(x => x.Name != Permissions.Attendance.MonthlyViewTeam).Select(x => x.RoleId).ToHashSet();
        var viewAllRoleIds = matches.Where(x => x.Name == Permissions.Attendance.MonthlyViewAll).Select(x => x.RoleId).ToHashSet();
        var teamScopedOnlyRoleIds = matches.Where(x => x.Name == Permissions.Attendance.MonthlyViewTeam).Select(x => x.RoleId)
            .Where(roleId => !viewAllRoleIds.Contains(roleId)).ToHashSet();
        broadRoleIds.ExceptWith(teamScopedOnlyRoleIds);
        return broadRoleIds;
    }

    private static string PropertyFor(RoleScopeType type) => type switch
    {
        RoleScopeType.HoldingCompany => nameof(EmployeeEmploymentHistory.HoldingCompanyId),
        RoleScopeType.Lob => nameof(EmployeeEmploymentHistory.LobId),
        RoleScopeType.Organisation => nameof(EmployeeEmploymentHistory.OrganisationId),
        RoleScopeType.Department => nameof(EmployeeEmploymentHistory.DepartmentId),
        RoleScopeType.SubDepartment => nameof(EmployeeEmploymentHistory.SubDepartmentId),
        RoleScopeType.Section => nameof(EmployeeEmploymentHistory.SectionId),
        RoleScopeType.SubSection => nameof(EmployeeEmploymentHistory.SubSectionId),
        RoleScopeType.Function => nameof(EmployeeEmploymentHistory.FunctionId),
        RoleScopeType.SubFunction => nameof(EmployeeEmploymentHistory.SubFunctionId),
        RoleScopeType.Country => nameof(EmployeeEmploymentHistory.CountryLocationId),
        RoleScopeType.Location or RoleScopeType.WorkLocation => nameof(EmployeeEmploymentHistory.WorkLocationId),
        RoleScopeType.CostCenter => nameof(EmployeeEmploymentHistory.CostCenterId),
        RoleScopeType.Grade => nameof(EmployeeEmploymentHistory.GradeId),
        RoleScopeType.Designation => nameof(EmployeeEmploymentHistory.DesignationId),
        RoleScopeType.EmployeeType => nameof(EmployeeEmploymentHistory.EmployeeTypeId),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported employment scope dimension.")
    };
}
