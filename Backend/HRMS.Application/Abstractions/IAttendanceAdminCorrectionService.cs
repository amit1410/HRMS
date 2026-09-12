using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public sealed record AdminAttendanceCorrectionRequest(Guid EmployeeId, DateOnly BusinessDate, DateTime? CorrectedInAtUtc, DateTime? CorrectedOutAtUtc, string Reason);
public sealed class AdminAttendanceCorrectionQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}
public sealed record AdminAttendanceCorrectionDto(Guid Id, Guid EmployeeId, DateOnly BusinessDate, DateTime? CorrectedInAtUtc, DateTime? CorrectedOutAtUtc, string Reason, Guid CreatedByUserId, DateTime CreatedAtUtc, int CorrectionVersion);

public interface IAttendanceAdminCorrectionService
{
    Task<Result<AdminAttendanceCorrectionDto>> CreateAsync(AdminAttendanceCorrectionRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<AdminAttendanceCorrectionDto>>> ListAsync(AdminAttendanceCorrectionQuery query, CancellationToken ct = default);
    Task<Result<AdminAttendanceCorrectionDto>> GetAsync(Guid id, CancellationToken ct = default);
}
