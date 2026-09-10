using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveCalendarService
{
    Task<Result<IReadOnlyList<LeaveCalendarEventDto>>> GetAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

public sealed record LeaveCalendarEventDto(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal ChargeableQuantity,
    LeaveRequestStatus Status);
