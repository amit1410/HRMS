using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Tenant configuration for the Phase 9b confirmation/probation reminder scheduler, plus read access to the
/// scheduler execution log. Tenant-scoped via the DbContext filter; never trusts a caller-supplied tenant id.
/// </summary>
public interface IConfirmationSchedulerSettingService
{
    Task<Result<ConfirmationSchedulerSettingDto>> GetAsync(CancellationToken ct = default);
    Task<Result<ConfirmationSchedulerSettingDto>> UpdateAsync(ConfirmationSchedulerSettingRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<ConfirmationReminderDeliveryDto>>> GetExecutionLogAsync(ConfirmationReminderLogQuery query, CancellationToken ct = default);
}
