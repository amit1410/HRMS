using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeePortalAccountService
{
    Task<Result<PortalAccountDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<CreatePortalAccountResponse>> CreateAsync(Guid employeeId, CreatePortalAccountRequest request, CancellationToken cancellationToken = default);
    Task<Result<CreatePortalAccountResponse>> ResendAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<PortalAccountDto>> RevokeAsync(Guid employeeId, CancellationToken cancellationToken = default);
    Task<Result<bool>> SetPasswordAsync(SetPasswordRequest request, CancellationToken cancellationToken = default);
}
