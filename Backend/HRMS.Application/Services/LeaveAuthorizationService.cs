using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HRMS.Application.Services;

public sealed class LeaveAuthorizationService(
    IHrmsDbContext db,
    ITenantContext tenantContext,
    IEmployeeIdentityResolver identityResolver,
    IEmployeeAccessScopeService accessScope,
    IEmployeeManagerResolver managerResolver) : ILeaveAuthorizationService
{
    public async Task<Result<Expression<Func<Employee, bool>>>> BuildEmployeePredicateAsync(
        string permission,
        bool includeManager,
        bool includeRoleScope,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveContextAsync(cancellationToken);
        if (!context.Succeeded || context.Value is null)
            return Result<Expression<Func<Employee, bool>>>.Failure(context.Status, context.Message, context.Errors);

        if (!await HasPermissionAsync(context.Value.TenantId, context.Value.UserId, permission, effectiveDate, cancellationToken))
            return Result<Expression<Func<Employee, bool>>>.Forbidden("The authenticated account lacks the required Leave permission.");

        var employee = Expression.Parameter(typeof(Employee), "employee");
        Expression? body = null;
        if (includeRoleScope)
            body = ReplaceParameter(await accessScope.BuildRoleScopePredicateAsync(effectiveDate, cancellationToken), employee);

        if (includeManager)
        {
            var identity = await identityResolver.ResolveCurrentAsync(cancellationToken);
            if (identity.Succeeded && identity.Value is not null)
            {
                var managerPredicate = BuildCurrentManagerPredicate(employee, identity.Value.EmployeeId, effectiveDate);
                body = body is null ? managerPredicate : Expression.OrElse(body, managerPredicate);
            }
        }

        return Result<Expression<Func<Employee, bool>>>.Success(
            Expression.Lambda<Func<Employee, bool>>(body ?? Expression.Constant(false), employee));
    }

    public async Task<Result<bool>> CanAccessEmployeeAsync(
        Guid employeeId,
        string permission,
        bool includeSelf,
        bool includeManager,
        bool includeRoleScope,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveContextAsync(cancellationToken);
        if (!context.Succeeded || context.Value is null)
            return Result<bool>.Failure(context.Status, context.Message, context.Errors);

        if (!await HasPermissionAsync(context.Value.TenantId, context.Value.UserId, permission, effectiveDate, cancellationToken))
            return Result<bool>.Forbidden("The authenticated account lacks the required Leave permission.");

        RuntimeEmployeeIdentity? identity = null;
        if (includeSelf || includeManager)
        {
            var resolved = await identityResolver.ResolveCurrentAsync(cancellationToken);
            if (resolved.Succeeded)
                identity = resolved.Value;
        }

        if (includeSelf && identity is not null && employeeId == identity.EmployeeId)
            return Result<bool>.Success(true);

        if (includeRoleScope)
        {
            var roleScopePredicate = await accessScope.BuildRoleScopePredicateAsync(effectiveDate, cancellationToken);
            if (await db.Employees.AsNoTracking().Where(roleScopePredicate).AnyAsync(x => x.Id == employeeId, cancellationToken))
                return Result<bool>.Success(true);
        }

        if (includeManager)
        {
            var manager = await managerResolver.ResolveAsync(employeeId, effectiveDate, cancellationToken);
            if (manager.Succeeded && manager.Value?.Status == EmployeeManagerResolutionStatus.Resolved &&
                identity is not null && manager.Value.ManagerId == identity.EmployeeId)
                return Result<bool>.Success(true);
        }

        return Result<bool>.Success(false);
    }

    private async Task<Result<AuthorizationContext>> ResolveContextAsync(CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not Guid tenantId || tenantContext.UserId is not Guid userId)
            return Result<AuthorizationContext>.Unauthorized("An authenticated tenant and account are required.");

        var active = await db.Users.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.Id == userId && x.IsActive, cancellationToken);
        return active
            ? Result<AuthorizationContext>.Success(new(tenantId, userId))
            : Result<AuthorizationContext>.Forbidden("The authenticated account is not active.");
    }

    private async Task<bool> HasPermissionAsync(
        Guid tenantId,
        Guid userId,
        string permission,
        DateOnly effectiveDate,
        CancellationToken cancellationToken) => await (
            from userRole in db.UserRoles.AsNoTracking()
            join rolePermission in db.RolePermissions.AsNoTracking() on userRole.RoleId equals rolePermission.RoleId
            join permissionRow in db.Permissions.AsNoTracking() on rolePermission.PermissionId equals permissionRow.Id
            where userRole.TenantId == tenantId && userRole.UserId == userId &&
                  userRole.EffectiveFrom <= effectiveDate &&
                  (userRole.EffectiveTo == null || userRole.EffectiveTo >= effectiveDate) &&
                  permissionRow.Name == permission
            select permissionRow.Id).AnyAsync(cancellationToken);

    private sealed record AuthorizationContext(Guid TenantId, Guid UserId);

    private static Expression BuildCurrentManagerPredicate(ParameterExpression employee, Guid managerId, DateOnly effectiveDate)
    {
        var history = Expression.Parameter(typeof(EmployeeEmploymentHistory), "history");
        var effective = Expression.AndAlso(
            Expression.Not(Expression.Property(history, nameof(EmployeeEmploymentHistory.IsSuperseded))),
            Expression.AndAlso(
                Expression.LessThanOrEqual(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveFrom)), Expression.Constant(effectiveDate)),
                Expression.OrElse(
                    Expression.Equal(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveTo)), Expression.Constant(null, typeof(DateOnly?))),
                    Expression.GreaterThanOrEqual(Expression.Property(history, nameof(EmployeeEmploymentHistory.EffectiveTo)), Expression.Convert(Expression.Constant(effectiveDate), typeof(DateOnly?))))));
        var manager = Expression.AndAlso(effective,
            Expression.Equal(Expression.Property(history, nameof(EmployeeEmploymentHistory.ManagerId)), Expression.Constant(managerId, typeof(Guid?))));
        return Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Any), [typeof(EmployeeEmploymentHistory)],
            Expression.Property(employee, nameof(Employee.EmploymentHistory)),
            Expression.Lambda<Func<EmployeeEmploymentHistory, bool>>(manager, history));
    }

    private static Expression ReplaceParameter(Expression<Func<Employee, bool>> predicate, ParameterExpression parameter)
    {
        return new ParameterReplaceVisitor(predicate.Parameters[0], parameter).Visit(predicate.Body)!;
    }

    private sealed class ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }
}
