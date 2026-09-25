using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public sealed record CompOffPolicyRequest(
    string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    bool AllowWeekOff, bool AllowHoliday, bool AllowOvertimeSource,
    int MinimumWorkedMinutes, decimal CreditRatio, CompOffRoundingMode RoundingMode,
    int RoundingMinutes, int? MaximumCreditMinutesPerDay, int? MaximumCreditMinutesPerMonth,
    int? ExpiryDays, int? ExpiryMonths, bool RequireCreditApproval,
    bool AllowPartialDayConsumption = true, int ConsumptionIncrementMinutes = 1,
    CompOffBenefitMode BenefitMode = CompOffBenefitMode.CompOffOnly, string EligibilityMode = "All");

public sealed record CompOffPolicyDto(Guid Id, string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    int MinimumWorkedMinutes, decimal CreditRatio, CompOffRoundingMode RoundingMode,
    int? MaximumCreditMinutesPerDay, int? MaximumCreditMinutesPerMonth, int? ExpiryDays,
    int? ExpiryMonths, bool RequireCreditApproval, CompOffBenefitMode BenefitMode);

public sealed record CompOffEarnRequest(Guid EmployeeId, DateOnly WorkDate, CompOffSourceType SourceType,
    Guid AttendanceDayId, int SourceAttendanceVersion, int? SourceWorkedMinutes = null,
    Guid? SourceOvertimeRequestId = null, Guid? SourceOvertimeSnapshotId = null);

public sealed record CompOffEarningDto(Guid Id, Guid EmployeeId, DateOnly SourceWorkDate,
    CompOffSourceType SourceType, int SourceWorkedMinutes, int EligibleMinutes, int CreditedMinutes,
    CompOffEarningStatus Status, DateOnly? ExpiresOn, Guid PolicyId, int PolicyVersion,
    Guid? SourceAttendanceDayId, int SourceAttendanceVersion);

public sealed record CompOffBalanceDto(Guid EmployeeId, int EarnedMinutes, int AvailableMinutes,
    int ReservedMinutes, int ConsumedMinutes, int ExpiredMinutes);

public sealed record CompOffLedgerDto(Guid Id, Guid EarningId, Guid? LeaveRequestId,
    CompOffLedgerEntryType EntryType, int Minutes, DateOnly EffectiveDate, DateOnly? ExpiresOn,
    string SourceReference);

public sealed class CompOffOperationalQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public Guid? ManagerId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public CompOffSourceType? SourceType { get; set; }
    public CompOffEarningStatus? Status { get; set; }
    public DateOnly? ExpiryFrom { get; set; }
    public DateOnly? ExpiryTo { get; set; }
}

public sealed record CompOffOperationalEarningDto(
    Guid EarningId, Guid EmployeeId, string EmployeeCode, string EmployeeName,
    DateOnly WorkDate, CompOffSourceType SourceType, int EligibleWorkedMinutes,
    int CreditedMinutes, int AvailableMinutes, int ReservedMinutes, int ConsumedMinutes,
    int ExpiredMinutes, DateOnly? ExpiryDate, CompOffEarningStatus Status, Guid PolicyId,
    int PolicyVersion, Guid? AttendanceDayId, int AttendanceVersion, string? CorrectionStatus,
    int? CorrectionDeficitMinutes, bool ApprovalRequired, Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc);

public interface ICompOffService
{
    Task<Result<CompOffPolicyDto>> CreatePolicyAsync(CompOffPolicyRequest request, CancellationToken ct = default);
    Task<Result<CompOffEarningDto>> EarnAsync(CompOffEarnRequest request, CancellationToken ct = default);
    Task<Result<CompOffEarningDto>> ApproveAsync(Guid earningId, CancellationToken ct = default);
    Task<Result<CompOffEarningDto>> RejectAsync(Guid earningId, CancellationToken ct = default);
    Task<Result<CompOffBalanceDto>> GetBalanceAsync(Guid employeeId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CompOffEarningDto>>> GetEarningsAsync(Guid employeeId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CompOffLedgerDto>>> GetLedgerAsync(Guid employeeId, CancellationToken ct = default);
    Task<Result<PagedResult<CompOffOperationalEarningDto>>> GetOperationalAsync(CompOffOperationalQuery query, CancellationToken ct = default);
    Task<Result<bool>> ReserveAsync(Guid leaveRequestId, Guid employeeId, int minutes, CancellationToken ct = default);
    Task<Result<bool>> ConsumeAsync(Guid leaveRequestId, CancellationToken ct = default);
    Task<Result<bool>> ReleaseAsync(Guid leaveRequestId, CancellationToken ct = default);
    Task<Result<bool>> RestoreAsync(Guid leaveRequestId, CancellationToken ct = default);
    Task<Result<int>> ExpireAsync(DateOnly asOfDate, CancellationToken ct = default);
    Task<Result<CompOffEarningDto>> CorrectSourceAsync(Guid earningId, int correctedEligibleMinutes, string reason, CancellationToken ct = default);
}
