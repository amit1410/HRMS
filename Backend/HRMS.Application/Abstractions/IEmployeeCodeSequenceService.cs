using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IEmployeeCodeSequenceService
{
    Task<Result<long>> AllocateAsync(Guid ruleId, EmployeeCodeSequenceScope scope, string scopeKey, EmployeeCodeResetPeriod resetPeriod, string periodKey, long startNumber = 1, int incrementBy = 1, CancellationToken cancellationToken = default);
}
