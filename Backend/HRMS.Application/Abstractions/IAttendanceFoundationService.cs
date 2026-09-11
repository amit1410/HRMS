using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;

namespace HRMS.Application.Abstractions;

public interface IAttendanceFoundationService
{
    Task<Result<IReadOnlyList<ShiftDto>>> GetShiftsAsync(ShiftQuery query, CancellationToken ct = default);
    Task<Result<ShiftDto>> CreateShiftAsync(ShiftRequest request, CancellationToken ct = default);
    Task<Result<ShiftDto>> UpdateShiftAsync(Guid id, ShiftRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ShiftPatternDto>>> GetPatternsAsync(CancellationToken ct = default);
    Task<Result<ShiftPatternDto>> CreatePatternAsync(ShiftPatternRequest request, CancellationToken ct = default);
    Task<Result<ShiftApplicabilityRequest>> AddApplicabilityAsync(ShiftApplicabilityRequest request, CancellationToken ct = default);
    Task<Result<ShiftResolutionDto>> ResolveAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RosterDayDto>>> AssignRosterAsync(RosterAssignmentRequest request, CancellationToken ct = default);
    Task<Result<RosterUploadBatchDto>> ValidateRosterUploadAsync(string fileName, Stream csv, CancellationToken ct = default);
    Task<Result<RosterUploadBatchDto>> CommitRosterUploadAsync(Guid batchId, CancellationToken ct = default);
}
