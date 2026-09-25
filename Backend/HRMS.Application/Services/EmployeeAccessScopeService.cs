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
        // An unscoped manual assignment is tenant-wide. System-managed assignments with no
        // scopes (for example Manager) are intentionally not tenant-wide; they are handled by
        // the self/direct-report branches below. Keep those two cases separate so a manager-capable
        // user who also has a tenant-wide manual assignment does not produce a null expression.
        if (assignments.Any(x => x.Scopes.Count == 0 && x.AssignmentSource != RoleAssignmentSource.System)) return _ => true;

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
