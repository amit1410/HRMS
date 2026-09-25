using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed record OvertimePolicyRequest(string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo, int MinimumExtraMinutes, int RoundingMinutes, OvertimeRoundingMode RoundingMode, int? MaximumMinutesPerDay, int? MaximumMinutesPerMonth, bool RequirePreApproval, bool RequirePostApproval, bool AllowNormalWorkingDay, bool AllowWeekOff, bool AllowHoliday, decimal NormalDayMultiplier, decimal WeekOffMultiplier, decimal HolidayMultiplier, string EligibilityMode = "All", Guid? HourlyRateComponentId = null, int? MonthlyWorkMinutes = null);
public sealed record OvertimeRequestRequest(Guid EmployeeId, DateOnly WorkDate, int RequestedMinutes, OvertimeCategory Category, string? Reason);
public sealed record OvertimeDecisionRequest(int? ExpectedConcurrencyVersion = null, string? Reason = null);
public sealed record OvertimeCorrectionRequest(int ApprovedMinutes, string Reason, int? ExpectedConcurrencyVersion = null);
public sealed record OvertimeReopenRequest(string Reason);
public sealed record OvertimePolicyDto(Guid Id, string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo, int MinimumExtraMinutes, int RoundingMinutes, OvertimeRoundingMode RoundingMode, int? MaximumMinutesPerDay, int? MaximumMinutesPerMonth, bool AllowNormalWorkingDay, bool AllowWeekOff, bool AllowHoliday, decimal NormalDayMultiplier, decimal WeekOffMultiplier, decimal HolidayMultiplier, string EligibilityMode, Guid? HourlyRateComponentId, int? MonthlyWorkMinutes);
public sealed record OvertimeRequestDto(Guid Id, Guid EmployeeId, DateOnly WorkDate, int RequestedMinutes, int ActualEligibleMinutes, int ApprovedMinutes, OvertimeCategory Category, OvertimeRequestStatus Status, Guid PolicyId, string? Reason, int ConcurrencyVersion);
public sealed record PayrollOvertimeSnapshotContract(Guid SnapshotId, int Version, Guid AttendanceSnapshotId, int AttendanceVersion, int NormalDayMinutes, int WeekOffMinutes, int HolidayMinutes, int TotalApprovedMinutes, decimal NormalDayMultiplier, decimal WeekOffMultiplier, decimal HolidayMultiplier, Guid? HourlyRateComponentId, int? MonthlyWorkMinutes);

public interface IPayrollOvertimeSnapshotResolver
{
    Task<Result<PayrollOvertimeSnapshotContract?>> ResolveAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
}

public interface IOvertimeService : IPayrollOvertimeSnapshotResolver
{
    Task<Result<OvertimePolicyDto>> CreatePolicyAsync(OvertimePolicyRequest request, CancellationToken ct = default);
    Task<Result<OvertimeRequestDto>> CreateRequestAsync(OvertimeRequestRequest request, CancellationToken ct = default);
    Task<Result<OvertimeRequestDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<OvertimeRequestDto>> ApproveAsync(Guid id, OvertimeDecisionRequest request, CancellationToken ct = default);
    Task<Result<OvertimeRequestDto>> RejectAsync(Guid id, OvertimeDecisionRequest request, CancellationToken ct = default);
    Task<Result<OvertimeRequestDto>> CorrectAsync(Guid id, OvertimeCorrectionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>> FinalizeAsync(Guid attendancePeriodId, CancellationToken ct = default);
    Task<Result<bool>> ReopenAsync(Guid attendancePeriodId, string reason, CancellationToken ct = default);
}
