using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IHrConfirmationDashboardService
{
    Task<Result<HrConfirmationDashboardSummaryDto>> GetSummaryAsync(
        HrConfirmationDashboardFilter filter,
        CancellationToken cancellationToken = default);
}

public sealed record HrConfirmationDashboardFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? DepartmentId = null,
    Guid? DesignationId = null);