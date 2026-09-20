using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public interface IPayrollApprovalGuard
{
    Task<Result<bool>> ValidateAsync(Guid? makerUserId, string action, string? reason = null, CancellationToken ct = default);
}
