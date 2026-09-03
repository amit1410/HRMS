using HRMS.Application.Common;
using HRMS.Application.DTOs.Masters;

namespace HRMS.Application.Abstractions;

public interface IMasterManagementService
{
    Task<Result<MasterManagementPage>> GetAsync(string kind, MasterManagementQuery query, CancellationToken cancellationToken = default);
    Task<Result<MasterManagementRecordDto>> GetByIdAsync(string kind, Guid id, CancellationToken cancellationToken = default);
    Task<Result<MasterManagementRecordDto>> CreateAsync(string kind, MasterManagementRequest request, CancellationToken cancellationToken = default);
    Task<Result<MasterManagementRecordDto>> UpdateAsync(string kind, Guid id, MasterManagementRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(string kind, Guid id, CancellationToken cancellationToken = default);
}
