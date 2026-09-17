using HRMS.Domain.Entities;
using System.Linq.Expressions;

namespace HRMS.Application.Abstractions;

/// <summary>Builds tenant-safe, server-translatable employee predicates from effective role scopes.</summary>
public interface IEmployeeAccessScopeService
{
    Task<Expression<Func<Employee, bool>>> BuildPredicateAsync(DateOnly effectiveDate, CancellationToken cancellationToken = default);
    Task<bool> CanAccessEmployeeAsync(Guid employeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}
