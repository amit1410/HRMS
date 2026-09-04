using HRMS.Application.Common;
using HRMS.Application.DTOs.AccountEmployeeLinks;

namespace HRMS.Application.Abstractions;

public interface IAccountEmployeeLinkService
{
    Task<Result<AccountEmployeeCurrentStateDto>> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<Result<AccountEmployeeCurrentStateDto>> GetEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<Result<PagedResult<AccountEmployeeCandidateDto>>> GetUserCandidatesAsync(AccountEmployeeQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AccountEmployeeCandidateDto>>> GetEmployeeCandidatesAsync(AccountEmployeeQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<AccountEmployeeLinkEventDto>>> GetHistoryAsync(Guid userId, AccountEmployeeHistoryQuery query, CancellationToken ct = default);
    Task<Result<AccountEmployeeCurrentStateDto>> LinkAsync(Guid userId, AccountEmployeeLinkRequest request, CancellationToken ct = default);
    Task<Result<AccountEmployeeCurrentStateDto>> UnlinkAsync(Guid userId, AccountEmployeeUnlinkRequest request, CancellationToken ct = default);
    Task<Result<AccountEmployeeCurrentStateDto>> ReplaceAsync(Guid userId, AccountEmployeeReplaceRequest request, CancellationToken ct = default);
}
