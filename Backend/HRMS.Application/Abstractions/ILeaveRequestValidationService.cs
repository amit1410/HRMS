using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;

namespace HRMS.Application.Abstractions;

public interface ILeaveRequestValidationService
{
    Task<Result<LeaveRequestValidationResult>> ValidateAsync(
        LeaveRequestValidationInput input,
        CancellationToken cancellationToken = default);
}
