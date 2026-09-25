using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed class AttendanceRegularizationQuery : PagedQuery
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public sealed record RegularizationRequestInput(DateOnly BusinessDate, AttendanceRegularizationType RequestType, DateTime? ProposedInAtUtc, DateTime? ProposedOutAtUtc, string Reason);
public sealed record OnDutyRequestInput(DateOnly StartDate, DateOnly EndDate, string Reason, string? Purpose = null, string? Location = null);
public sealed record AttendanceWorkflowEventDto(AttendanceRequestEventType EventType, Guid ActorUserId, DateTime OccurredAtUtc, string? Comments);
public sealed record RegularizationDto(Guid Id, Guid EmployeeId, DateOnly BusinessDate, AttendanceRegularizationType RequestType, DateTime? ProposedInAtUtc, DateTime? ProposedOutAtUtc, string Reason, AttendanceRequestStatus Status, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc, string? ReviewerComments, IReadOnlyList<AttendanceWorkflowEventDto> Events);
public sealed record OnDutyDto(Guid Id, Guid EmployeeId, DateOnly StartDate, DateOnly EndDate, string Reason, string? Purpose, string? Location, AttendanceRequestStatus Status, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc, string? ReviewerComments, IReadOnlyList<AttendanceWorkflowEventDto> Events);

public interface IAttendanceWorkflowService
{
    Task<Result<RegularizationDto>> SubmitRegularizationAsync(RegularizationRequestInput input, CancellationToken ct = default);
    Task<Result<PagedResult<RegularizationDto>>> GetMyRegularizationsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<RegularizationDto>> GetMyRegularizationAsync(Guid id, CancellationToken ct = default);
    Task<Result<RegularizationDto>> CancelRegularizationAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<RegularizationDto>>> GetManagerRegularizationsAsync(AttendanceRegularizationQuery query, CancellationToken ct = default);
    Task<Result<RegularizationDto>> ApproveRegularizationAsync(Guid id, CancellationToken ct = default);
    Task<Result<RegularizationDto>> RejectRegularizationAsync(Guid id, string comments, CancellationToken ct = default);
    Task<Result<OnDutyDto>> SubmitOnDutyAsync(OnDutyRequestInput input, CancellationToken ct = default);
    Task<Result<PagedResult<OnDutyDto>>> GetMyOnDutyAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<OnDutyDto>> GetMyOnDutyByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<OnDutyDto>> CancelOnDutyAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<OnDutyDto>>> GetManagerOnDutyAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<OnDutyDto>> ApproveOnDutyAsync(Guid id, CancellationToken ct = default);
    Task<Result<OnDutyDto>> RejectOnDutyAsync(Guid id, string comments, CancellationToken ct = default);
}
