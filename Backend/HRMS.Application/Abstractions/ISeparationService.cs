using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;

namespace HRMS.Application.Abstractions;

public interface ISeparationService
{
    Task<Result<IReadOnlyList<SeparationReasonDto>>> GetReasonsAsync(CancellationToken ct = default);
    Task<Result<SeparationReasonDto>> CreateReasonAsync(SeparationReasonRequest request, CancellationToken ct = default);
    Task<Result<SeparationReasonDto>> UpdateReasonAsync(Guid id, SeparationReasonRequest request, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> CreateSelfAsync(SeparationRequest request, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> CreateForEmployeeAsync(Guid employeeId, HrSeparationRequest request, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> GetCurrentSelfAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetMineAsync(CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetAllAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetTeamAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationEventDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> WithdrawAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> ManagerApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> ManagerRejectAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> HrApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> HrRejectAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<EmployeeSeparationDto>> ReviseLwdAsync(Guid id, SeparationLwdRevisionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetManagerInboxAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetHrInboxAsync(CancellationToken ct = default);
    Task<Result<SeparationNoticeDto>> GetNoticeAsync(Guid id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationEventDto>>> GetNoticeHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<SeparationNoticeDto>> ApplyNoticeWaiverAsync(Guid id, NoticeWaiverRequest request, CancellationToken ct = default);
    Task<Result<SeparationNoticeDto>> ReviseApprovedLwdAsync(Guid id, SeparationLwdRevisionRequest request, CancellationToken ct = default);
}
