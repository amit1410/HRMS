using HRMS.Application.Common;
using HRMS.Domain.Entities;
using System.Linq.Expressions;

namespace HRMS.Application.Abstractions;

public interface IAttendanceAuthorizationService
{
    Task<Result<Expression<Func<Employee, bool>>>> BuildEmployeePredicateAsync(
        string permission,
        bool includeSelf,
        bool includeManager,
        bool includeRoleScope,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> CanAccessEmployeeAsync(
        Guid employeeId,
        string permission,
        bool includeSelf,
        bool includeManager,
        bool includeRoleScope,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);
}
